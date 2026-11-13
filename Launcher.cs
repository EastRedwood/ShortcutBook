using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ShortcutBook;

/// <summary>바로가기 실행과 매뉴얼·웹 주소 열기</summary>
internal static partial class Launcher
{
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]+://")]
    private static partial Regex SchemeRegex();

    public static string Expand(string? s) => Environment.ExpandEnvironmentVariables((s ?? "").Trim().Trim('"').Trim());

    public static bool IsUrl(string s)
    {
        s = s.Trim();
        return SchemeRegex().IsMatch(s) || s.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeUrl(string s)
    {
        s = s.Trim();
        return s.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + s : s;
    }

    /// <summary>설치된 앱(스토어 앱 포함)을 가리키는 대상. 탐색기의 shell:AppsFolder 주소 형식이다.</summary>
    public const string AppsFolderPrefix = @"shell:AppsFolder\";

    public static bool IsShellApp(string s) =>
        s.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase) && s.Length > AppsFolderPrefix.Length;

    public static bool IsList(string path) =>
        path.EndsWith(AppInfo.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>실행에 실패하면 사용자에게 보여 줄 문장을 돌려준다. 성공하거나 사용자가 취소했으면 null.</summary>
    public static string? Run(ListItem it, MainWindow? from = null)
    {
        var target = Expand(it.Target);
        if (target.Length == 0) return Loc.T("err_no_target");
        try
        {
            if (IsUrl(target)) { ShellOpen(NormalizeUrl(target)); return null; }

            if (IsShellApp(target)) { ShellOpen(target); return null; }

            if (IsList(target) && File.Exists(target))
            {
                App.Instance.OpenListFrom(from, target);
                return null;
            }

            bool isDir = Directory.Exists(target);
            if (!isDir && !File.Exists(target)) return Loc.F("err_target_missing", target);

            var psi = new ProcessStartInfo { UseShellExecute = true };
            psi.FileName = target;
            if (!isDir) psi.Arguments = it.Arguments ?? "";

            var work = Expand(it.WorkDir);
            if (work.Length > 0 && Directory.Exists(work)) psi.WorkingDirectory = work;
            else if (!isDir) psi.WorkingDirectory = Path.GetDirectoryName(target) ?? "";

            psi.WindowStyle = it.WindowStyle switch
            {
                1 => ProcessWindowStyle.Minimized,
                2 => ProcessWindowStyle.Maximized,
                _ => ProcessWindowStyle.Normal,
            };
            if (it.RunAsAdmin && !isDir) psi.Verb = "runas";

            Process.Start(psi)?.Dispose();
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return null; // 관리자 권한 요청을 사용자가 취소함
        }
        catch (Exception ex)
        {
            Log.Write("실행 실패 " + target + ": " + ex.Message);
            return Loc.F("err_run_failed", ex.Message);
        }
    }

    public static string? OpenManual(ListItem it)
    {
        var p = Expand(it.ManualPath);
        if (p.Length == 0) return null;
        try
        {
            if (IsUrl(p)) { ShellOpen(NormalizeUrl(p)); return null; }
            if (!File.Exists(p) && !Directory.Exists(p)) return Loc.F("err_target_missing", p);
            ShellOpen(p);
            return null;
        }
        catch (Exception ex) { return Loc.F("err_run_failed", ex.Message); }
    }

    public static string? OpenWeb(ListItem it)
    {
        var u = (it.WebUrl ?? "").Trim();
        if (u.Length == 0) return null;
        try { ShellOpen(NormalizeUrl(u)); return null; }
        catch (Exception ex) { return Loc.F("err_run_failed", ex.Message); }
    }

    public static void OpenLocation(ListItem it)
    {
        var t = Expand(it.Target);
        try
        {
            if (File.Exists(t) || Directory.Exists(t))
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + t + "\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { Log.Write("위치 열기 실패: " + ex.Message); }
    }

    public static void ShellOpen(string what)
    {
        Process.Start(new ProcessStartInfo(what) { UseShellExecute = true })?.Dispose();
    }
}
