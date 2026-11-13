using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ShortcutBook;

public partial class ItemEditWindow : Window
{
    readonly ListItem _item;
    readonly bool _isNew;

    /// <summary>저장한 결과 (취소했거나 삭제했다면 쓰지 않는다)</summary>
    public ListItem Result => _item;
    public bool Deleted { get; private set; }

    public ItemEditWindow(ListItem item, bool isNew)
    {
        _item = item;
        _isNew = isNew;
        InitializeComponent();
        ThemeService.Hook(this);
        MaxHeight = SystemParameters.WorkArea.Height * 0.94;

        WindowCombo.Items.Add(Loc.T("win_normal"));
        WindowCombo.Items.Add(Loc.T("win_min"));
        WindowCombo.Items.Add(Loc.T("win_max"));

        NameBox.Text = item.Name;
        DescBox.Text = item.Description;
        TargetBox.Text = item.Target;
        ArgsBox.Text = item.Arguments;
        WorkDirBox.Text = item.WorkDir;
        IconBox.Text = item.IconPath;
        WindowCombo.SelectedIndex = Math.Clamp(item.WindowStyle, 0, 2);
        AdminCheck.IsChecked = item.RunAsAdmin;
        ManualBox.Text = item.ManualPath;
        WebBox.Text = item.WebUrl;
        MemoBox.Text = item.Memo;

        Title = Loc.T(isNew ? "add_title" : "edit_title");
        DeleteBtn.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;
        UpdatePreview();
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
        // Ctrl+Enter = 바로 저장하고 닫기 (여러 줄 입력 칸에서도 줄바꿈이 되지 않게 먼저 가로챈다)
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                e.Handled = true;
                OnSave(this, new RoutedEventArgs());
            }
        };
    }

    // ─────────────── 아이콘 미리보기 ───────────────

    void OnTargetChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    void UpdatePreview()
    {
        if (PreviewImage == null) return;
        var probe = new ListItem { Target = TargetBox.Text, IconPath = IconBox.Text };
        var img = IconService.Get(probe, 40);
        PreviewImage.Source = img;
        PreviewGlyph.Visibility = img == null ? Visibility.Visible : Visibility.Collapsed;
        var t = Launcher.Expand(TargetBox.Text);
        PreviewGlyph.Text = Launcher.IsUrl(t) ? G.Globe : Directory.Exists(t) ? G.Folder : G.File;
    }

    // ─────────────── 찾아보기 ───────────────

    static string? PickFile(Window owner, string filter, string? current)
    {
        var dlg = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        var dir = Launcher.Expand(current);
        try { if (dir.Length > 0 && Path.GetDirectoryName(dir) is { Length: > 0 } d && Directory.Exists(d)) dlg.InitialDirectory = d; } catch { }
        return dlg.ShowDialog(owner) == true ? dlg.FileName : null;
    }

    static string? PickFolder(Window owner)
    {
        var dlg = new OpenFolderDialog();
        return dlg.ShowDialog(owner) == true ? dlg.FolderName : null;
    }

    void OnBrowseTargetFile(object sender, RoutedEventArgs e)
    {
        var f = PickFile(this, Loc.T("filter_all") + "|*.*|" + Loc.T("filter_programs") + "|*.exe;*.bat;*.cmd;*.lnk|" + Loc.T("filter_list") + "|*" + AppInfo.Extension, TargetBox.Text);
        if (f == null) return;
        ApplyTarget(f);
    }

    void OnBrowseTargetFolder(object sender, RoutedEventArgs e)
    {
        var f = PickFolder(this);
        if (f != null) ApplyTarget(f);
    }

    /// <summary>대상을 정하고, 이름이 비어 있으면 대상에서 이름을 만든다. 바로가기(.lnk)면 안의 설정도 가져온다.</summary>
    void ApplyTarget(string path)
    {
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var imp = ShortcutImport.FromPath(path);
            TargetBox.Text = imp.Target;
            ArgsBox.Text = imp.Arguments;
            WorkDirBox.Text = imp.WorkDir;
            if (imp.IconPath.Length > 0) IconBox.Text = imp.IconPath;
            WindowCombo.SelectedIndex = imp.WindowStyle;
            if (NameBox.Text.Trim().Length == 0) NameBox.Text = imp.Name;
            if (DescBox.Text.Trim().Length == 0) DescBox.Text = imp.Description;
            return;
        }
        TargetBox.Text = path;
        if (NameBox.Text.Trim().Length == 0) NameBox.Text = ShortcutImport.FromPath(path).Name;
    }

    void OnBrowseWorkDir(object sender, RoutedEventArgs e)
    {
        var f = PickFolder(this);
        if (f != null) WorkDirBox.Text = f;
    }

    /// <summary>설치된 앱(스토어 앱 포함)을 목록에서 골라 대상으로 삼는다.</summary>
    void OnPickApp(object sender, RoutedEventArgs e)
    {
        var dlg = new AppPickerWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Selected is not { } app) return;
        TargetBox.Text = app.Target;
        ArgsBox.Clear();
        WorkDirBox.Clear();
        if (NameBox.Text.Trim().Length == 0) NameBox.Text = app.Name;
    }

    void OnBrowseIcon(object sender, RoutedEventArgs e)
    {
        var f = PickFile(this, Loc.T("filter_icons") + "|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.exe;*.dll|" + Loc.T("filter_all") + "|*.*", IconBox.Text);
        if (f != null) IconBox.Text = f;
    }

    void OnClearIcon(object sender, RoutedEventArgs e) => IconBox.Clear();

    void OnBrowseManual(object sender, RoutedEventArgs e)
    {
        var f = PickFile(this, Loc.T("filter_all") + "|*.*", ManualBox.Text);
        if (f != null) ManualBox.Text = f;
    }

    // ─────────────── 대상 칸에 파일 끌어놓기 ───────────────

    void OnTargetDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnTargetDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) ApplyTarget(files[0]);
        e.Handled = true;
    }

    // ─────────────── 저장 · 삭제 ───────────────

    void OnSave(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            var t = Launcher.Expand(TargetBox.Text);
            if (t.Length > 0) name = Launcher.IsUrl(t) ? ShortcutImport.FromUrl(t).Name : ShortcutImport.FromPath(t).Name;
        }
        if (name.Length == 0)
        {
            MessageBox.Show(this, Loc.T("err_name_required"), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            NameBox.Focus();
            return;
        }

        _item.Name = name;
        _item.Description = DescBox.Text.Trim();
        _item.Target = TargetBox.Text.Trim().Trim('"');
        _item.Arguments = ArgsBox.Text.Trim();
        _item.WorkDir = WorkDirBox.Text.Trim().Trim('"');
        _item.IconPath = IconBox.Text.Trim().Trim('"');
        _item.WindowStyle = Math.Max(0, WindowCombo.SelectedIndex);
        _item.RunAsAdmin = AdminCheck.IsChecked == true;
        _item.ManualPath = ManualBox.Text.Trim().Trim('"');
        _item.WebUrl = WebBox.Text.Trim();
        _item.Memo = MemoBox.Text.TrimEnd();
        DialogResult = true;
    }

    void OnDelete(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(this, Loc.F("confirm_delete", _item.Name), AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (r != MessageBoxResult.Yes) return;
        Deleted = true;
        DialogResult = true;
    }
}
