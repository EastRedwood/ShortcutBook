using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ShortcutBook;

/// <summary>설치된 앱(Windows 스토어 앱 포함)을 이름으로 찾아 고르는 창. 고른 앱은 Selected 로 돌려준다.</summary>
public partial class AppPickerWindow : Window
{
    IReadOnlyList<InstalledApp> _all = Array.Empty<InstalledApp>();

    public InstalledApp? Selected { get; private set; }

    public AppPickerWindow()
    {
        InitializeComponent();
        ThemeService.Hook(this);
        Loaded += async (_, _) =>
        {
            SearchBox.Focus();
            _all = await AppCatalog.LoadAsync();
            ApplyFilter();
        };
    }

    void OnSearchChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }

    void ApplyFilter()
    {
        var rows = AppCatalog.Search(_all, SearchBox.Text).Select(a => new AppRow(a)).ToList();
        AppList.ItemsSource = rows;
        StatusText.Text = Loc.T("app_picker_none");
        StatusText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rows.Count > 0) AppList.SelectedIndex = 0;
        OkBtn.IsEnabled = rows.Count > 0;
    }

    /// <summary>검색 칸에서 위/아래 화살표로 목록을 고를 수 있게 한다.</summary>
    void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Down or Key.Up) || AppList.Items.Count == 0) return;
        int i = Math.Clamp(AppList.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, AppList.Items.Count - 1);
        AppList.SelectedIndex = i;
        AppList.ScrollIntoView(AppList.SelectedItem);
        e.Handled = true;
    }

    void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
    }

    void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 스크롤 막대 같은 곳을 두 번 눌렀을 때는 고르지 않는다
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(AppList, d) != null) Accept();
    }

    void OnOk(object sender, RoutedEventArgs e) => Accept();

    void Accept()
    {
        if (AppList.SelectedItem is not AppRow row) return;
        Selected = row.App;
        DialogResult = true;
    }
}
