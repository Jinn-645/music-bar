using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicBar.Interop;

/// <summary>Display name + icon for a file path or a "shell:AppsFolder\{AUMID}" entry.</summary>
internal static class ShellImages
{
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE
    {
        public int cx, cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitfields0, dsBitfields1, dsBitfields2;
        public IntPtr dshSection;
        public uint dsOffset;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [DllImport("gdi32.dll")]
    static extern int GetObject(IntPtr handle, int size, ref DIBSECTION section);

    const int SIIGBF_BIGGERSIZEOK = 0x1;
    const int SIIGBF_ICONONLY = 0x4;

    public static (string? Name, BitmapSource? Icon) Load(string parsingName, int size)
    {
        IShellItem? item = null;
        try
        {
            var iid = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out item);

            string? name = null;
            try
            {
                item.GetDisplayName(0 /* SIGDN_NORMALDISPLAY */, out var namePtr);
                name = Marshal.PtrToStringUni(namePtr);
                Marshal.FreeCoTaskMem(namePtr);
            }
            catch (COMException)
            {
            }

            BitmapSource? icon = null;
            if (item is IShellItemImageFactory factory
                && factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out var hbm) == 0
                && hbm != IntPtr.Zero)
            {
                try { icon = FromHBitmap(hbm); }
                finally { Native.DeleteObject(hbm); }
            }
            return (name, icon);
        }
        catch (Exception)
        {
            return (null, null);
        }
        finally
        {
            if (item != null) Marshal.ReleaseComObject(item);
        }
    }

    static BitmapSource FromHBitmap(IntPtr hbm)
    {
        var ds = new DIBSECTION();
        int got = GetObject(hbm, Marshal.SizeOf<DIBSECTION>(), ref ds);
        if (got == Marshal.SizeOf<DIBSECTION>() && ds.dsBm.bmBits != IntPtr.Zero && ds.dsBm.bmBitsPixel == 32)
        {
            int w = ds.dsBm.bmWidth, h = Math.Abs(ds.dsBm.bmHeight), stride = ds.dsBm.bmWidthBytes;
            BitmapSource src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, ds.dsBm.bmBits, stride * h, stride);
            if (ds.dsBmih.biHeight > 0) src = new TransformedBitmap(src, new ScaleTransform(1, -1));
            src.Freeze();
            return src;
        }

        var fallback = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        fallback.Freeze();
        return fallback;
    }
}
