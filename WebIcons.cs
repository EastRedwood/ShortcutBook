using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace ShortcutBook;

/// <summary>
/// 대상이 웹 주소인 항목의 사이트 아이콘(파비콘)을 해당 사이트에서 직접 받아 이 PC에 저장해 두고 쓴다.
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

    static string Dir => Path.Combine(AppInfo.DataDir, "webicons");
    static string FilePath(string host) => Path.Combine(Dir, host + ".img");

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

    /// <summary>저장해 둔 아이콘을 읽는다 (없으면 null). 요청 크기에 가장 알맞은 그림을 고른다.</summary>
    public static ImageSource? Load(string host, int px)
    {
        try
        {
            var f = FilePath(host);
            if (!File.Exists(f)) return null;
            return Decode(File.ReadAllBytes(f), px);
        }
        catch (Exception ex)
        {
            Log.Write("웹 아이콘 읽기 실패 " + host + ": " + ex.Message);
            return null;
        }
    }

    static ImageSource? Decode(byte[] bytes, int px)
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
    /// 저장된 아이콘이 없으면 사이트에서 한 번 받아 본다 (백그라운드). 수정 창에서 주소를 입력했을 때와 수정 창을 열었을 때만 부른다.
    /// 인터넷에 연결되지 않았거나 아이콘이 없으면 그냥 끝나며, 스스로 다시 시도하지 않는다.
    /// </summary>
    /// <param name="force">이미 저장된 아이콘이 있어도 다시 받아 덮어쓴다 (수정 창에서 주소를 고쳐 쓸 때, 마지막에 입력한 주소의 결과를 우선하려고)</param>
    public static void Request(Uri origin, Uri? page = null, bool force = false)
    {
        var host = origin.Host;
        try { if (!force && File.Exists(FilePath(host))) return; }
        catch { return; }
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
                Directory.CreateDirectory(Dir);
                File.WriteAllBytes(FilePath(host), store);
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

    /// <summary>받은 바이트를 저장할 그림으로 바꾼다. 래스터(PNG/ICO/…)는 읽을 수 있으면 그대로, SVG 는 PNG 로 그려서. 못 쓰면 null.</summary>
    static byte[]? ToStorable(byte[] bytes)
    {
        try
        {
            if (LooksLikeSvg(bytes)) return SvgToPng(bytes);
            return Decode(bytes, 32) == null ? null : bytes;
        }
        catch { return null; }
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

    /// <summary>
    /// 지워진(또는 대상이 바뀐) 바로가기의 사이트 아이콘을 지운다. 어느 목록에서든 같은 사이트를 쓰는 항목이 남아 있으면 지우지 않는다.
    /// 반드시 목록 파일을 저장한 뒤에 부른다 (저장된 목록들을 읽어 판단하므로).
    /// </summary>
    public static void Cleanup(IEnumerable<string> hosts)
    {
        var hs = hosts.Distinct(StringComparer.OrdinalIgnoreCase).Where(h => File.Exists(FilePath(h))).ToList();
        if (hs.Count == 0) return;

        // 확인할 목록: 열려 있는 목록 + 최근 목록 + 프로그램 폴더의 Lists. (UI 스레드에서 모아 둔다)
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in App.Instance.OpenListPaths()) paths.Add(p);
        foreach (var p in App.Settings.Recent) paths.Add(p);
        try { if (Directory.Exists(AppInfo.ListsDir)) foreach (var p in Directory.GetFiles(AppInfo.ListsDir, "*" + AppInfo.Extension)) paths.Add(p); }
        catch { }

        _ = Task.Run(() =>
        {
            try
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in paths)
                {
                    try
                    {
                        if (!File.Exists(p)) continue;
                        foreach (var it in ListDocument.Load(p).Items)
                            if (TryOrigin(it.Target, out var o)) used.Add(o.Host);
                    }
                    catch (Exception ex) { Log.Write("웹 아이콘 정리 중 목록 읽기 실패 " + p + ": " + ex.Message); return; } // 못 읽은 목록이 있으면 아무것도 지우지 않는다
                }
                foreach (var h in hs)
                    if (!used.Contains(h)) try { File.Delete(FilePath(h)); } catch { }
            }
            catch (Exception ex) { Log.Write("웹 아이콘 정리 실패: " + ex.Message); }
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
