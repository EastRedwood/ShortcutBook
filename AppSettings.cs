using System.Text;
using System.Text.Json;

namespace ShortcutBook;

/// <summary>프로그램 전체 설정 (%AppData%\ShortcutBook\settings.json). 목록 내용은 각 .sbook 파일에 따로 저장된다.</summary>
public sealed class AppSettings
{
    /// <summary>0 작게 · 1 중간 · 2 크게 · 3 아주 크게</summary>
    public int ViewSize { get; set; } = 1;
    public string? Language { get; set; }
    public bool RegisterShell { get; set; } = true;
    public List<string> Recent { get; set; } = new();
    public double WindowWidth { get; set; } = 760;
    public double WindowHeight { get; set; } = 560;

    static string FilePath => Path.Combine(AppInfo.DataDir, "settings.json");
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath, Encoding.UTF8), Opt) ?? new();
        }
        catch (Exception ex) { Log.Write("설정 읽기 실패: " + ex.Message); }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opt), new UTF8Encoding(false));
        }
        catch (Exception ex) { Log.Write("설정 저장 실패: " + ex.Message); }
    }

    public void AddRecent(string path)
    {
        Recent.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, path);
        if (Recent.Count > 20) Recent.RemoveRange(20, Recent.Count - 20);
    }
}
