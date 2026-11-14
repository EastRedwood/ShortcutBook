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
        Task.Run(WarmUpText); // 글꼴·글자 그리기 초기화를 UI 스레드와 나란히 해 둔다
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
        if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(args[0]))
        {
            // 창을 만들고 그리는 동안 목록의 아이콘을 다른 스레드에서 미리 읽어 둔다 (시작 시간 단축)
            var first = args[0];
            Task.Run(() => PrefetchIcons(first));
        }
        Loc.Instance.Load(Settings.Language);
        ThemeService.Apply();

        // 개발용: 창을 그림 파일로 저장하고 끝낸다 (입력 장치 없이 화면을 확인하는 용도)
        if (args.Length >= 3 && args[0] == "--render")
        {
            ApplyViewSize();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => RenderMode_(args)));
            return;
        }

        // 탐색기 "새로 만들기 > 바로가기 목록 바로가기" 가 호출한다: 목록 파일은 프로그램 폴더의 Lists 에, 이 위치에는 바로가기(.lnk)를 만든다.
        if (args.Length >= 2 && args[0] == "--new-link")
        {
            ApplyViewSize();
            NewLinkFlow.Run(args[1], args.Length >= 4 && args[2] == "--name" ? args[3] : null);
            Shutdown();
            return;
        }

        // 빌드 뒤 자동 실행용: 탐색기 연결(.sbook)을 이 실행 파일로 갱신하고 끝낸다 (창·단일 인스턴스 없음).
        if (args.Length > 0 && args[0] == "--register")
        {
            try
            {
                if (Settings.RegisterShell && Environment.ProcessPath is { Length: > 0 } me) { ShellRegistration.Register(me); Log.Write("--register: " + me); }
            }
            catch (Exception ex) { Log.Write("--register 실패: " + ex.Message); }
            Shutdown();
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
        r["ExtraWidth"] = s.btn * 3 + 6; // 매뉴얼·웹·메모 단추 자리(항상 확보): 줄마다 이름/설명 열이 같은 위치에 정렬되게
        r["RowPadding"] = s.pad;
        ApplyNameWidth();
        // 처음 적용할 때는 비우지 않는다 (시작 때 미리 읽어 둔 아이콘을 쓰려고). 크기를 바꿨을 때만 옛 크기의 그림을 버린다.
        if (_appliedViewSize >= 0 && _appliedViewSize != Settings.ViewSize) IconService.Clear();
        _appliedViewSize = Settings.ViewSize;
        ViewChanged?.Invoke();
    }

    static int _appliedViewSize = -1;

    /// <summary>처음 글자를 재거나 그릴 때 드는 글꼴 시스템 초기화(DirectWrite 등)를 다른 스레드에서 미리 한다.</summary>
    static void WarmUpText()
    {
        try
        {
            foreach (var name in new[] { "Segoe UI", "Malgun Gothic", "Segoe MDL2 Assets" })
            {
                var ft = new FormattedText("가Aa", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(name), 13, Brushes.Black, 1.0);
                _ = ft.Width;
            }
        }
        catch { }
    }

    static void PrefetchIcons(string listPath)
    {
        try
        {
            listPath = Path.GetFullPath(listPath);
            if (!listPath.EndsWith(AppInfo.Extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(listPath)) return;
            var doc = ListDocument.Load(listPath);
            IconService.UseSystemScale();
            int size = IconLogicalSize;
            foreach (var it in doc.Items.Take(40)) IconService.Get(it, size);
        }
        catch (Exception ex) { Log.Write("아이콘 미리 읽기 실패: " + ex.Message); }
    }

    /// <summary>아이콘 캐시를 비우고 모든 창의 아이콘을 다시 읽게 한다 (웹 아이콘 설정을 바꿨을 때).</summary>
    public static void RefreshIcons()
    {
        IconService.Clear();
        ViewChanged?.Invoke();
    }

    public static void ApplyNameWidth()
    {
        Current.Resources["NameColWidth"] = new GridLength(Math.Clamp(Settings.NameWidth, 100, 700));
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
            if (a.StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(a)) continue;
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
        w.Closed += (_, _) => { _lists.Remove(w.ListPath); MemoryTrimLater(); };
        w.Show();
        Bring(w);
        if (_start != null) { _start.Close(); }
        // 최근 목록 저장(파일 쓰기)은 창이 뜬 뒤에 한다
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { Settings.AddRecent(path); Settings.Save(); }));
    }

    /// <summary>
    /// 열려 있는 목록 안에서 다른 목록을 열 때. 설정이 "현재 창 대체"(기본)이면 그 창에서 목록을 바꾸고
    /// (뒤로/앞으로 기록을 남긴다), "새 창"이면 새로 연다.
    /// </summary>
    public void OpenListFrom(MainWindow? from, string path)
    {
        if (from == null || Settings.OpenInNewWindow) { OpenList(path); return; }
        try { path = Path.GetFullPath(path); } catch { }
        if (!File.Exists(path)) { OpenList(path); return; }
        if (_lists.TryGetValue(path, out var existing)) { Bring(existing); return; }
        from.OpenInPlace(path);
    }

    /// <summary>이미 그 목록을 열고 있는 다른 창이 있으면 돌려준다.</summary>
    public MainWindow? FindWindow(string path, MainWindow? except = null)
    {
        try { path = Path.GetFullPath(path); } catch { }
        return _lists.TryGetValue(path, out var w) && w != except ? w : null;
    }

    public void Activate(Window w) => Bring(w);

    /// <summary>창이 다른 목록으로 바뀌었을 때 경로 목록을 맞춘다.</summary>
    public void Rekey(string oldPath, string newPath, MainWindow w)
    {
        _lists.Remove(oldPath);
        _lists[newPath] = w;
        Settings.AddRecent(newPath);
        Settings.Save();
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
            if (a.StartsWith("--", StringComparison.Ordinal)) continue;
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
            if (l.StartsWith("OPEN|", StringComparison.Ordinal)) { OpenList(l[5..]); opened = true; }
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

    /// <summary>제목 표시줄까지 포함한 창 전체를 그림 파일로 저장한다 (이 프로세스 자신의 창만, 화면 캡처 아님).</summary>
    static bool PrintFullWindow(Window w, string output)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var r)) return false;
        int pw = r.Right - r.Left, ph = r.Bottom - r.Top;
        if (pw <= 0 || ph <= 0) return false;
        var screen = Native.GetDC(IntPtr.Zero);
        var mem = Native.CreateCompatibleDC(screen);
        var bmp = Native.CreateCompatibleBitmap(screen, pw, ph);
        var old = Native.SelectObject(mem, bmp);
        try
        {
            if (!Native.PrintWindow(hwnd, mem, 2 /* PW_RENDERFULLCONTENT */)) return false;
            Native.SelectObject(mem, old);
            var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var fs = File.Create(output);
            enc.Save(fs);
            Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, out int d, sizeof(int));
            Log.Write($"render {output}: 제목 표시줄 다크 속성={d}");
            return true;
        }
        finally
        {
            Native.DeleteObject(bmp); Native.DeleteDC(mem); Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>--render 종류 출력.png [목록.sbook]  (종류: message · main · edit · settings · help · start · memo)</summary>
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
                case "apppicker": w = new AppPickerWindow(); break;
                case "message": w = new MessageWindow("'테스트' 항목을 목록에서 삭제하시겠습니까?\n(실제 프로그램이나 파일은 지워지지 않습니다.)", AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No); break;
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
                    PrintFullWindow(w, output + ".frame.png"); // 제목 표시줄 확인용 (오프스크린이라 본문은 비어 보일 수 있음)
                    var dpi = VisualTreeHelper.GetDpi(w);
                    int pw = (int)Math.Ceiling(w.ActualWidth * dpi.DpiScaleX), ph = (int)Math.Ceiling(w.ActualHeight * dpi.DpiScaleY);
                    var rtb = new RenderTargetBitmap(pw, ph, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                    // 창 배경(다크 테마 색 등)까지 포함해 그린다
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        dc.DrawRectangle(w.Background, null, new Rect(0, 0, w.ActualWidth, w.ActualHeight));
                        dc.DrawRectangle(new VisualBrush((Visual)w.Content), null, new Rect(0, 0, w.ActualWidth, w.ActualHeight));
                    }
                    rtb.Render(dv);
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
