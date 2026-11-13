using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace ShortcutBook;

/// <summary>프로그램을 직접 실행했을 때(열 목록을 지정하지 않았을 때) 보이는 시작 창</summary>
public partial class StartWindow : Window
{
    sealed record RecentEntry(string Path)
    {
        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
        public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
    }

    public StartWindow()
    {
        InitializeComponent();
        LoadRecent();
    }

    void LoadRecent()
    {
        var list = App.Settings.Recent.Where(File.Exists).Select(p => new RecentEntry(p)).ToList();
        RecentList.ItemsSource = list;
        NoRecent.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnRecentDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentList.SelectedItem is RecentEntry r) App.Instance.OpenList(r.Path);
    }

    void OnNew(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = Loc.T("filter_list") + "|*" + AppInfo.Extension,
            DefaultExt = AppInfo.Extension,
            FileName = Loc.T("shell_new_name"),
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            new ListDocument().Save(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.F("err_save_list", ex.Message), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        App.Instance.OpenList(dlg.FileName);
    }

    void OnOpen(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = Loc.T("filter_list") + "|*" + AppInfo.Extension, CheckFileExists = true };
        if (dlg.ShowDialog(this) == true) App.Instance.OpenList(dlg.FileName);
    }

    void OnSettings(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this }.ShowDialog();
    void OnHelp(object sender, RoutedEventArgs e) => new HelpWindow { Owner = this }.ShowDialog();
}
