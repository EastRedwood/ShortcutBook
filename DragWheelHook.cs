using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShortcutBook;

/// <summary>
/// 손잡이를 끄는 동안(DoDragDrop 이 마우스를 붙잡고 있는 동안)에는 마우스 휠 입력이 창에 오지 않는다.
/// 끄는 동안만 저수준 마우스 훅을 걸어 휠 값을 받아, 커서 아래에 있는 목록을 스크롤한다.
/// </summary>
sealed class DragWheelHook : IDisposable
{
    const int WH_MOUSE_LL = 14;
    const int WM_MOUSEWHEEL = 0x020A;

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT { public int X, Y; public uint MouseData, Flags, Time; public IntPtr Extra; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);

    readonly HookProc _proc; // 훅이 살아 있는 동안 수거되지 않게 필드로 잡아 둔다
    IntPtr _hook;
    readonly Action<Point, int> _onWheel;

    public DragWheelHook(Action<Point, int> onWheel)
    {
        _onWheel = onWheel;
        _proc = Proc;
        try { _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0); }
        catch (Exception ex) { Log.Write("휠 훅 설치 실패: " + ex.Message); }
    }

    IntPtr Proc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (int)wParam == WM_MOUSEWHEEL)
        {
            try
            {
                var s = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int delta = (short)(s.MouseData >> 16);
                _onWheel(new Point(s.X, s.Y), delta);
            }
            catch (Exception ex) { Log.Write("휠 처리 실패: " + ex.Message); }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }

    /// <summary>화면 좌표(px) 아래에 있는 목록 상자를 휠 값만큼 스크롤한다.</summary>
    public static void ScrollListAt(ListBox list, Point screenPx, int delta)
    {
        if (!list.IsVisible) return;
        Point p;
        try { p = list.PointFromScreen(screenPx); } catch { return; }
        if (p.X < 0 || p.Y < 0 || p.X > list.ActualWidth || p.Y > list.ActualHeight) return;
        var sv = Find(list);
        if (sv == null) return;
        double notches = delta / 120.0;
        double step = sv.CanContentScroll ? 3 : 48;
        sv.ScrollToVerticalOffset(sv.VerticalOffset - notches * step);
    }

    static ScrollViewer? Find(DependencyObject d)
    {
        if (d is ScrollViewer s) return s;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var r = Find(VisualTreeHelper.GetChild(d, i));
            if (r != null) return r;
        }
        return null;
    }
}
