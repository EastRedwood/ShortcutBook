using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
        HowtoText.Text = Loc.T("help_howto");
        BuildKeys();
        VersionText.Text = AppInfo.VersionText;
        MailRun.Text = AppInfo.AuthorMail;
    }

    /// <summary>
    /// 단축키 표를 만든다. 언어 파일의 help_keys 는 "# 묶음 이름" 줄과 "키 | 설명" 줄로 되어 있고,
    /// {0} 자리에는 설정에서 지금 지정한 수정 단축키가 들어간다.
    /// </summary>
    void BuildKeys()
    {
        foreach (var raw in Loc.F("help_keys", App.Settings.EditHotkey).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                KeysPanel.Children.Add(new TextBlock
                {
                    Text = line[2..], FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("SubText"),
                    Margin = new Thickness(0, KeysPanel.Children.Count == 0 ? 0 : 14, 0, 4),
                });
                continue;
            }
            var parts = line.Split(" | ", 2);
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = parts[0], FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 14, 0) });
            var desc = new TextBlock { Text = parts.Length > 1 ? parts[1] : "", TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(desc, 1);
            row.Children.Add(desc);
            KeysPanel.Children.Add(row);
        }
    }

    void OnMailClick(object sender, RoutedEventArgs e)
    {
        try { Launcher.ShellOpen("mailto:" + AppInfo.AuthorMail); }
        catch (Exception ex) { Log.Write("메일 열기 실패: " + ex.Message); }
    }
}
