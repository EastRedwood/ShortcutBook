using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ShortcutBook;

/// <summary>북마크 HTML 에서 가져올 북마크만 체크해서 고르는 창.</summary>
public partial class BookmarkImportWindow : Window
{
    readonly BmNode _root;
    readonly List<BmNode> _leaves;

    /// <summary>고른 북마크 (이름, 주소, 폴더 경로)</summary>
    public List<(string Name, string Url, string Folder)> Result { get; } = new();
    public bool FolderAsDescription { get; private set; }

    public BookmarkImportWindow(BmNode root, string fileName)
    {
        InitializeComponent();
        ThemeService.Hook(this);
        _root = root;
        _leaves = root.Leaves().ToList();
        FileText.Text = fileName;
        Tree.ItemsSource = root.Children;

        // 선택 개수를 보여 주려고 모든 북마크의 선택 변화를 지켜본다 (수천 개여도 가벼운 이벤트 하나씩이다)
        foreach (var n in _leaves) n.PropertyChanged += OnNodeChanged;
        Closed += (_, _) => { foreach (var n in _leaves) n.PropertyChanged -= OnNodeChanged; };
        UpdateCount();
        Loaded += (_, _) => SearchBox.Focus();
    }

    bool _countPending;

    void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BmNode.IsChecked) || _countPending) return;
        // 전체 선택처럼 한꺼번에 바뀔 때 개수를 매번 세지 않고 한 번만 센다
        _countPending = true;
        Dispatcher.BeginInvoke(new Action(() => { _countPending = false; UpdateCount(); }), System.Windows.Threading.DispatcherPriority.Background);
    }

    void UpdateCount()
    {
        int n = _leaves.Count(l => l.IsChecked == true);
        CountText.Text = Loc.F("bm_count", n, _leaves.Count);
        ImportBtn.IsEnabled = n > 0;
    }

    void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim();
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _root.Filter(q);
        // 검색을 지우면 맨 위 폴더만 펼친 처음 모양으로 돌아간다
        if (q.Length == 0)
            foreach (var c in _root.Children) if (c.IsFolder) c.IsExpanded = true;
    }

    /// <summary>지금 보이는(검색에 걸린) 북마크만 모두 선택/해제한다.</summary>
    void SetVisible(bool value)
    {
        foreach (var l in _leaves)
            if (IsShown(l)) l.IsChecked = value;
    }

    static bool IsShown(BmNode n)
    {
        for (var p = n; p != null; p = p.Parent)
            if (!p.IsVisible && p.Parent != null) return false;
        return true;
    }

    void OnSelectAll(object sender, RoutedEventArgs e) => SetVisible(true);
    void OnSelectNone(object sender, RoutedEventArgs e) => SetVisible(false);

    void OnImport(object sender, RoutedEventArgs e)
    {
        foreach (var l in _leaves)
            if (l.IsChecked == true) Result.Add((l.Name, l.Url, l.FolderPath));
        FolderAsDescription = FolderCheck.IsChecked == true;
        DialogResult = true;
    }
}
