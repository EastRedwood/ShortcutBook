using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ShortcutBook;

public partial class SettingsWindow : Window
{
    bool _loading = true;
    List<(string Code, string Name)> _langs = new();

    public SettingsWindow()
    {
        InitializeComponent();
        ThemeService.Hook(this);

        var radios = new[] { Size0, Size1, Size2, Size3 };
        radios[Math.Clamp(App.Settings.ViewSize, 0, 3)].IsChecked = true;

        _langs = Loc.Available();
        foreach (var l in _langs) LangCombo.Items.Add(l.Name);
        int idx = _langs.FindIndex(l => l.Code.Equals(Loc.Instance.Code, StringComparison.OrdinalIgnoreCase));
        LangCombo.SelectedIndex = Math.Max(0, idx);

        new[] { Theme0, Theme1, Theme2 }[Math.Clamp(App.Settings.Theme, 0, 2)].IsChecked = true;
        (App.Settings.OpenInNewWindow ? OpenNew : OpenReplace).IsChecked = true;
        new[] { Dup0, Dup1, Dup2 }[Math.Clamp(App.Settings.DuplicateMode, 0, 2)].IsChecked = true;
        EditKeyBox.Text = App.Settings.EditHotkey;
        NameWidthSlider.Value = Math.Clamp(App.Settings.NameWidth, 120, 600);
        NameWidthText.Text = ((int)NameWidthSlider.Value) + " px";

        ShellCheck.IsChecked = App.Settings.RegisterShell;
        UpdateShellStatus();
        VersionText.Text = AppInfo.VersionText;
        ListsPath.Text = AppInfo.ListsDir;
        ListsPath.ToolTip = AppInfo.ListsDir;
        Loc.Changed += OnLocChanged;
        Closed += (_, _) => { Loc.Changed -= OnLocChanged; App.Settings.Save(); };
        _loading = false;
    }

    void OnLocChanged()
    {
        UpdateShellStatus();
        VersionText.Text = AppInfo.VersionText;
    }

    void OnOpenListsFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppInfo.ListsDir);
            Launcher.ShellOpen(AppInfo.ListsDir);
        }
        catch (Exception ex) { Log.Write("목록 폴더 열기 실패: " + ex.Message); }
    }

    void OnNameWidthChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        App.Settings.NameWidth = NameWidthSlider.Value;
        NameWidthText.Text = ((int)NameWidthSlider.Value) + " px";
        App.ApplyNameWidth();
    }

    void OnSizeChecked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (sender is RadioButton { Tag: string t } && int.TryParse(t, out int v))
        {
            App.Settings.ViewSize = v;
            App.Settings.Save();
            App.ApplyViewSize();
        }
    }

    void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (sender is RadioButton { Tag: string t } && int.TryParse(t, out int v))
        {
            App.Settings.Theme = v;
            App.Settings.Save();
            ThemeService.Apply();
        }
    }

    void OnOpenModeChecked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Settings.OpenInNewWindow = OpenNew.IsChecked == true;
        App.Settings.Save();
    }

    void OnDupChecked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (sender is RadioButton { Tag: string t } && int.TryParse(t, out int v))
        {
            App.Settings.DuplicateMode = v;
            App.Settings.Save();
        }
    }

    /// <summary>이 칸에서 누른 키 조합을 수정 단축키로 저장한다.</summary>
    void OnEditKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) { e.Handled = false; return; }
        if (!Hotkey.IsAcceptable(key, Keyboard.Modifiers)) return;
        var text = Hotkey.Format(key, Keyboard.Modifiers);
        EditKeyBox.Text = text;
        App.Settings.EditHotkey = text;
        App.Settings.Save();
    }

    void OnEditKeyDefault(object sender, RoutedEventArgs e)
    {
        EditKeyBox.Text = Hotkey.DefaultEdit;
        App.Settings.EditHotkey = Hotkey.DefaultEdit;
        App.Settings.Save();
    }

    void OnLangChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LangCombo.SelectedIndex < 0) return;
        App.ChangeLanguage(_langs[LangCombo.SelectedIndex].Code);
    }

    void OnShellToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool on = ShellCheck.IsChecked == true;
        App.Settings.RegisterShell = on;
        App.Settings.Save();
        try
        {
            if (on) ShellRegistration.Register(Environment.ProcessPath ?? "");
            else ShellRegistration.Unregister();
        }
        catch (Exception ex)
        {
            Log.Write("탐색기 메뉴 설정 실패: " + ex.Message);
            MessageBox.Show(this, Loc.F("err_shell", ex.Message), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        UpdateShellStatus();
    }

    void UpdateShellStatus()
    {
        ShellStatus.Text = Loc.T(ShellRegistration.IsRegistered() ? "shell_on" : "shell_off");
    }
}
