using System.Runtime.InteropServices;
using System.Windows;

namespace ShortcutBook;

/// <summary>
/// 탐색기 "새로 만들기 > 바로가기 목록 바로가기".
/// 이름을 물은 뒤 ① 프로그램 폴더의 Lists 에 빈 .sbook 목록 파일을 만들고 ② 우클릭한 자리에 그 파일을 가리키는 .lnk 를 만든다.
/// .lnk 는 탐색기 속성 > 아이콘 변경으로 아이콘을 바꿀 수 있다.
/// </summary>
internal static class NewLinkFlow
{
    /// <param name="pathArg">탐색기가 넘기는 값: 새 항목이 놓일 폴더 또는 파일 경로</param>
    public static void Run(string pathArg, string? nameOverride = null)
    {
        try
        {
            string dest = Directory.Exists(pathArg) ? pathArg : (Path.GetDirectoryName(pathArg) ?? "");
            if (!Directory.Exists(dest)) dest = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            string value;
            if (nameOverride != null) value = nameOverride; // 개발용: 이름 창 없이 만들기
            else
            {
                var dlg = new InputWindow(Loc.T("newlink_title"), Loc.T("newlink_prompt"), Loc.T("newlink_default"));
                if (dlg.ShowDialog() != true) return;
                value = dlg.Value;
            }
            var name = Sanitize(value);
            if (name.Length == 0) name = Loc.T("newlink_default");

            Directory.CreateDirectory(AppInfo.ListsDir);
            var listPath = Unique(AppInfo.ListsDir, name, AppInfo.Extension);
            new ListDocument().Save(listPath);

            var lnkPath = Unique(dest, name, ".lnk");
            CreateLink(lnkPath, listPath);
            Log.Write($"새 바로가기 목록 바로가기: {lnkPath} -> {listPath}");
        }
        catch (Exception ex)
        {
            Log.Write("새 바로가기 목록 바로가기 실패: " + ex);
            MessageBox.Show(Loc.F("err_newlink", ex.Message), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c.ToString(), "");
        return s.Trim().TrimEnd('.');
    }

    static string Unique(string dir, string name, string ext)
    {
        var path = Path.Combine(dir, name + ext);
        for (int i = 2; File.Exists(path) || Directory.Exists(path); i++)
            path = Path.Combine(dir, $"{name} ({i}){ext}");
        return path;
    }

    static void CreateLink(string lnkPath, string target)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell");
        dynamic? shell = null, lnk = null;
        try
        {
            shell = Activator.CreateInstance(type);
            lnk = shell!.CreateShortcut(lnkPath);
            lnk.TargetPath = target;
            lnk.WorkingDirectory = Path.GetDirectoryName(target) ?? "";
            lnk.Description = Path.GetFileNameWithoutExtension(target);
            lnk.Save();
        }
        finally
        {
            if (lnk != null) Marshal.ReleaseComObject(lnk);
            if (shell != null) Marshal.ReleaseComObject(shell);
        }
    }
}
