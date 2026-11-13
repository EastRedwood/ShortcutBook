using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ShortcutBook;

/// <summary>지금 열려 있는 목록 파일의 설정 (현재는 목록 아이콘)</summary>
public partial class ListSettingsWindow : Window
{
    public string IconPath { get; private set; }

    public ListSettingsWindow(string listPath, string iconPath)
    {
        InitializeComponent();
        IconPath = iconPath;
        ListName.Text = Path.GetFileNameWithoutExtension(listPath);
        ListFile.Text = listPath;
        IconBox.Text = iconPath;
        UpdatePreview();
    }

    void OnIconChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    void UpdatePreview()
    {
        if (PreviewImage == null) return;
        var img = string.IsNullOrWhiteSpace(IconBox.Text) ? null : IconService.Get(new ListItem { IconPath = IconBox.Text }, 40);
        PreviewImage.Source = img;
        PreviewGlyph.Visibility = img == null ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = Loc.T("filter_icons") + "|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.exe;*.dll|" + Loc.T("filter_all") + "|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == true) IconBox.Text = dlg.FileName;
    }

    void OnClear(object sender, RoutedEventArgs e) => IconBox.Clear();

    void OnSave(object sender, RoutedEventArgs e)
    {
        IconPath = IconBox.Text.Trim().Trim('"');
        DialogResult = true;
    }
}
