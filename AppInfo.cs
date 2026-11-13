using System.Reflection;

namespace ShortcutBook;

/// <summary>화면에 보여 줄 프로그램 이름과 버전. 버전은 빌드할 때 .build_number 에서 들어온다 (ShortcutBook.csproj).</summary>
public static class AppInfo
{
    public const string Name = "ShortcutBook";
    public const string Extension = ".sbook";
    public const string ProgId = "ShortcutBook.List";
    public const string AuthorMail = "redwoodmane@gmail.com";

    /// <summary>예: "v1.0.1 (빌드 #1)"</summary>
    public static string VersionText { get; } = Format(Assembly.GetExecutingAssembly().GetName().Version);

    public static string Format(Version? v)
    {
        if (v == null) return "";
        if (v.Build < 0) return $"v{v.Major}.{v.Minor}";
        return $"v{v.Major}.{v.Minor}.{v.Build} ({Loc.T("build_word")} #{v.Build})";
    }

    public static string DataDir
    {
        get
        {
            var over = Environment.GetEnvironmentVariable("SHORTCUTBOOK_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(over)) return over;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);
        }
    }
}

/// <summary>처리되지 않은 오류와 사용자가 나중에 확인할 결과를 남기는 로그 (%AppData%\ShortcutBook\log.txt).</summary>
public static class Log
{
    static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppInfo.DataDir);
                var path = Path.Combine(AppInfo.DataDir, "log.txt");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                    File.Delete(path);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch { /* 로그 실패는 무시 */ }
    }
}
