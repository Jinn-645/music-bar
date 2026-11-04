using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MusicBar.Infrastructure;

namespace MusicBar.Interop;

/// <summary>
/// Makes the Windows 11 taskbar see-through. Its background is a XAML element
/// inside Explorer, so MusicBar gets a small helper (MusicBar.TaskbarTap.dll)
/// loaded there: a hook on the taskbar's thread brings the DLL into Explorer,
/// where it attaches to Explorer's XAML through the public diagnostics API and
/// stays (the approach TranslucentTB takes). The helper keeps the background
/// clear while MusicBar's "clear" event is set; if MusicBar exits or crashes,
/// the event goes away and the taskbar gets its own background back.
/// </summary>
internal static class XamlTaskbar
{
    const string WantEvent = @"Local\MusicBar.TaskbarClear.v1";
    // Must match the helper; bumped whenever the helper changes, so a newer one gets loaded.
    const string ReadyPrefix = @"Local\MusicBar.TaskbarTap.Ready.v2.";
    static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(40);
    static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(35);

    static EventWaitHandle? _want;
    static uint _loadedIn;
    static DateTime _lastAttempt = DateTime.MinValue;
    static int _loading;

    /// <summary>Asks for a clear taskbar; loads the helper into the current Explorer if needed. Cheap to repeat.</summary>
    public static void Clear(IntPtr taskbar)
    {
        _want ??= new EventWaitHandle(false, EventResetMode.ManualReset, WantEvent);
        _want.Set();
        if (taskbar == IntPtr.Zero) return;
        Native.GetWindowThreadProcessId(taskbar, out uint pid);
        if (pid == 0 || pid == _loadedIn) return;
        if (IsLoaded(pid))
        {
            _loadedIn = pid;
            return;
        }
        if (DateTime.Now - _lastAttempt < RetryEvery || Interlocked.CompareExchange(ref _loading, 1, 0) != 0) return;
        _lastAttempt = DateTime.Now;
        Task.Run(() =>
        {
            try
            {
                Load(taskbar, pid);
            }
            finally
            {
                Volatile.Write(ref _loading, 0);
            }
        });
    }

    /// <summary>Gives the taskbar its own background back.</summary>
    public static void Restore() => _want?.Reset();

    static bool IsLoaded(uint pid)
    {
        if (!EventWaitHandle.TryOpenExisting(ReadyPrefix + pid, out var ready)) return false;
        ready.Dispose();
        return true;
    }

    /// <summary>
    /// Hooks the taskbar's thread so Windows loads the helper into Explorer, waits
    /// until the helper reports itself attached, then removes the hook (the helper
    /// keeps itself loaded from then on).
    /// </summary>
    static void Load(IntPtr taskbar, uint pid)
    {
        IntPtr hook = IntPtr.Zero;
        try
        {
            string dll = WriteHelper();
            var module = LoadLibrary(dll);
            var proc = module == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(module, "TapHookProc");
            uint thread = Native.GetWindowThreadProcessId(taskbar, out _);
            if (proc == IntPtr.Zero || thread == 0)
            {
                Log.Error($"Taskbar helper not usable (error {Marshal.GetLastWin32Error()})");
                return;
            }
            hook = SetWindowsHookEx(WH_CALLWNDPROC, proc, module, thread);
            if (hook == IntPtr.Zero)
            {
                Log.Error($"Hooking the taskbar failed (error {Marshal.GetLastWin32Error()})");
                return;
            }
            // Any message through the taskbar's thread loads the DLL there.
            PostMessage(taskbar, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (!IsLoaded(pid) && waited.Elapsed < ReadyTimeout) Thread.Sleep(200);
            if (IsLoaded(pid))
            {
                _loadedIn = pid;
                Log.Info($"Taskbar helper attached to Explorer ({pid}) in {waited.ElapsedMilliseconds} ms");
            }
            else
            {
                Log.Error($"Taskbar helper did not attach to Explorer ({pid}) within {ReadyTimeout.TotalSeconds:0} s");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Loading the taskbar helper failed", ex);
        }
        finally
        {
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        }
    }

    /// <summary>
    /// Writes the embedded helper next to MusicBar's data. The file name carries a
    /// hash of its contents: a copy Explorer still holds open is never overwritten.
    /// </summary>
    static string WriteHelper()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("MusicBar.TaskbarTap.dll")
                             ?? throw new InvalidOperationException("The taskbar helper is missing from this build.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        var bytes = buffer.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();
        string folder = Path.Combine(AppPaths.DataDir, "TaskbarHelper");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"MusicBar.TaskbarTap.{hash}.dll");
        if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
        // Older copies go once no Explorer holds them any more.
        foreach (var old in Directory.EnumerateFiles(folder, "MusicBar.TaskbarTap.*.dll"))
        {
            if (string.Equals(old, path, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                File.Delete(old);
            }
            catch (Exception)
            {
            }
        }
        return path;
    }

    const int WH_CALLWNDPROC = 4;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibrary(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int type, IntPtr proc, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
