using System.Text;
using System.Text.Json;

namespace ShortcutBook;

/// <summary>프로그램 전체 설정 (%AppData%\ShortcutBook\settings.json). 목록 내용은 각 .sbook 파일에 따로 저장된다.</summary>
public sealed class AppSettings
{
    /// <summary>0 작게 · 1 중간 · 2 크게 · 3 아주 크게</summary>
    public int ViewSize { get; set; } = 1;
    /// <summary>이름 칸 너비(px). 설명은 이 칸 바로 오른쪽부터 왼쪽 정렬로 보인다.</summary>
    public double NameWidth { get; set; } = 300;
    /// <summary>같은 대상(경로·주소)을 등록할 때: 0 추가 등록 · 1 등록 제외 · 2 새로 덮어쓰기</summary>
    public int DuplicateMode { get; set; }
    public string? Language { get; set; }
    public bool RegisterShell { get; set; } = true;
    /// <summary>0 시스템 · 1 라이트 · 2 다크 · 3 회색</summary>
    public int Theme { get; set; }
    /// <summary>목록 안에서 다른 목록을 열 때 새 창으로 열지 (기본: 현재 창을 대체)</summary>
    public bool OpenInNewWindow { get; set; }
    /// <summary>선택한 항목의 수정 창을 여는 단축키</summary>
    public string EditHotkey { get; set; } = "F2";
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
