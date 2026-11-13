using System.Windows;

namespace ShortcutBook;

/// <summary>이름 하나를 묻는 작은 창</summary>
public partial class InputWindow : Window
{
    public string Value { get; private set; } = "";

    public InputWindow(string title, string prompt, string defaultText)
    {
        InitializeComponent();
        Title = title;
        Prompt.Text = prompt;
        Box.Text = defaultText;
        Loaded += (_, _) => { Activate(); Box.Focus(); Box.SelectAll(); };
    }

    void OnOk(object sender, RoutedEventArgs e)
    {
        Value = Box.Text;
        DialogResult = true;
    }
}
