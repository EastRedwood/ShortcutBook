using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace ShortcutBook;

/// <summary>
/// 대상이 웹 주소인 항목의 사이트 아이콘(파비콘)을 해당 사이트에서 직접 받는다. 받은 그림은 80px 이하의 PNG 로 줄여
/// 그 항목(ListItem.WebIcon) 안에 저장하므로 목록 파일(.sbook)과 함께 움직이고, 항목이나 파일을 지우면 같이 사라진다.
/// 제3자 아이콘 서비스는 쓰지 않는다 (어떤 사이트를 적어 뒀는지 밖으로 알리지 않으려고).
/// 인터넷에 연결되지 않거나 아이콘이 없으면 아무것도 돌려주지 않으므로 화면은 기본 웹 아이콘을 쓴다.
/// </summary>
internal static partial class WebIcons
{
    /// <summary>사이트 아이콘 요청이 끝났을 때 (성공이든 실패든, 사이트 호스트 이름). UI 스레드에서 올라온다. 불러오는 중 표시를 거두는 데도 쓴다.</summary>
    public static event Action<string>? Finished;

    /// <summary>이 대상(웹 주소)의 사이트 아이콘을 지금 받아 오는 중인가</summary>
    public static bool IsPending(string? target)
    {
        if (!TryOrigin(target, out var o)) return false;
        lock (Pending) return Pending.Contains(o.Host);
    }

    static readonly HttpClient Http = CreateClient();
    static readonly HashSet<string> Pending = new(StringComparer.OrdinalIgnoreCase);
    static readonly SemaphoreSlim Gate = new(4);

    static HttpClient CreateClient()
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 }) { Timeout = TimeSpan.FromSeconds(6) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("ShortcutBook/1.0");
        return c;
    }

    // 옛 버전(빌드 #35 이전)이 이 PC의 폴더에 따로 저장해 둔 아이콘. 새로 쓰지는 않고, 처음 열 때 항목 안으로 옮겨 담는 데만 읽는다.
    static string LegacyFile(string host) => Path.Combine(AppInfo.DataDir, "webicons", host + ".img");

    // 이번 실행 중에 받았거나 옛 폴더에서 읽은 사이트별 아이콘 (80px 이하 PNG 바이트). 파일로는 저장하지 않는다.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> Memory = new(StringComparer.OrdinalIgnoreCase);
    // 옛 폴더에 파일이 없던 사이트 (줄마다 같은 파일 확인을 되풀이하지 않으려고)
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> NoLegacy = new(StringComparer.OrdinalIgnoreCase);
    const int MemoryLimit = 300; // 상주 프로그램이라 이번 실행 중 모아 두는 양에 상한을 둔다

    /// <summary>이 사이트의 아이콘 바이트 (이번 실행 중 받은 것, 없으면 옛 폴더의 것). 없으면 null.</summary>
    public static byte[]? Lookup(string host)
    {
        if (Memory.TryGetValue(host, out var b)) return b;
        if (NoLegacy.ContainsKey(host)) return null;
        try
        {
            var f = LegacyFile(host);
            if (!File.Exists(f)) { NoLegacy[host] = true; return null; }
            var n = Normalize(File.ReadAllBytes(f));
            if (n != null) Remember(host, n);
            return n;
        }
        catch (Exception ex) { Log.Write("옛 웹 아이콘 읽기 실패 " + host + ": " + ex.Message); return null; }
    }

    static void Remember(string host, byte[] png)
    {
        if (Memory.Count >= MemoryLimit) Memory.Clear(); // 항목에 이미 저장되므로 비워도 다시 받을 일은 없다
        Memory[host] = png;
    }

    /// <summary>이 항목이 지금 대상 사이트의 아이콘을 이미 갖고 있는가</summary>
    public static bool HasStored(ListItem it, string host) =>
        !string.IsNullOrEmpty(it.WebIcon) && string.Equals(it.WebIconHost, host, StringComparison.OrdinalIgnoreCase);

    public static string ToText(byte[] png) => Convert.ToBase64String(png);

    /// <summary>항목에 저장된 아이콘 바이트 (없거나 깨졌으면 null)</summary>
    public static byte[]? StoredBytes(ListItem it)
    {
        if (string.IsNullOrEmpty(it.WebIcon)) return null;
        try { return Convert.FromBase64String(it.WebIcon); } catch { return null; }
    }

    /// <summary>
    /// 항목의 저장된 아이콘을 지금 대상에 맞게 정리한다: 웹 주소가 아니거나 아이콘을 직접 지정했으면 지우고,
    /// 사이트가 바뀌었으면 옛 것을 버리고, 아이콘이 없는데 이미 받아 둔 것(이번 실행 또는 옛 폴더)이 있으면 채운다.
    /// 바뀌었으면 true (목록을 다시 저장해야 한다).
    /// </summary>
    public static bool Adopt(ListItem it)
    {
        if (!string.IsNullOrWhiteSpace(it.IconPath) || !TryOrigin(it.Target, out var o))
        {
            if (it.WebIcon == null && it.WebIconHost == null) return false;
            it.WebIcon = null; it.WebIconHost = null;
            return true;
        }
        if (HasStored(it, o.Host)) return false;
        var b = Lookup(o.Host);
        if (b != null) { it.WebIcon = ToText(b); it.WebIconHost = o.Host; return true; }
        if (it.WebIcon == null && it.WebIconHost == null) return false;
        it.WebIcon = null; it.WebIconHost = null; // 사이트가 바뀐 뒤의 옛 아이콘
        return true;
    }

    /// <summary>대상이 http(s) 웹 주소이면 그 사이트의 기준 주소(origin)를 돌려준다.</summary>
    public static bool TryOrigin(string? target, out Uri origin) => TryTarget(target, out origin, out _);

    /// <summary>origin 은 사이트의 기준 주소, page 는 사용자가 입력한 전체 주소(경로 포함, 조각 제외).</summary>
    public static bool TryTarget(string? target, out Uri origin, out Uri page)
    {
        origin = page = null!;
        var t = Launcher.Expand(target);
        if (t.Length == 0 || !Launcher.IsUrl(t)) return false;
        if (!Uri.TryCreate(Launcher.NormalizeUrl(t), UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
        if (!u.Host.Contains('.')) return false; // 입력 도중의 짧은 이름으로 요청을 보내지 않는다
        origin = new Uri(u.GetLeftPart(UriPartial.Authority));
        page = new Uri(u.GetLeftPart(UriPartial.Query)); // #조각 제외
        return true;
    }

    /// <summary>그림 바이트를 화면용 그림으로 읽는다. 요청 크기 이상 중 가장 작은 그림, 없으면 가장 큰 그림.</summary>
    public static ImageSource? Decode(byte[] bytes, int px)
    {
        using var ms = new MemoryStream(bytes);
        var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (dec.Frames.Count == 0) return null;
        // 요청 크기 이상 중 가장 작은 그림, 없으면 가장 큰 그림
        var frames = dec.Frames.OrderBy(f => f.PixelWidth).ToList();
        var best = frames.FirstOrDefault(f => f.PixelWidth >= px) ?? frames[^1];
        var bs = BitmapFrame.Create(best);
        bs.Freeze();
        return bs;
    }

    /// <summary>
    /// 이미 받아 둔 아이콘이 없으면 사이트에서 한 번 받아 본다 (백그라운드). 수정 창에서 주소를 입력했을 때, 수정 창을 열었을 때, 북마크를 가져올 때만 부른다.
    /// 인터넷에 연결되지 않았거나 아이콘이 없으면 그냥 끝나며, 스스로 다시 시도하지 않는다.
    /// </summary>
    /// <param name="force">이미 받아 둔 아이콘이 있어도 다시 받는다 (수정 창에서 주소를 고쳐 쓸 때, 마지막에 입력한 주소의 결과를 우선하려고)</param>
    public static void Request(Uri origin, Uri? page = null, bool force = false)
    {
        var host = origin.Host;
        if (!force && Lookup(host) != null) return;
        lock (Pending) { if (!Pending.Add(host)) return; }
        _ = Task.Run(() => FetchAsync(origin, page));
    }

    static async Task FetchAsync(Uri origin, Uri? page)
    {
        var host = origin.Host;
        await Gate.WaitAsync();
        try
        {
            // 입력한 주소의 페이지를 먼저, 그다음 사이트 홈페이지에서 아이콘 안내(<link rel="icon">)를 찾고, 마지막에 /favicon.ico 를 본다.
            // (예: 홈페이지는 비어 있고 /a2dp/ 아래 페이지에만 아이콘이 적혀 있는 사이트)
            var candidates = new List<Uri>();
            var pages = new List<Uri>();
            if (page != null && page.AbsolutePath.Length > 1) pages.Add(page);
            pages.Add(origin);
            foreach (var p in pages.DistinctBy(u => u.AbsoluteUri))
            {
                var html = await GetBytesAsync(p, 128 * 1024);
                if (html != null) candidates.AddRange(FindLinks(System.Text.Encoding.UTF8.GetString(html), p));
            }
            candidates.Add(new Uri(origin, "/favicon.ico"));

            foreach (var c in candidates.DistinctBy(u => u.AbsoluteUri))
            {
                var bytes = await GetBytesAsync(c, 1024 * 1024);
                if (bytes == null || bytes.Length < 16) continue;
                var store = ToStorable(bytes);
                if (store == null) continue; // 그림이 아니면(예: 오류 페이지) 건너뛴다
                Remember(host, store); // 목록 파일에는 UI 쪽(Finished 처리)이 항목에 넣어 저장한다
                break;
            }
        }
        catch (Exception ex) { Log.Write("웹 아이콘 받기 실패 " + host + ": " + ex.Message); }
        finally
        {
            Gate.Release();
            lock (Pending) Pending.Remove(host);
        }

        Application.Current?.Dispatcher.BeginInvoke(new Action(() => Finished?.Invoke(host)));
    }

    /// <summary>받은 바이트를 저장할 그림으로 바꾼다 (SVG 는 먼저 PNG 로 그린 뒤, 모두 80px 이하 PNG 로 줄인다). 못 쓰면 null.</summary>
    static byte[]? ToStorable(byte[] bytes)
    {
        try
        {
            var raw = LooksLikeSvg(bytes) ? SvgToPng(bytes) : bytes;
            return raw == null ? null : Normalize(raw);
        }
        catch { return null; }
    }

    const int MaxStoredPx = 80;

    /// <summary>그림(PNG·ICO 등)을 읽어 가장 알맞은 한 장을 80px 이하의 PNG 로 줄인다. 읽을 수 없으면 null.</summary>
    static byte[]? Normalize(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (dec.Frames.Count == 0) return null;
        var frames = dec.Frames.OrderBy(f => f.PixelWidth).ToList();
        var best = BitmapFrame.Create(frames.FirstOrDefault(f => f.PixelWidth >= MaxStoredPx) ?? frames[^1]);
        best.Freeze();
        BitmapSource src = best;
        int m = Math.Max(best.PixelWidth, best.PixelHeight);
        if (m > MaxStoredPx)
        {
            double k = MaxStoredPx / (double)m;
            var tb = new TransformedBitmap(best, new ScaleTransform(k, k));
            tb.Freeze();
            src = tb;
        }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        using var outMs = new MemoryStream();
        enc.Save(outMs);
        return outMs.ToArray();
    }

    static bool LooksLikeSvg(byte[] bytes)
    {
        var head = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 1024));
        return head.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>SVG 를 128px 정사각형 PNG 로 그린다. WPF 가 SVG 를 읽지 못해서 SharpVectors 를 쓰고, 그림 그리기는 UI 스레드에서 한다.</summary>
    static byte[]? SvgToPng(byte[] svg)
    {
        var app = Application.Current;
        if (app == null) return null;
        return app.Dispatcher.Invoke(() =>
        {
            try
            {
                var settings = new WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = true, OptimizePath = true };
                using var ms = new MemoryStream(svg);
                var dg = new FileSvgReader(settings).Read(ms);
                if (dg == null) return null;
                var b = dg.Bounds;
                if (b.IsEmpty || b.Width <= 0 || b.Height <= 0) return null;

                const int N = 128;
                double scale = N / Math.Max(b.Width, b.Height);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // 그림을 칸 가운데에 맞춘다 (먼저 크기를 맞추고 그다음 이동)
                    dc.PushTransform(new TranslateTransform((N - b.Width * scale) / 2 - b.X * scale, (N - b.Height * scale) / 2 - b.Y * scale));
                    dc.PushTransform(new ScaleTransform(scale, scale));
                    dc.DrawDrawing(dg);
                }
                var rtb = new RenderTargetBitmap(N, N, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var outMs = new MemoryStream();
                enc.Save(outMs);
                return outMs.ToArray();
            }
            catch (Exception ex)
            {
                Log.Write("SVG 아이콘 변환 실패: " + ex.Message);
                return null;
            }
        });
    }

    /// <summary>응답을 최대 limit 바이트까지 읽는다. 연결에 실패하면 null.</summary>
    static async Task<byte[]?> GetBytesAsync(Uri uri, int limit)
    {
        try
        {
            using var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return null;
            await using var s = await resp.Content.ReadAsStreamAsync();
            using var ms = new MemoryStream();
            var buf = new byte[16 * 1024];
            int n;
            while (ms.Length < limit && (n = await s.ReadAsync(buf)) > 0) ms.Write(buf, 0, n);
            return ms.ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();
    [GeneratedRegex(@"(\w[\w-]*)\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase)]
    private static partial Regex Attr();

    /// <summary>페이지의 &lt;link rel="icon"&gt; 들을 후보로 모은다 (큰 아이콘 먼저). 상대 주소는 그 페이지 기준으로 푼다.</summary>
    static IEnumerable<Uri> FindLinks(string html, Uri origin)
    {
        var found = new List<(Uri Url, int Size, int Rank)>();
        foreach (Match m in LinkTag().Matches(html))
        {
            string? rel = null, href = null, sizes = null;
            foreach (Match a in Attr().Matches(m.Value))
            {
                var v = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Value;
                switch (a.Groups[1].Value.ToLowerInvariant())
                {
                    case "rel": rel = v.ToLowerInvariant(); break;
                    case "href": href = System.Net.WebUtility.HtmlDecode(v); break;
                    case "sizes": sizes = v; break;
                }
            }
            if (rel == null || href == null || !rel.Contains("icon")) continue;
            if (href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(origin, href, out var url) || (url.Scheme != "http" && url.Scheme != "https")) continue;
            int size = 0;
            if (sizes != null) int.TryParse(sizes.Split('x', 'X')[0], out size);
            // 순서: 일반 래스터 아이콘 > SVG > 애플 터치 아이콘 (SVG 는 변환이 실패할 수 있어 래스터 뒤)
            int rank = rel.Contains("apple-touch") ? 2 : url.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            found.Add((url, size, rank));
        }
        // 같은 순위·크기라면 나중에 적힌 것을 먼저 (일반 아이콘을 앞에, 그 사이트 고유 아이콘을 뒤에 적는 사이트가 많아서)
        return found.Select((f, i) => (f, i)).OrderBy(x => x.f.Rank).ThenByDescending(x => x.f.Size).ThenByDescending(x => x.i).Select(x => x.f.Url);
    }
}
