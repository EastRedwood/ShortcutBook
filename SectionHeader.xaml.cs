using System.Windows;
using System.Windows.Controls;

namespace ShortcutBook;

/// <summary>설정 항목의 제목. 안내글이 있으면 제목 옆에 ? 단추가 생기고, 누르면 안내글이 팝업으로 뜬다.</summary>
public partial class SectionHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SectionHeader),
        new PropertyMetadata("", (d, e) => ((SectionHeader)d).TitleText.Text = (string?)e.NewValue ?? ""));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(SectionHeader),
        new PropertyMetadata("", (d, e) =>
        {
            var h = (SectionHeader)d;
            var text = (string?)e.NewValue ?? "";
            h.HintText.Text = text;
            h.HintBtn.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }

    long _closedAt;

    public SectionHeader()
    {
        InitializeComponent();
        HintBtn.Visibility = Visibility.Collapsed;
    }

    void OnHintClick(object sender, RoutedEventArgs e)
    {
        // 열려 있는 팝업은 단추를 누르는 순간 먼저 닫힌다 (StaysOpen=False). 그 직후의 클릭은 "닫기"로 보고 다시 열지 않는다.
        if (Environment.TickCount64 - _closedAt < 250) return;
        HintPopup.IsOpen = true;
    }

    void OnPopupClosed(object? sender, EventArgs e) => _closedAt = Environment.TickCount64;
}
