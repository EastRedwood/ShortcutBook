using System.Windows;

namespace ShortcutBook;

/// <summary>항목에 적어 둔 메모를 보여 주는 작은 창</summary>
public partial class MemoWindow : Window
{
    public MemoWindow(string title, string memo)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        Body.Text = memo;
    }
}
