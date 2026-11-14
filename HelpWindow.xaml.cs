using System.Windows;

namespace ShortcutBook;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        ThemeService.Hook(this);
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 40);
        // 수정 단축키는 설정에서 바꿀 수 있으므로 지금 설정된 키를 문장에 넣는다
        HowtoText.Text = Loc.F("help_howto", App.Settings.EditHotkey);
        VersionText.Text = AppInfo.VersionText;
        MailRun.Text = AppInfo.AuthorMail;
    }

    void OnMailClick(object sender, RoutedEventArgs e)
    {
        try { Launcher.ShellOpen("mailto:" + AppInfo.AuthorMail); }
        catch (Exception ex) { Log.Write("메일 열기 실패: " + ex.Message); }
    }
}
