using Microsoft.Win32;

namespace ShortcutBook;

/// <summary>
/// 탐색기 우클릭 "새로 만들기" 메뉴에 "바로가기 목록"을 넣고, .sbook 더블클릭으로 이 프로그램이 열리게 한다.
/// 관리자 권한이 필요 없는 현재 사용자 영역(HKCU\Software\Classes)에만 쓴다.
/// </summary>
internal static class ShellRegistration
{
    const string ClassesRoot = @"Software\Classes\";

    static string Command(string exe) => "\"" + exe + "\" \"%1\"";

    public static bool IsCurrent(string exe)
    {
        try
        {
            using var cmd = Registry.CurrentUser.OpenSubKey(ClassesRoot + AppInfo.ProgId + @"\shell\open\command");
            using var ext = Registry.CurrentUser.OpenSubKey(ClassesRoot + AppInfo.Extension);
            using var shellNew = Registry.CurrentUser.OpenSubKey(ClassesRoot + AppInfo.Extension + @"\ShellNew");
            using var prog = Registry.CurrentUser.OpenSubKey(ClassesRoot + AppInfo.ProgId);
            return (cmd?.GetValue("") as string) == Command(exe)
                && (ext?.GetValue("") as string) == AppInfo.ProgId
                && shellNew?.GetValue("NullFile") != null
                && (shellNew.GetValue("ItemName") as string) == Loc.T("shell_new_name")
                && (prog?.GetValue("") as string) == Loc.T("shell_type_name");
        }
        catch { return false; }
    }

    public static bool IsRegistered()
    {
        try
        {
            using var shellNew = Registry.CurrentUser.OpenSubKey(ClassesRoot + AppInfo.Extension + @"\ShellNew");
            return shellNew != null;
        }
        catch { return false; }
    }

    public static void Register(string exe)
    {
        using (var ext = Registry.CurrentUser.CreateSubKey(ClassesRoot + AppInfo.Extension))
        {
            ext.SetValue("", AppInfo.ProgId);
            ext.SetValue("Content Type", "application/json");
            using var sn = ext.CreateSubKey("ShellNew");
            sn.SetValue("NullFile", "");          // 빈 파일을 만들고 곧바로 이름 바꾸기 상태가 된다 (새 폴더처럼)
            sn.SetValue("ItemName", Loc.T("shell_new_name"));
        }
        using (var prog = Registry.CurrentUser.CreateSubKey(ClassesRoot + AppInfo.ProgId))
        {
            prog.SetValue("", Loc.T("shell_type_name"));
            using var icon = prog.CreateSubKey("DefaultIcon");
            icon.SetValue("", "\"" + exe + "\",0");
            using var cmd = prog.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", Command(exe));
        }
        Notify();
    }

    public static void Unregister()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(ClassesRoot + AppInfo.Extension, false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(ClassesRoot + AppInfo.ProgId, false); } catch { }
        Notify();
    }

    static void Notify() => Native.SHChangeNotify(Native.SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
}
