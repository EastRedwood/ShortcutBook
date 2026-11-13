using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace ShortcutBook;

public enum SortMode { Custom, NameAsc, NameDesc }

/// <summary>목록의 한 줄. 윈도우 바로가기처럼 대상·인수·시작 위치·실행 방식을 갖고, 설명과 추가 옵션(매뉴얼·웹 주소·메모)이 붙는다.</summary>
public sealed class ListItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>실행 파일 · 문서 · 폴더 · 웹 주소 · 다른 목록(.sbook) 경로</summary>
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkDir { get; set; } = "";
    /// <summary>아이콘을 직접 지정할 때 (경로 또는 "경로,번호"). 비우면 대상의 아이콘.</summary>
    public string IconPath { get; set; } = "";
    /// <summary>0 보통 · 1 최소화 · 2 최대화</summary>
    public int WindowStyle { get; set; }
    public bool RunAsAdmin { get; set; }
    public string ManualPath { get; set; } = "";
    public string WebUrl { get; set; } = "";
    public string Memo { get; set; } = "";

    public ListItem Clone() => (ListItem)MemberwiseClone();

    public void CopyFrom(ListItem o)
    {
        Name = o.Name; Description = o.Description; Target = o.Target; Arguments = o.Arguments;
        WorkDir = o.WorkDir; IconPath = o.IconPath; WindowStyle = o.WindowStyle;
        RunAsAdmin = o.RunAsAdmin; ManualPath = o.ManualPath; WebUrl = o.WebUrl; Memo = o.Memo;
    }
}

/// <summary>.sbook 파일 하나. 빈 파일(탐색기 "새로 만들기"가 만든 0바이트)도 빈 목록으로 읽는다.</summary>
public sealed class ListDocument
{
    public int Version { get; set; } = 1;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SortMode Sort { get; set; } = SortMode.Custom;
    public List<ListItem> Items { get; set; } = new();

    static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ListDocument Load(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text)) return new ListDocument();
        return JsonSerializer.Deserialize<ListDocument>(text, Opt) ?? new ListDocument();
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, Opt);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }
}

/// <summary>화면에 보이는 한 줄. 아이콘은 보일 때 처음 읽는다.</summary>
public sealed class ItemVM : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public ListItem Item { get; }
    public ItemVM(ListItem item) { Item = item; }

    ImageSource? _icon;
    bool _iconLoaded;
    bool _missing;
    int _iconSize;

    public string Name => Item.Name;
    public string Description => Item.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Item.Description);
    public bool HasManual => !string.IsNullOrWhiteSpace(Item.ManualPath);
    public bool HasWeb => !string.IsNullOrWhiteSpace(Item.WebUrl);
    public bool HasMemo => !string.IsNullOrWhiteSpace(Item.Memo);

    public string ManualTip => Loc.T("tip_manual") + "\n" + Item.ManualPath;
    public string WebTip => Loc.T("tip_web") + "\n" + Item.WebUrl;
    public string MemoTip
    {
        get
        {
            var m = Item.Memo.Trim();
            return Loc.T("tip_memo") + "\n" + (m.Length > 200 ? m[..200] + "…" : m);
        }
    }

    public bool Missing { get { EnsureIcon(); return _missing; } }

    public ImageSource? Icon { get { EnsureIcon(); return _icon; } }

    public string FallbackGlyph
    {
        get
        {
            var t = Launcher.Expand(Item.Target);
            if (Launcher.IsUrl(t)) return G.Globe;
            if (Directory.Exists(t)) return G.Folder;
            return G.File;
        }
    }

    public bool HasIcon { get { EnsureIcon(); return _icon != null; } }

    void EnsureIcon()
    {
        int size = App.IconLogicalSize;
        if (_iconLoaded && _iconSize == size) return;
        _iconLoaded = true;
        _iconSize = size;
        var t = Launcher.Expand(Item.Target);
        _missing = t.Length > 0 && !Launcher.IsUrl(t) && !Launcher.IsShellApp(t) && !File.Exists(t) && !Directory.Exists(t);
        _icon = IconService.Get(Item, size);
    }

    int _dropMark;
    /// <summary>끌어 순서를 바꿀 때 이 줄 위(1)·아래(2)에 놓일 자리를 표시한다.</summary>
    public int DropMark { get => _dropMark; set { if (_dropMark != value) { _dropMark = value; Raise(); } } }

    bool _canReorder;
    public bool CanReorder { get => _canReorder; set { if (_canReorder != value) { _canReorder = value; Raise(); } } }

    /// <summary>내용이 바뀌었거나 크기 설정이 바뀌었을 때 화면 값을 다시 읽게 한다.</summary>
    public void Refresh()
    {
        _iconLoaded = false;
        Raise(string.Empty);
    }
}

/// <summary>Segoe MDL2 Assets 글리프</summary>
public static class G
{
    public const string Add = "";
    public const string Settings = "";
    public const string Sort = "";
    public const string Globe = "";
    public const string Manual = "";
    public const string Memo = "";
    public const string Clear = "";
    public const string Folder = "";
    public const string File = "";
}

public sealed class NameComparer : System.Collections.IComparer
{
    readonly bool _desc;
    public NameComparer(bool desc) { _desc = desc; }

    public int Compare(object? x, object? y)
    {
        var a = (x as ItemVM)?.Name ?? "";
        var b = (y as ItemVM)?.Name ?? "";
        int c = StringComparer.OrdinalIgnoreCase.Compare(a, b);
        return _desc ? -c : c;
    }
}
