using System.Runtime.InteropServices;

namespace ShortcutBook;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    public static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint PrivateExtractIcons(string file, int index, int cx, int cy,
        [Out] IntPtr[] phicon, [Out] uint[] piconid, uint nIcons, uint flags);

    [DllImport("shell32.dll")] public static extern void SHChangeNotify(int eventId, uint flags, IntPtr i1, IntPtr i2);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);

    public const int ASFW_ANY = -1;
    public const int SHCNE_ASSOCCHANGED = 0x08000000;
}
