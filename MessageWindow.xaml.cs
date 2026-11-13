using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ShortcutBook;

/// <summary>
/// 테마가 적용되는 메시지 상자. 윈도우 기본 MessageBox 는 다크 테마가 적용되지 않아서 같은 모양의
/// Show(...) 를 가진 이 클래스로 대신한다 (ShortcutBook 네임스페이스의 MessageBox 가 System.Windows.MessageBox 보다 먼저 선택된다).
/// </summary>
public static class MessageBox
{
    public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons, MessageBoxImage image)
        => Show(null, text, caption, buttons, image, MessageBoxResult.None);

    public static MessageBoxResult Show(Window? owner, string text, string caption, MessageBoxButton buttons, MessageBoxImage image)
        => Show(owner, text, caption, buttons, image, MessageBoxResult.None);

    public static MessageBoxResult Show(Window? owner, string text, string caption, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
    {
        var w = new MessageWindow(text, caption, buttons, image, defaultResult);
        if (owner != null && owner.IsVisible) w.Owner = owner;
        else { w.WindowStartupLocation = WindowStartupLocation.CenterScreen; w.Topmost = true; }
        w.ShowDialog();
        return w.Result;
    }
}

public partial class MessageWindow : Window
{
    public MessageBoxResult Result { get; private set; }
    readonly MessageBoxResult _cancelResult;

    public MessageWindow(string text, string caption, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
    {
        InitializeComponent();
        ThemeService.Hook(this);
        Title = caption;
        MessageText.Text = text;

        (IconGlyph.Text, IconGlyph.Foreground) = image switch
        {
            MessageBoxImage.Error => ("", (Brush)FindResource("Danger")),
            MessageBoxImage.Warning => ("", (Brush)new SolidColorBrush(Color.FromRgb(0xF2, 0xB1, 0x1B))),
            _ => ("", (Brush)FindResource("Accent")),
        };

        switch (buttons)
        {
            case MessageBoxButton.YesNo:
                Add("btn_yes", MessageBoxResult.Yes, defaultResult == MessageBoxResult.Yes);
                Add("btn_no", MessageBoxResult.No, defaultResult != MessageBoxResult.Yes);
                _cancelResult = MessageBoxResult.No;
                break;
            case MessageBoxButton.OKCancel:
                Add("btn_ok", MessageBoxResult.OK, defaultResult != MessageBoxResult.Cancel);
                Add("btn_cancel", MessageBoxResult.Cancel, defaultResult == MessageBoxResult.Cancel);
                _cancelResult = MessageBoxResult.Cancel;
                break;
            default:
                Add("btn_ok", MessageBoxResult.OK, true);
                _cancelResult = MessageBoxResult.OK;
                break;
        }
        Result = _cancelResult;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Result = _cancelResult; Close(); } };
        Loaded += (_, _) => Activate();
    }

    void Add(string key, MessageBoxResult result, bool isDefault)
    {
        var b = new Button
        {
            Content = Loc.T(key),
            Style = (Style)FindResource(isDefault ? "PrimaryButton" : "SecondaryButton"),
            IsDefault = isDefault,
            Margin = new Thickness(ButtonPanel.Children.Count == 0 ? 0 : 8, 0, 0, 0),
        };
        b.Click += (_, _) => { Result = result; Close(); };
        ButtonPanel.Children.Add(b);
    }
}
