using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Markup;

namespace ShortcutBook;

/// <summary>
/// 언어 파일(lang\*.json) 로더. 파일은 {"_name": "표시 이름", "키": "문장"} 형식의 평평한 JSON 이다.
/// 기준 언어(ko)를 먼저 읽고 선택한 언어로 덮어쓰므로, 번역에 빠진 키는 한국어로 보인다.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public static event Action? Changed;

    const string BaseCode = "ko";
    Dictionary<string, string> _map = new();

    public string Code { get; private set; } = BaseCode;

    public string this[string key] => _map.TryGetValue(key, out var v) ? v : key;

    public static string T(string key) => Instance[key];
    public static string F(string key, params object[] args)
    {
        try { return string.Format(Instance[key], args); }
        catch (FormatException) { return Instance[key]; }
    }

    public static string LangDir => Path.Combine(AppContext.BaseDirectory, "lang");

    /// <summary>사용할 수 있는 언어 (코드, 표시 이름). lang 폴더에 파일을 넣으면 늘어난다.</summary>
    public static List<(string Code, string Name)> Available()
    {
        var list = new List<(string, string)>();
        try
        {
            foreach (var f in Directory.GetFiles(LangDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var code = Path.GetFileNameWithoutExtension(f);
                var dict = Read(f);
                list.Add((code, dict.TryGetValue("_name", out var n) ? n : code));
            }
        }
        catch { }
        if (list.Count == 0) list.Add((BaseCode, "한국어"));
        return list;
    }

    public static string DefaultCode()
    {
        var culture = CultureInfo.CurrentUICulture;
        var two = culture.TwoLetterISOLanguageName;
        string[] candidates;
        if (two == "zh")
        {
            // 중국어는 간체/번체 파일이 따로 있다: 대만·홍콩·마카오 또는 Hant 표기면 번체, 그 밖에는 간체
            var name = culture.Name;
            var traditional = name.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase);
            candidates = new[] { traditional ? "zh-Hant" : "zh-Hans", "zh" };
        }
        else candidates = new[] { culture.Name, two };

        // 언어 파일이 있는지만 본다 (모든 파일을 열어 표시 이름까지 읽을 필요가 없다)
        foreach (var c in candidates)
            if (File.Exists(Path.Combine(LangDir, c + ".json"))) return c;
        return "en";
    }

    public void Load(string? code)
    {
        code = string.IsNullOrWhiteSpace(code) ? DefaultCode() : code;
        var map = Read(Path.Combine(LangDir, BaseCode + ".json"));
        if (!code.Equals(BaseCode, StringComparison.OrdinalIgnoreCase))
            foreach (var kv in Read(Path.Combine(LangDir, code + ".json"))) map[kv.Key] = kv.Value;
        _map = map;
        Code = code;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        Changed?.Invoke();
    }

    static Dictionary<string, string> Read(string path)
    {
        var map = new Dictionary<string, string>();
        try
        {
            if (!File.Exists(path)) return map;
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String) map[p.Name] = p.Value.GetString() ?? "";
        }
        catch (Exception ex) { Log.Write("언어 파일 읽기 실패 " + path + ": " + ex.Message); }
        return map;
    }
}

/// <summary>XAML 에서 {l:T 키} 로 쓴다. 언어를 바꾸면 화면 글자도 바로 바뀐다.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public TExtension() { }
    public TExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
