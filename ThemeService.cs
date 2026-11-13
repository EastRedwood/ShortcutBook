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

    /// <summary>
    /// 제목 표시줄(비클라이언트 영역)을 테마에 맞춘다. ThemeMode 만으로는 제목 표시줄이 바뀌지 않아서
    /// 창마다 DWM 속성을 직접 지정해야 한다 (window\CLAUDE.md "다크 테마").
    /// </summary>
    /// <summary>
    /// 창 생성자에서 한 번 부른다. 창이 화면에 처음 그려지기 전(SourceInitialized)에 제목 표시줄 테마를 적용해야
    /// 열자마자 어둡다. Loaded 이후에 지정하면 창을 클릭하기 전까지 흰색으로 남는다 (WeatherBoard 와 같은 방식).
    /// </summary>
    public static void Hook(Window w) => w.SourceInitialized += (_, _) => ApplyTitleBar(w);

    public static void ApplyTitleBar(Window w)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = IsDark ? 1 : 0;
            // Windows 10 초기 빌드는 속성 번호가 19 이다
            if (Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                Native.DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
            if (!w.IsVisible) return; // 아직 그려지기 전이면 위 지정만으로 충분하다
            // 속성만 바꾸면 창을 클릭(활성화)하기 전까지 옛 색으로 남는다 -> 제목 표시줄을 즉시 다시 그리게 한다.
            Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x37 /* NOSIZE|NOMOVE|NOZORDER|NOACTIVATE|FRAMECHANGED */);
            Native.RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, 0x501 /* INVALIDATE|FRAME|UPDATENOW */);
        }
        catch (Exception ex) { Log.Write("제목 표시줄 테마 실패: " + ex.Message); }
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

        foreach (Window w in app.Windows) ApplyTitleBar(w);

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
