using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Text.RegularExpressions;

namespace ShortcutBook;

/// <summary>북마크 트리의 한 노드(폴더 또는 북마크). 가져오기 창의 체크 트리에 그대로 쓴다.</summary>
public sealed class BmNode : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool IsFolder { get; set; }
    public BmNode? Parent { get; set; }
    public ObservableCollection<BmNode> Children { get; } = new();

    public string Glyph => IsFolder ? G.Folder : G.Globe;

    /// <summary>상위 폴더 경로 ("북마크바 / 개발")</summary>
    public string FolderPath
    {
        get
        {
            var parts = new List<string>();
            for (var p = IsFolder ? this : Parent; p != null; p = p.Parent)
                if (p.Name.Length > 0) parts.Insert(0, p.Name);
            return string.Join(" / ", parts);
        }
    }

    bool? _checked = false;
    /// <summary>폴더는 아래 북마크의 선택 상태를 따라 3상태(일부 선택)가 된다. 사용자가 누르면 선택/해제만 번갈아 바뀐다.</summary>
    public bool? IsChecked
    {
        get => _checked;
        set
        {
            if (value == null) value = false; // 사용자의 클릭으로는 "일부 선택" 상태를 만들지 않는다
            ApplyDown(value);
            Parent?.Recompute();
            Raise(nameof(IsChecked)); // 값을 바꿔 넣었을 때도 체크 상자가 다시 읽게 한다
        }
    }

    /// <summary>이 노드와 보이는 아래 항목들의 선택을 바꾼다 (검색으로 가려진 항목은 건드리지 않는다).</summary>
    void ApplyDown(bool? v)
    {
        _checked = v;
        Raise(nameof(IsChecked));
        if (!IsFolder) return;
        foreach (var c in Children)
            if (c.IsVisible) c.ApplyDown(v);
        // 가려진 항목이 섞여 있으면 폴더 상태를 다시 계산한다
        if (Children.Any(c => !c.IsVisible)) Recompute(up: false);
    }

    void Recompute(bool up = true)
    {
        bool all = Children.All(c => c._checked == true), none = Children.All(c => c._checked == false);
        var v = all ? true : none ? false : (bool?)null;
        if (_checked != v) { _checked = v; Raise(nameof(IsChecked)); }
        if (up) Parent?.Recompute();
    }

    bool _visible = true;
    public bool IsVisible { get => _visible; set { if (_visible != value) { _visible = value; Raise(nameof(IsVisible)); } } }

    bool _expanded;
    public bool IsExpanded { get => _expanded; set { if (_expanded != value) { _expanded = value; Raise(nameof(IsExpanded)); } } }

    /// <summary>검색어에 맞는 북마크만 보이게 한다 (폴더는 안에 보이는 것이 있을 때만). 검색 중에는 폴더를 펼친다.</summary>
    public bool Filter(string q)
    {
        bool vis;
        if (IsFolder)
        {
            vis = false;
            foreach (var c in Children) vis |= c.Filter(q);
            if (q.Length > 0) IsExpanded = vis;
        }
        else vis = q.Length == 0 || Name.Contains(q, StringComparison.OrdinalIgnoreCase) || Url.Contains(q, StringComparison.OrdinalIgnoreCase);
        IsVisible = vis;
        return vis;
    }

    public IEnumerable<BmNode> Leaves()
    {
        if (!IsFolder) { yield return this; yield break; }
        foreach (var c in Children)
            foreach (var l in c.Leaves()) yield return l;
    }
}

/// <summary>브라우저가 내보낸 북마크 HTML(Netscape 형식)을 읽는다. Chrome · Edge · Firefox · Brave 등이 모두 같은 형식이다.</summary>
internal static partial class BookmarkHtml
{
    // <DL> 시작 · </DL> 끝 · <H3>폴더 이름</H3> · <A HREF="주소">제목</A>. 내보낸 파일은 <DT>·<p> 를 닫지 않아서 이 네 가지만 본다.
    [GeneratedRegex(@"<DL\b[^>]*>|</DL\s*>|<H3\b[^>]*>(?<folder>.*?)</H3\s*>|<A\b(?<attrs>[^>]*)>(?<title>.*?)</A\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Tokens();

    [GeneratedRegex(@"\bhref\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)')", RegexOptions.IgnoreCase)]
    private static partial Regex Href();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    static string Clean(string s) => WebUtility.HtmlDecode(Tags().Replace(s, "")).Trim();

    static bool Accept(string url)
    {
        // 브라우저 안에서만 쓰는 주소(javascript:, place:, chrome:// …)는 바로가기로 쓸 수 없다
        return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
    }

    public static BmNode Parse(string html)
    {
        var root = new BmNode { IsFolder = true };
        var stack = new Stack<BmNode>();
        stack.Push(root);
        BmNode? pending = null;

        foreach (Match m in Tokens().Matches(html))
        {
            var v = m.Value;
            if (v.StartsWith("<DL", StringComparison.OrdinalIgnoreCase))
            {
                stack.Push(pending ?? stack.Peek()); // 폴더 제목 바로 뒤의 <DL> 은 그 폴더의 내용
                pending = null;
            }
            else if (v.StartsWith("</DL", StringComparison.OrdinalIgnoreCase))
            {
                if (stack.Count > 1) stack.Pop();
            }
            else if (m.Groups["folder"].Success)
            {
                var parent = stack.Peek();
                var f = new BmNode { IsFolder = true, Name = Clean(m.Groups["folder"].Value), Parent = parent };
                parent.Children.Add(f);
                pending = f;
            }
            else if (m.Groups["attrs"].Success)
            {
                var hm = Href().Match(m.Groups["attrs"].Value);
                if (!hm.Success) continue;
                var url = WebUtility.HtmlDecode(hm.Groups["v"].Value).Trim();
                if (!Accept(url)) continue;
                var parent = stack.Peek();
                var name = Clean(m.Groups["title"].Value);
                if (name.Length == 0) name = url;
                parent.Children.Add(new BmNode { Name = name, Url = url, Parent = parent });
            }
        }

        Prune(root);
        // 맨 위 단계의 폴더는 펼쳐서 보여 준다
        foreach (var c in root.Children) if (c.IsFolder) c.IsExpanded = true;
        return root;
    }

    /// <summary>북마크가 하나도 없는 폴더는 뺀다.</summary>
    static bool Prune(BmNode n)
    {
        for (int i = n.Children.Count - 1; i >= 0; i--)
        {
            var c = n.Children[i];
            if (c.IsFolder && !Prune(c)) n.Children.RemoveAt(i);
        }
        return n.Children.Count > 0;
    }
}
