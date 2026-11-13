using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace ShortcutBook;

/// <summary>시스템 · 라이트 · 다크 테마. 기본 컨트롤은 .NET 9 의 Fluent ThemeMode 가, 이 프로그램의 색은 아래 팔레트가 바꾼다.</summary>
internal static class ThemeService
{
    static readonly Dictionary<string, string> Light = new()
    {
        ["Bg"] = "#FFFFFF", ["PanelBg"] = "#F5F6F8", ["Line"] = "#E3E5E9", ["LineStrong"] = "#C9CDD4",
        ["Text"] = "#1F2328", ["SubText"] = "#6B7280", ["Accent"] = "#2F6FED", ["AccentSoft"] = "#E8F0FE",
        ["Hover"] = "#EEF0F3", ["Danger"] = "#D13438",
    };

    static readonly Dictionary<string, string> Dark = new()
    {
        ["Bg"] = "#1E1F22", ["PanelBg"] = "#2B2D31", ["Line"] = "#3A3C41", ["LineStrong"] = "#52555B",
        ["Text"] = "#E6E7EA", ["SubText"] = "#9AA0A9", ["Accent"] = "#5B8DEF", ["AccentSoft"] = "#2A3A5C",
        ["Hover"] = "#34363B", ["Danger"] = "#F0646A",
    };

    public static bool IsDark { get; private set; }
    static bool _hooked;

    static bool SystemUsesDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>0 시스템 · 1 라이트 · 2 다크</summary>
    public static void Apply()
    {
        var app = Application.Current;
        int mode = App.Settings.Theme;
        IsDark = mode == 2 || (mode == 0 && SystemUsesDark());

        app.ThemeMode = mode switch { 1 => ThemeMode.Light, 2 => ThemeMode.Dark, _ => ThemeMode.System };

        foreach (var kv in IsDark ? Dark : Light)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(kv.Value));
            b.Freeze();
            app.Resources[kv.Key] = b;
        }

        if (!_hooked)
        {
            _hooked = true;
            // 시스템 테마를 따를 때는 윈도우 설정이 바뀌면 같이 바꾼다.
            SystemEvents.UserPreferenceChanged += (_, _) =>
            {
                if (App.Settings.Theme == 0) app.Dispatcher.BeginInvoke(new Action(Apply));
            };
        }
    }
}
