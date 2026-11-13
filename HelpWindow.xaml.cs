using System.Windows;

namespace ShortcutBook;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        ThemeService.Hook(this);
        VersionText.Text = AppInfo.VersionText;
        MailRun.Text = AppInfo.AuthorMail;
    }

    void OnMailClick(object sender, RoutedEventArgs e)
    {
        try { Launcher.ShellOpen("mailto:" + AppInfo.AuthorMail); }
        catch (Exception ex) { Log.Write("메일 열기 실패: " + ex.Message); }
    }
}
