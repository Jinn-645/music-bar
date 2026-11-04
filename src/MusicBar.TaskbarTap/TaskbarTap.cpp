// MusicBar.TaskbarTap: makes the Windows 11 taskbar's background see-through
// while MusicBar asks for it, so MusicBar's progress layer shows under the
// taskbar icons.
//
// The Windows 11 taskbar draws its background in XAML inside Explorer; no window
// attribute reaches it. The approach follows TranslucentTB (written independently
// here):
//   1. MusicBar sets a WH_CALLWNDPROC hook on the taskbar's thread, which makes
//      Windows load this DLL into Explorer.
//   2. Inside Explorer, the DLL connects to Explorer's XAML through the public
//      diagnostics API (InitializeXamlDiagnosticsEx, what Visual Studio's Live
//      Visual Tree uses), which keeps it loaded; MusicBar then drops the hook.
//      Older builds draw the taskbar with system XAML (Windows.UI.Xaml); newer
//      ones (26H2) with WinUI 3, whose diagnostics entry lives in
//      Microsoft.Internal.FrameworkUdk.dll. Both are tried.
//   3. As a XAML diagnostics "TAP", it watches the visual tree for the taskbar's
//      background rectangles (BackgroundFill and BackgroundStroke inside each
//      TaskbarFrame) and swaps their Fill for a transparent brush, keeping the
//      original to put back. Everything goes through the diagnostics service's
//      generic property calls, so the same code serves both XAML frameworks.
//
// MusicBar signals what it wants with a named event: while the event exists and
// is set the background stays clear; once it is reset, or MusicBar exits or
// crashes (the event then disappears), the taskbar gets its own brush back.

#include <windows.h>
#include <ocidl.h>
#include <xamlom.h>
#include <wrl/client.h>

#include <cstdarg>
#include <cstdio>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

using Microsoft::WRL::ComPtr;

// {391FC26B-9159-4921-8A61-FD6A21601EEC}
static const CLSID CLSID_TaskbarTap = { 0x391fc26b, 0x9159, 0x4921, { 0x8a, 0x61, 0xfd, 0x6a, 0x21, 0x60, 0x1e, 0xec } };

static const wchar_t WantEvent[] = L"Local\\MusicBar.TaskbarClear.v1";
// Versioned: a newer helper attaches even while an older one is still loaded in this Explorer.
static const wchar_t ReadyPrefix[] = L"Local\\MusicBar.TaskbarTap.Ready.v2.";
static const wchar_t PumpClass[] = L"MusicBar.TaskbarTap.Pump";
static const UINT WM_APPLY = WM_APP + 1;

static HMODULE g_module;

namespace
{
    // A line in %LOCALAPPDATA%\MusicBar\taskbar-helper.log: the helper runs inside Explorer,
    // where nothing else would show what went wrong.
    void Trace(const wchar_t* format, ...)
    {
        wchar_t path[MAX_PATH];
        if (!ExpandEnvironmentStringsW(L"%LOCALAPPDATA%\\MusicBar\\taskbar-helper.log", path, MAX_PATH)) return;
        wchar_t line[512];
        SYSTEMTIME t;
        GetLocalTime(&t);
        int n = swprintf_s(line, L"%04d-%02d-%02d %02d:%02d:%02d.%03d [%lu] ", t.wYear, t.wMonth, t.wDay,
            t.wHour, t.wMinute, t.wSecond, t.wMilliseconds, GetCurrentProcessId());
        va_list args;
        va_start(args, format);
        n += vswprintf_s(line + n, _countof(line) - n - 2, format, args);
        va_end(args);
        line[n++] = L'\r';
        line[n++] = L'\n';
        char utf8[1024];
        int bytes = WideCharToMultiByte(CP_UTF8, 0, line, n, utf8, sizeof(utf8), nullptr, nullptr);
        HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, 0, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
        DWORD written;
        WriteFile(file, utf8, bytes, &written, nullptr);
        CloseHandle(file);
    }

    std::wstring ReadyEventName() { return ReadyPrefix + std::to_wstring(GetCurrentProcessId()); }

    bool Wanted()
    {
        HANDLE e = OpenEventW(SYNCHRONIZE, FALSE, WantEvent);
        if (!e) return false;
        bool set = WaitForSingleObject(e, 0) == WAIT_OBJECT_0;
        CloseHandle(e);
        return set;
    }

    bool EndsWith(const std::wstring& s, const wchar_t* tail)
    {
        size_t n = wcslen(tail);
        return s.size() >= n && s.compare(s.size() - n, n, tail) == 0;
    }

    // One background rectangle. Only touched on its own XAML thread.
    struct Surface
    {
        InstanceHandle element = 0;
        DWORD thread = 0;
        unsigned int fill = 0;
        std::wstring brushType;
        InstanceHandle original = 0;
        InstanceHandle clear = 0;
        ComPtr<IInspectable> keepOriginal;
        ComPtr<IInspectable> keepClear;
    };
}

class TaskbarTap final : public IObjectWithSite, public IVisualTreeServiceCallback2
{
public:
    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, void** out) override
    {
        if (!out) return E_POINTER;
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IObjectWithSite))
            *out = static_cast<IObjectWithSite*>(this);
        else if (riid == __uuidof(IVisualTreeServiceCallback) || riid == __uuidof(IVisualTreeServiceCallback2))
            *out = static_cast<IVisualTreeServiceCallback2*>(this);
        else
        {
            *out = nullptr;
            return E_NOINTERFACE;
        }
        AddRef();
        return S_OK;
    }

    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_refs); }

    STDMETHODIMP_(ULONG) Release() override
    {
        ULONG refs = InterlockedDecrement(&_refs);
        if (refs == 0) delete this;
        return refs;
    }

    // IObjectWithSite: XAML diagnostics hands over its services here.
    STDMETHODIMP SetSite(IUnknown* site) override
    {
        _site = site;
        if (!site) return S_OK;
        site->QueryInterface(IID_PPV_ARGS(&_diagnostics));
        site->QueryInterface(IID_PPV_ARGS(&_tree));
        Trace(L"SetSite: diagnostics %p, tree service %p", _diagnostics.Get(), _tree.Get());
        if (!_diagnostics || !_tree) return E_NOINTERFACE;

        // Subscribing from inside SetSite hangs; a thread of our own does it (the
        // callbacks still arrive on the XAML thread) and then follows MusicBar's request.
        AddRef();
        HANDLE thread = CreateThread(nullptr, 0, [](void* self) -> DWORD
        {
            static_cast<TaskbarTap*>(self)->Run();
            return 0;
        }, this, 0, nullptr);
        if (thread) CloseHandle(thread);
        else Release();
        return S_OK;
    }

    STDMETHODIMP GetSite(REFIID riid, void** out) override
    {
        if (!_site) return E_FAIL;
        return _site->QueryInterface(riid, out);
    }

    // IVisualTreeServiceCallback: every existing element first, then every change,
    // on the element's XAML thread. The strings in the element belong to us.
    STDMETHODIMP OnVisualTreeChange(ParentChildRelation relation, VisualElement element, VisualMutationType mutation) override
    {
        std::wstring type = element.Type ? element.Type : L"";
        std::wstring name = element.Name ? element.Name : L"";
        SysFreeString(element.Type);
        SysFreeString(element.Name);
        SysFreeString(element.SrcInfo.FileName);
        SysFreeString(element.SrcInfo.Hash);

        bool applied;
        {
            std::lock_guard<std::mutex> guard(_lock);
            if (mutation == Remove)
            {
                _nodes.erase(element.Handle);
                _surfaces.erase(element.Handle);
                return S_OK;
            }
            _nodes[element.Handle] = Node { relation.Parent, type, name };
            if (!EndsWith(type, L".Xaml.Shapes.Rectangle") || (name != L"BackgroundFill" && name != L"BackgroundStroke")) return S_OK;
            if (!InsideTaskbarFrame(relation.Parent)) return S_OK;
            applied = _applied;
        }

        auto surface = std::make_shared<Surface>();
        surface->element = element.Handle;
        surface->thread = GetCurrentThreadId();
        // Same namespace as the rectangle: Windows.UI.Xaml or Microsoft.UI.Xaml.
        surface->brushType = type.substr(0, type.size() - wcslen(L"Shapes.Rectangle")) + L"Media.SolidColorBrush";
        HRESULT hr = FindProperty(element.Handle, L"Fill", surface->fill);
        if (FAILED(hr))
        {
            Trace(L"Found taskbar %s but no Fill property: 0x%08X", name.c_str(), hr);
            return S_OK;
        }
        EnsurePump();
        {
            std::lock_guard<std::mutex> guard(_lock);
            _surfaces[element.Handle] = surface;
        }
        Trace(L"Found taskbar %s (%s)", name.c_str(), type.c_str());
        if (applied) Apply(*surface, true);
        return S_OK;
    }

    STDMETHODIMP OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) override { return S_OK; }

private:
    struct Node
    {
        InstanceHandle parent;
        std::wstring type;
        std::wstring name;
    };

    ~TaskbarTap()
    {
        if (_ready) CloseHandle(_ready);
    }

    // The diagnostics index of a property, looked up in the element's property list
    // (GetPropertyIndex rejects plain names on some builds).
    HRESULT FindProperty(InstanceHandle element, const wchar_t* property, unsigned int& index)
    {
        HRESULT hr = _tree->GetPropertyIndex(element, property, &index);
        if (SUCCEEDED(hr)) return hr;

        unsigned int sourceCount = 0, valueCount = 0;
        PropertyChainSource* sources = nullptr;
        PropertyChainValue* values = nullptr;
        hr = _tree->GetPropertyValuesChain(element, &sourceCount, &sources, &valueCount, &values);
        if (FAILED(hr)) return hr;
        bool found = false;
        for (unsigned int i = 0; i < valueCount; i++)
        {
            if (!found && values[i].PropertyName && wcscmp(values[i].PropertyName, property) == 0)
            {
                index = values[i].Index;
                found = true;
            }
            SysFreeString(values[i].Type);
            SysFreeString(values[i].DeclaringType);
            SysFreeString(values[i].ValueType);
            SysFreeString(values[i].ItemType);
            SysFreeString(values[i].Value);
            SysFreeString(values[i].PropertyName);
        }
        for (unsigned int i = 0; i < sourceCount; i++)
        {
            SysFreeString(sources[i].TargetType);
            SysFreeString(sources[i].Name);
            SysFreeString(sources[i].SrcInfo.FileName);
            SysFreeString(sources[i].SrcInfo.Hash);
        }
        CoTaskMemFree(values);
        CoTaskMemFree(sources);
        return found ? S_OK : HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    }

    bool InsideTaskbarFrame(InstanceHandle handle) const
    {
        for (int depth = 0; depth < 16 && handle; depth++)
        {
            auto node = _nodes.find(handle);
            if (node == _nodes.end()) return false;
            if (EndsWith(node->second.type, L"Taskbar.TaskbarFrame") || node->second.name == L"TaskbarFrame") return true;
            handle = node->second.parent;
        }
        return false;
    }

    // XAML thread: swaps the rectangle's brush (or puts the taskbar's own back).
    void Apply(Surface& s, bool clear)
    {
        InstanceHandle current = 0;
        if (FAILED(_tree->GetProperty(s.element, s.fill, &current))) return;
        if (!clear)
        {
            if (s.original && current == s.clear) _tree->SetProperty(s.element, s.original, s.fill);
            return;
        }
        if (s.clear && current == s.clear) return;
        // Not drawn yet: wait for the taskbar to give it a brush first.
        if (!current) return;
        // Whatever the taskbar put there (first time, or after a theme change) is what comes back later.
        s.original = current;
        s.keepOriginal.Reset();
        _diagnostics->GetIInspectableFromHandle(current, &s.keepOriginal);
        if (!s.clear)
        {
            BSTR type = SysAllocString(s.brushType.c_str());
            BSTR value = SysAllocString(L"#00000000");
            HRESULT hr = _tree->CreateInstance(type, value, &s.clear);
            SysFreeString(type);
            SysFreeString(value);
            if (FAILED(hr))
            {
                Trace(L"Creating %s failed: 0x%08X", s.brushType.c_str(), hr);
                s.clear = 0;
                return;
            }
            _diagnostics->GetIInspectableFromHandle(s.clear, &s.keepClear);
        }
        HRESULT hr = _tree->SetProperty(s.element, s.clear, s.fill);
        if (FAILED(hr)) Trace(L"Setting Fill failed: 0x%08X", hr);
    }

    // XAML thread: applies MusicBar's request to the surfaces living on this thread.
    void ApplyHere(bool clear)
    {
        DWORD me = GetCurrentThreadId();
        std::vector<std::shared_ptr<Surface>> mine;
        {
            std::lock_guard<std::mutex> guard(_lock);
            for (auto& entry : _surfaces)
                if (entry.second->thread == me) mine.push_back(entry.second);
        }
        for (auto& s : mine) Apply(*s, clear);
    }

    // A message-only window on each XAML thread, so the watcher can hand work to that thread.
    void EnsurePump()
    {
        DWORD me = GetCurrentThreadId();
        {
            std::lock_guard<std::mutex> guard(_lock);
            if (_pumps.count(me)) return;
        }
        static ATOM atom = []
        {
            WNDCLASSW wc = {};
            wc.lpfnWndProc = [](HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) -> LRESULT
            {
                if (msg == WM_APPLY)
                {
                    auto self = reinterpret_cast<TaskbarTap*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
                    if (self) self->ApplyHere(wParam != 0);
                    return 0;
                }
                return DefWindowProcW(hwnd, msg, wParam, lParam);
            };
            wc.hInstance = g_module;
            wc.lpszClassName = PumpClass;
            return RegisterClassW(&wc);
        }();
        if (!atom) return;
        HWND pump = CreateWindowExW(0, PumpClass, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, g_module, nullptr);
        if (!pump) return;
        SetWindowLongPtrW(pump, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(this));
        std::lock_guard<std::mutex> guard(_lock);
        _pumps[me] = pump;
    }

    void Run()
    {
        HRESULT hr = _tree->AdviseVisualTreeChange(this);
        Trace(L"AdviseVisualTreeChange: 0x%08X", hr);
        // Tells MusicBar the helper is in place (and that this Explorer needs no second one).
        _ready = CreateEventW(nullptr, TRUE, TRUE, ReadyEventName().c_str());

        for (int tick = 0;; tick++)
        {
            bool want = Wanted();
            std::vector<HWND> pumps;
            bool changed;
            size_t count;
            {
                std::lock_guard<std::mutex> guard(_lock);
                changed = want != _applied;
                _applied = want;
                count = _surfaces.size();
                // While clear, look again every second: the taskbar replaces its brush on theme changes.
                if (changed || (want && tick % 4 == 0))
                    for (auto& entry : _pumps) pumps.push_back(entry.second);
            }
            for (HWND pump : pumps) PostMessageW(pump, WM_APPLY, want ? 1 : 0, 0);
            if (changed) Trace(L"Background %s (%zu surfaces)", want ? L"cleared" : L"restored", count);
            Sleep(250);
        }
    }

    LONG _refs = 1;
    ComPtr<IUnknown> _site;
    ComPtr<IXamlDiagnostics> _diagnostics;
    ComPtr<IVisualTreeService3> _tree;
    HANDLE _ready = nullptr;

    std::mutex _lock;
    std::map<InstanceHandle, Node> _nodes;
    std::map<InstanceHandle, std::shared_ptr<Surface>> _surfaces;
    std::map<DWORD, HWND> _pumps;
    bool _applied = false;
};

class Factory final : public IClassFactory
{
public:
    STDMETHODIMP QueryInterface(REFIID riid, void** out) override
    {
        if (!out) return E_POINTER;
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IClassFactory))
        {
            *out = static_cast<IClassFactory*>(this);
            return S_OK;
        }
        *out = nullptr;
        return E_NOINTERFACE;
    }

    STDMETHODIMP_(ULONG) AddRef() override { return 2; }
    STDMETHODIMP_(ULONG) Release() override { return 1; }

    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** out) override
    {
        if (outer) return CLASS_E_NOAGGREGATION;
        auto tap = new (std::nothrow) TaskbarTap();
        if (!tap) return E_OUTOFMEMORY;
        HRESULT hr = tap->QueryInterface(riid, out);
        tap->Release();
        return hr;
    }

    STDMETHODIMP LockServer(BOOL) override { return S_OK; }
};

static Factory g_factory;

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** out)
{
    if (clsid != CLSID_TaskbarTap) return CLASS_E_CLASSNOTAVAILABLE;
    return g_factory.QueryInterface(riid, out);
}

// Explorer keeps the TAP until it exits: the watcher thread lives in this DLL.
STDAPI DllCanUnloadNow() { return S_FALSE; }

// The hook MusicBar sets on the taskbar's thread; it only exists to get this DLL loaded there.
extern "C" LRESULT CALLBACK TapHookProc(int code, WPARAM wParam, LPARAM lParam)
{
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

using InitFn = HRESULT(WINAPI*)(LPCWSTR, DWORD, LPCWSTR, LPCWSTR, CLSID, LPCWSTR);

// XAML names each of its instances in a process "VisualDiagConnection<n>", and the
// connection has to name an existing one; "not found" means try the next number
// (Windhawk's taskbar mods do the same). A successful connection pins this DLL.
static HRESULT Connect(InitFn init, const wchar_t* path, int& number)
{
    HRESULT hr = HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    for (number = 1; number <= 10000; number++)
    {
        wchar_t connection[64];
        swprintf_s(connection, L"VisualDiagConnection%d", number);
        hr = init(connection, GetCurrentProcessId(), L"", path, CLSID_TaskbarTap, nullptr);
        if (hr != HRESULT_FROM_WIN32(ERROR_NOT_FOUND)) break;
    }
    return hr;
}

// Runs in Explorer. The taskbar is system XAML (Windows.UI.Xaml) up to at least 26H2;
// WinUI 3, whose entry point lives in Microsoft.Internal.FrameworkUdk.dll, is the
// fallback should it move. XAML diagnostics initialises once per thread, so each
// try runs on a fresh thread; while the taskbar is still starting nothing is found
// yet, so it keeps trying for a while.
static DWORD WINAPI Install(void*)
{
    wchar_t path[MAX_PATH];
    if (!GetModuleFileNameW(g_module, path, MAX_PATH)) return 1;
    Trace(L"Loaded into Explorer: %s", path);

    struct Framework
    {
        const wchar_t* name;
        InitFn init;
    };
    std::vector<Framework> frameworks;
    if (HMODULE wux = LoadLibraryExW(L"Windows.UI.Xaml.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32))
        if (auto init = reinterpret_cast<InitFn>(GetProcAddress(wux, "InitializeXamlDiagnosticsEx")))
            frameworks.push_back({ L"system XAML", init });
    if (HMODULE udk = GetModuleHandleW(L"Microsoft.Internal.FrameworkUdk.dll"))
        if (auto init = reinterpret_cast<InitFn>(GetProcAddress(udk, "InitializeXamlDiagnosticsEx")))
            frameworks.push_back({ L"WinUI 3", init });

    for (int round = 1; round <= 30; round++)
    {
        for (auto& f : frameworks)
        {
            HRESULT hr = E_FAIL;
            int number = 0;
            std::thread([&] { hr = Connect(f.init, path, number); }).join();
            if (SUCCEEDED(hr))
            {
                Trace(L"Connected to %s as VisualDiagConnection%d", f.name, number);
                return 0;
            }
            if (round == 1 || round == 30) Trace(L"%s: 0x%08X (round %d)", f.name, hr, round);
        }
        Sleep(1000);
    }
    return 1;
}

static bool InExplorer()
{
    wchar_t image[MAX_PATH];
    DWORD n = GetModuleFileNameW(nullptr, image, MAX_PATH);
    const wchar_t* file = wcsrchr(image, L'\\');
    return n && file && _wcsicmp(file + 1, L"explorer.exe") == 0;
}

BOOL WINAPI DllMain(HMODULE module, DWORD reason, void*)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    g_module = module;
    DisableThreadLibraryCalls(module);
    // MusicBar itself also loads the DLL (to set the hook); there it does nothing.
    if (!InExplorer()) return TRUE;

    // Already connected in this Explorer (an earlier hook): nothing more to do.
    HANDLE ready = OpenEventW(SYNCHRONIZE, FALSE, ReadyEventName().c_str());
    if (ready)
    {
        CloseHandle(ready);
        return TRUE;
    }
    static LONG installing = 0;
    if (InterlockedExchange(&installing, 1) != 0) return TRUE;

    // Pinned: once connected, the watcher thread must never run on unloaded code.
    HMODULE pinned;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&DllMain), &pinned);
    HANDLE thread = CreateThread(nullptr, 0, Install, nullptr, 0, nullptr);
    if (thread) CloseHandle(thread);
    return TRUE;
}
