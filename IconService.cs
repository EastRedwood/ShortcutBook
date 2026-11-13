using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShortcutBook;

/// <summary>바로가기 대상(또는 직접 지정한 파일)의 아이콘을 읽어 온다. 화면 배율에 맞춰 실제 픽셀 크기로 읽고 캐시한다.</summary>
internal static class IconService
{
    public static double Scale = 1.0;
    // 시작할 때 백그라운드 스레드가 미리 읽어 두므로 스레드에 안전한 사전을 쓴다 (읽은 그림은 Freeze 되어 있다)
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource?> Cache = new();

    public static void Clear() => Cache.Clear();

    [DllImport("user32.dll")] static extern uint GetDpiForSystem();

    /// <summary>창이 생기기 전(미리 읽기)에는 창의 배율을 모르므로 시스템 배율로 읽는다. 창이 생긴 뒤 배율이 다르면 MainWindow 가 다시 읽는다.</summary>
    public static void UseSystemScale()
    {
        try { Scale = GetDpiForSystem() / 96.0; } catch { }
    }

    public static ImageSource? Get(ListItem it, int logicalSize)
    {
        var src = ResolveSource(it);
        if (src.Length == 0) return null;
        int px = (int)Math.Ceiling(logicalSize * Scale);
        var key = src + "|" + px;
        if (Cache.TryGetValue(key, out var hit)) return hit;
        var img = Load(src, px);
        Cache[key] = img;
        return img;
    }

    public static string ResolveSource(ListItem it)
    {
        if (!string.IsNullOrWhiteSpace(it.IconPath)) return Launcher.Expand(it.IconPath);
        var t = Launcher.Expand(it.Target);
        if (t.Length == 0 || Launcher.IsUrl(t)) return "";
        return t;
    }

    static (string path, int index, bool hasIndex) Split(string src)
    {
        int i = src.LastIndexOf(',');
        if (i > 1 && int.TryParse(src[(i + 1)..].Trim(), out int idx))
            return (src[..i].Trim().Trim('"'), idx, true);
        return (src, 0, false);
    }

    static ImageSource? Load(string src, int px)
    {
        try
        {
            var (path, idx, hasIndex) = Split(src);
            var ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif")
            {
                if (!File.Exists(path)) return null;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(path);
                bi.DecodePixelWidth = px;
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }

            if (ext is ".ico" or ".exe" or ".dll" or ".icl" || hasIndex)
            {
                var fromFile = ExtractIcon(path, idx, px);
                if (fromFile != null) return fromFile;
            }

            return ShellImage(path, px);
        }
        catch (Exception ex)
        {
            Log.Write("아이콘 읽기 실패 " + src + ": " + ex.Message);
            return null;
        }
    }

    static ImageSource? ExtractIcon(string path, int index, int px)
    {
        if (!File.Exists(path)) return null;
        var handles = new IntPtr[1];
        var ids = new uint[1];
        uint n = Native.PrivateExtractIcons(path, index, px, px, handles, ids, 1, 0);
        if (n == 0 || n == 0xFFFFFFFF || handles[0] == IntPtr.Zero) return null;
        try
        {
            var bs = Imaging.CreateBitmapSourceFromHIcon(handles[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bs.Freeze();
            return bs;
        }
        finally { Native.DestroyIcon(handles[0]); }
    }

    /// <summary>탐색기가 보여 주는 것과 같은 아이콘 (폴더·문서·바로가기 포함)</summary>
    static ImageSource? ShellImage(string path, int px)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return null;
        path = Path.GetFullPath(path); // 셸은 역슬래시 경로만 받는다 (C:/x 형태는 실패)
        var iid = Native.IID_IShellItemImageFactory;
        Native.IShellItemImageFactory? factory = null;
        try
        {
            Native.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            int hr = factory.GetImage(new Native.SIZE { cx = px, cy = px }, 0x4 /* 아이콘만 (미리보기 그림 제외) */, out var hbm);
            if (hr != 0 || hbm == IntPtr.Zero) return null;
            try
            {
                var bs = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                return bs;
            }
            finally { Native.DeleteObject(hbm); }
        }
        finally
        {
            if (factory != null) Marshal.ReleaseComObject(factory);
        }
    }
}
