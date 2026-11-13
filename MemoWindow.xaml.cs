using System.Windows;
using System.Windows.Input;

namespace ShortcutBook;

/// <summary>항목에 적어 둔 메모를 보여 주는 작은 창. 제목 표시줄의 X 나 Esc 로 닫는다.</summary>
public partial class MemoWindow : Window
{
    public MemoWindow(string title, string memo)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        Body.Text = memo;
    }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
