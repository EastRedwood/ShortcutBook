using System.Windows;
using System.Windows.Controls;

namespace ShortcutBook;

public partial class SettingsWindow : Window
{
    bool _loading = true;
    List<(string Code, string Name)> _langs = new();

    public SettingsWindow()
    {
        InitializeComponent();

        var radios = new[] { Size0, Size1, Size2, Size3 };
        radios[Math.Clamp(App.Settings.ViewSize, 0, 3)].IsChecked = true;

        _langs = Loc.Available();
        foreach (var l in _langs) LangCombo.Items.Add(l.Name);
        int idx = _langs.FindIndex(l => l.Code.Equals(Loc.Instance.Code, StringComparison.OrdinalIgnoreCase));
        LangCombo.SelectedIndex = Math.Max(0, idx);

        ShellCheck.IsChecked = App.Settings.RegisterShell;
        UpdateShellStatus();
        VersionText.Text = AppInfo.VersionText;
        Loc.Changed += OnLocChanged;
        Closed += (_, _) => Loc.Changed -= OnLocChanged;
        _loading = false;
    }

    void OnLocChanged()
    {
        UpdateShellStatus();
        VersionText.Text = AppInfo.VersionText;
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
