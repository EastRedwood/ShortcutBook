using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShortcutBook;

/// <summary>끌어다 놓은 파일(.lnk·.url·실행 파일·폴더·다른 목록)에서 추가 팝업의 초기값을 만든다.</summary>
internal static class ShortcutImport
{
    public static ListItem FromPath(string path)
    {
        var it = new ListItem();
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".lnk") FillFromLnk(it, path);
            else if (ext == ".url") FillFromUrlFile(it, path);
            else if (Directory.Exists(path))
            {
                it.Target = path;
                it.Name = new DirectoryInfo(path).Name;
            }
            else
            {
                it.Target = path;
                it.Name = NameOf(path);
            }
        }
        catch (Exception ex)
        {
            Log.Write("바로가기 읽기 실패 " + path + ": " + ex.Message);
            it.Target = path;
            if (it.Name.Length == 0) it.Name = Path.GetFileNameWithoutExtension(path);
        }
        return it;
    }

    public static ListItem FromUrl(string url)
    {
        var it = new ListItem { Target = url.Trim() };
        try { it.Name = new Uri(Launcher.NormalizeUrl(url)).Host; } catch { it.Name = url; }
        return it;
    }

    static string NameOf(string path)
    {
        var plain = Path.GetFileNameWithoutExtension(path);
        try
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var d = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(d) && d.Length <= 60) return d.Trim();
            }
        }
        catch { }
        return plain;
    }

    static void FillFromLnk(ListItem it, string lnkPath)
    {
        it.Name = Path.GetFileNameWithoutExtension(lnkPath);

        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type == null) { it.Target = lnkPath; return; }
        dynamic? shell = null, lnk = null;
        try
        {
            shell = Activator.CreateInstance(type);
            lnk = shell!.CreateShortcut(lnkPath);
            string target = lnk.TargetPath ?? "";
            string args = lnk.Arguments ?? "";
            string work = lnk.WorkingDirectory ?? "";
            string desc = lnk.Description ?? "";
            string icon = lnk.IconLocation ?? "";
            int style = lnk.WindowStyle;

            if (target.Length > 0 && (File.Exists(target) || Directory.Exists(target)))
            {
                it.Target = target;
                it.Arguments = args;
                it.WorkDir = work;
                it.WindowStyle = style == 7 ? 1 : style == 3 ? 2 : 0;
            }
            else
            {
                // 스토어 앱·특수 바로가기처럼 대상을 풀 수 없으면 바로가기 파일 자체를 연다.
                it.Target = lnkPath;
            }

            if (!string.IsNullOrWhiteSpace(desc) && !desc.Equals(it.Name, StringComparison.OrdinalIgnoreCase))
                it.Description = desc.Trim();

            icon = icon.Trim();
            if (icon.Length > 0 && !icon.StartsWith(',') && !SamePathAsTarget(icon, it.Target))
                it.IconPath = icon;
        }
        finally
        {
            if (lnk != null) Marshal.ReleaseComObject(lnk);
            if (shell != null) Marshal.ReleaseComObject(shell);
        }
    }

    static bool SamePathAsTarget(string icon, string target)
    {
        int i = icon.LastIndexOf(',');
        var p = i > 1 ? icon[..i] : icon;
        return p.Equals(target, StringComparison.OrdinalIgnoreCase);
    }

    static void FillFromUrlFile(ListItem it, string path)
    {
        it.Name = Path.GetFileNameWithoutExtension(path);
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) it.Target = line[4..].Trim();
            else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
            {
                var f = line[9..].Trim();
                if (f.Length > 0 && File.Exists(f)) it.IconPath = f;
            }
        }
        if (it.Target.Length == 0) it.Target = path;
    }
}
