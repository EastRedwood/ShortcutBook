using System.IO.Pipes;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ShortcutBook;

public partial class App : Application
{
    public static App Instance => (App)Current;
    public static AppSettings Settings { get; private set; } = new();

    /// <summary>설정이나 언어가 바뀌었을 때 열려 있는 창이 다시 그리도록 알린다.</summary>
    public static event Action? ViewChanged;

    // 목록 크기 (0 작게 · 1 중간 · 2 크게 · 3 아주 크게): 아이콘, 이름 글자, 설명 글자, 버튼, 버튼 글리프, 줄 여백
    static readonly (double icon, double name, double desc, double btn, double glyph, Thickness pad)[] Sizes =
    {
        (24, 13, 11, 26, 13, new Thickness(6, 4, 6, 4)),
        (36, 15, 12, 30, 15, new Thickness(8, 6, 8, 6)),
        (48, 17, 13, 36, 18, new Thickness(10, 8, 10, 8)),
        (72, 20, 15, 44, 22, new Thickness(12, 10, 12, 10)),
    };

    public static int IconLogicalSize => (int)Sizes[Math.Clamp(Settings.ViewSize, 0, 3)].icon;

    Mutex? _mutex;
    readonly Dictionary<string, MainWindow> _lists = new(StringComparer.OrdinalIgnoreCase);
    StartWindow? _start;
    CancellationTokenSource? _pipeCts;

    static string PipeName => "ShortcutBook.Pipe." + Environment.UserName;
    static string MutexName => "ShortcutBook.Single." + Environment.UserName;

    protected override void OnStartup(StartupEventArgs e)
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        base.OnStartup(e);

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Write("처리되지 않은 오류: " + ex.Exception);
            MessageBox.Show(Loc.F("err_unhandled", ex.Exception.Message), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Write("치명적 오류: " + ex.ExceptionObject);

        var args = e.Args;
        Settings = AppSettings.Load();
        Loc.Instance.Load(Settings.Language);

        // 개발용: 창을 그림 파일로 저장하고 끝낸다 (입력 장치 없이 화면을 확인하는 용도)
        if (args.Length >= 3 && args[0] == "--render")
        {
            ApplyViewSize();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => RenderMode_(args)));
            return;
        }

        // 단일 인스턴스: 이미 실행 중이면 그쪽으로 넘기고 끝낸다.
        _mutex = new Mutex(true, MutexName, out bool created);
        bool exitOnly = args.Length > 0 && args[0] == "--exit";
        if (!created)
        {
            var payload = exitOnly ? new[] { "EXIT" } : BuildMessages(args);
            if (SendToRunning(payload)) { Shutdown(); return; }
            // 이전 인스턴스가 막 끝나는 중이면 잠깐 기다렸다가 이어받는다.
            try { if (!_mutex.WaitOne(3000)) { Shutdown(); return; } }
            catch (AbandonedMutexException) { }
        }
        if (exitOnly) { Shutdown(); return; }

        ApplyViewSize();
        StartPipeServer();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(EnsureShellRegistration));
        HandleArgs(args);
        if (_lists.Count == 0 && _start == null) Shutdown();

        // 시작 20초 뒤 한 번 메모리를 정리한다 (window\CLAUDE.md "메모리")
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        t.Tick += (_, _) => { t.Stop(); TrimMemory(); };
        t.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCts?.Cancel();
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    // ─────────────── 표시 크기 · 언어 ───────────────

    public static void ApplyViewSize()
    {
        var s = Sizes[Math.Clamp(Settings.ViewSize, 0, 3)];
        var r = Current.Resources;
        r["IconSize"] = s.icon;
        r["GlyphBig"] = Math.Round(s.icon * 0.62);
        r["NameSize"] = s.name;
        r["DescSize"] = s.desc;
        r["BtnSize"] = s.btn;
        r["GlyphSize"] = s.glyph;
        r["RowPadding"] = s.pad;
        IconService.Clear();
        ViewChanged?.Invoke();
    }

    public static void ChangeLanguage(string code)
    {
        Settings.Language = code;
        Settings.Save();
        Loc.Instance.Load(code);
        if (Settings.RegisterShell) Instance.EnsureShellRegistration();
        ViewChanged?.Invoke();
    }

    public void EnsureShellRegistration()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            if (Settings.RegisterShell)
            {
                if (!ShellRegistration.IsCurrent(exe)) { ShellRegistration.Register(exe); Log.Write("탐색기 메뉴 등록: " + exe); }
            }
        }
        catch (Exception ex) { Log.Write("탐색기 메뉴 등록 실패: " + ex.Message); }
    }

    // ─────────────── 창 열기 ───────────────

    void HandleArgs(string[] args)
    {
        foreach (var a in args)
        {
            if (a.StartsWith("--")) continue;
            OpenList(a);
        }
        if (_lists.Count == 0) ShowStart();
    }

    public void OpenList(string path)
    {
        try { path = Path.GetFullPath(path); } catch { }
        if (!File.Exists(path))
        {
            MessageBox.Show(Loc.F("err_target_missing", path), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_lists.TryGetValue(path, out var existing)) { Bring(existing); return; }

        var w = ShortcutBook.MainWindow.TryCreate(path);
        if (w == null) return;
        _lists[path] = w;
        w.Closed += (_, _) => { _lists.Remove(path); MemoryTrimLater(); };
        Settings.AddRecent(path);
        Settings.Save();
        w.Show();
        Bring(w);
        if (_start != null) { _start.Close(); }
    }

    public void ShowStart()
    {
        if (_start != null) { Bring(_start); return; }
        _start = new StartWindow();
        _start.Closed += (_, _) => { _start = null; MemoryTrimLater(); };
        _start.Show();
    }

    static void Bring(Window w)
    {
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Show();
        w.Activate();
        w.Topmost = true;   // 다른 프로그램 뒤에 숨지 않게 한 번 올렸다가 내린다
        w.Topmost = false;
        w.Focus();
    }

    // ─────────────── 단일 인스턴스 통신 ───────────────

    static string[] BuildMessages(string[] args)
    {
        var list = new List<string>();
        foreach (var a in args)
        {
            if (a.StartsWith("--")) continue;
            try { list.Add("OPEN|" + Path.GetFullPath(a)); } catch { }
        }
        if (list.Count == 0) list.Add("SHOW");
        return list.ToArray();
    }

    static bool SendToRunning(string[] lines)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(1500);
                Native.AllowSetForegroundWindow(Native.ASFW_ANY);
                using var w = new StreamWriter(client, new UTF8Encoding(false));
                foreach (var l in lines) w.WriteLine(l);
                w.Flush();
                return true;
            }
            catch (Exception) { Thread.Sleep(300); }
        }
        return false;
    }

    void StartPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token);
                    using var reader = new StreamReader(server, new UTF8Encoding(false));
                    var lines = new List<string>();
                    string? line;
                    while ((line = await reader.ReadLineAsync(token)) != null) lines.Add(line);
                    Dispatcher.Invoke(() => HandleMessages(lines));
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Log.Write("통신 오류: " + ex.Message); await Task.Delay(500); }
            }
        });
    }

    void HandleMessages(List<string> lines)
    {
        bool opened = false;
        foreach (var l in lines)
        {
            if (l == "EXIT") { Shutdown(); return; }
            if (l.StartsWith("OPEN|")) { OpenList(l[5..]); opened = true; }
        }
        if (!opened)
        {
            var any = _lists.Values.FirstOrDefault();
            if (any != null) Bring(any); else ShowStart();
        }
    }

    // ─────────────── 메모리 ───────────────

    static void MemoryTrimLater()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        t.Tick += (_, _) => { t.Stop(); TrimMemory(); };
        t.Start();
    }

    [System.Runtime.InteropServices.DllImport("psapi.dll")]
    static extern bool EmptyWorkingSet(IntPtr hProcess);

    static void TrimMemory()
    {
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        try { EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle); } catch { }
    }

    // ─────────────── 개발용: 창 그림 저장 ───────────────

    /// <summary>--render 종류 출력.png [목록.sbook]  (종류: main · edit · settings · help · start · memo)</summary>
    void RenderMode_(string[] args)
    {
        string kind = args[1], output = args[2];
        string? list = args.Length > 3 ? args[3] : null;
        Window? w = null;
        try
        {
            switch (kind)
            {
                case "main": w = ShortcutBook.MainWindow.TryCreate(list ?? ""); break;
                case "edit":
                    {
                        var item = list != null && File.Exists(list) ? ShortcutImport.FromPath(list) : new ListItem();
                        item.ManualPath = item.ManualPath.Length == 0 ? @"C:\Windows\System32\notepad.exe" : item.ManualPath;
                        item.WebUrl = "https://example.com/manual";
                        item.Memo = "메모 예시\n두 번째 줄";
                        w = new ItemEditWindow(item, isNew: false);
                        break;
                    }
                case "settings": w = new SettingsWindow(); break;
                case "help": w = new HelpWindow(); break;
                case "start": w = new StartWindow(); break;
                case "memo": w = new MemoWindow("메모 예시", "첫 줄\n둘째 줄 — 길게 써도 줄이 바뀌어 보여야 합니다. " + new string('가', 60)); break;
            }
            if (w == null) { Shutdown(2); return; }

            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -20000; w.Top = 0;
            w.ShowInTaskbar = false;
            w.Show();
            w.UpdateLayout();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    w.UpdateLayout();
                    var dpi = VisualTreeHelper.GetDpi(w);
                    int pw = (int)Math.Ceiling(w.ActualWidth * dpi.DpiScaleX), ph = (int)Math.Ceiling(w.ActualHeight * dpi.DpiScaleY);
                    var rtb = new RenderTargetBitmap(pw, ph, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                    rtb.Render((Visual)w.Content);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    using var fs = File.Create(output);
                    enc.Save(fs);
                }
                catch (Exception ex) { Log.Write("render 실패: " + ex); }
                Shutdown();
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            Log.Write("render 실패: " + ex);
            Shutdown(1);
        }
    }
}
