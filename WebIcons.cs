using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShortcutBook;

/// <summary>
/// 대상이 웹 주소인 항목의 사이트 아이콘(파비콘)을 해당 사이트에서 직접 받아 이 PC에 저장해 두고 쓴다.
/// 제3자 아이콘 서비스는 쓰지 않는다 (어떤 사이트를 적어 뒀는지 밖으로 알리지 않으려고).
/// 인터넷에 연결되지 않거나 아이콘이 없으면 아무것도 돌려주지 않으므로 화면은 기본 웹 아이콘을 쓴다.
/// </summary>
internal static partial class WebIcons
{
    /// <summary>아이콘을 새로 받았을 때 (사이트 호스트 이름). UI 스레드에서 올라온다.</summary>
    public static event Action<string>? Fetched;

    static readonly HttpClient Http = CreateClient();
    static readonly HashSet<string> Pending = new(StringComparer.OrdinalIgnoreCase);
    // 인터넷에 닿지 않았던 사이트: 잠시 뒤(오프라인이 풀렸을 때) 다시 시도할 수 있게 시각을 둔다
    static readonly Dictionary<string, DateTime> Offline = new(StringComparer.OrdinalIgnoreCase);
    static readonly TimeSpan OfflineRetry = TimeSpan.FromMinutes(5);
    static readonly SemaphoreSlim Gate = new(4);
    static readonly TimeSpan NoneRetry = TimeSpan.FromDays(7);

    static HttpClient CreateClient()
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 }) { Timeout = TimeSpan.FromSeconds(6) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("ShortcutBook/1.0");
        return c;
    }

    static string Dir => Path.Combine(AppInfo.DataDir, "webicons");
    static string FilePath(string host) => Path.Combine(Dir, host + ".img");
    static string NonePath(string host) => Path.Combine(Dir, host + ".none");

    /// <summary>대상이 http(s) 웹 주소이면 그 사이트의 기준 주소(origin)를 돌려준다.</summary>
    public static bool TryOrigin(string? target, out Uri origin)
    {
        origin = null!;
        var t = Launcher.Expand(target);
        if (t.Length == 0 || !Launcher.IsUrl(t)) return false;
        if (!Uri.TryCreate(Launcher.NormalizeUrl(t), UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
        if (!u.Host.Contains('.')) return false; // 입력 도중의 짧은 이름으로 요청을 보내지 않는다
        origin = new Uri(u.GetLeftPart(UriPartial.Authority));
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

    /// <summary>저장된 아이콘이 없고 최근에 실패하지 않았다면 사이트에서 받아 온다 (백그라운드).</summary>
    public static void Request(Uri origin)
    {
        var host = origin.Host;
        try
        {
            if (File.Exists(FilePath(host))) return;
            var none = NonePath(host);
            if (File.Exists(none) && DateTime.UtcNow - File.GetLastWriteTimeUtc(none) < NoneRetry) return;
        }
        catch { return; }

        lock (Pending)
        {
            if (Offline.TryGetValue(host, out var when) && DateTime.UtcNow - when < OfflineRetry) return;
            if (!Pending.Add(host)) return;
        }
        _ = Task.Run(() => FetchAsync(origin));
    }

    static async Task FetchAsync(Uri origin)
    {
        var host = origin.Host;
        bool reached = false, saved = false;
        await Gate.WaitAsync();
        try
        {
            var candidates = new List<Uri>();
            var html = await GetBytesAsync(origin, 128 * 1024, () => reached = true);
            if (html != null) candidates.AddRange(FindLinks(System.Text.Encoding.UTF8.GetString(html), origin));
            candidates.Add(new Uri(origin, "/favicon.ico"));

            foreach (var c in candidates.DistinctBy(u => u.AbsoluteUri))
            {
                var bytes = await GetBytesAsync(c, 1024 * 1024, () => reached = true);
                if (bytes == null || bytes.Length < 16) continue;
                try { if (Decode(bytes, 32) == null) continue; } catch { continue; } // 그림이 아니면(예: 오류 페이지) 건너뛴다
                Directory.CreateDirectory(Dir);
                File.WriteAllBytes(FilePath(host), bytes);
                saved = true;
                break;
            }

            if (!saved && reached)
            {
                // 사이트에는 닿았는데 아이콘이 없다: 한동안 다시 묻지 않는다
                Directory.CreateDirectory(Dir);
                File.WriteAllText(NonePath(host), "");
            }
        }
        catch (Exception ex) { Log.Write("웹 아이콘 받기 실패 " + host + ": " + ex.Message); }
        finally
        {
            Gate.Release();
            lock (Pending)
            {
                Pending.Remove(host);
                if (!saved && !reached) Offline[host] = DateTime.UtcNow; // 인터넷에 연결되지 않음: 5분 동안은 다시 시도하지 않는다
                else Offline.Remove(host);
            }
        }

        if (saved) Application.Current?.Dispatcher.BeginInvoke(new Action(() => Fetched?.Invoke(host)));
    }

    /// <summary>응답을 최대 limit 바이트까지 읽는다. 연결에 실패하면 null. 서버가 응답했다면 onReached 를 부른다.</summary>
    static async Task<byte[]?> GetBytesAsync(Uri uri, int limit, Action onReached)
    {
        try
        {
            using var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            onReached();
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

    /// <summary>홈페이지의 &lt;link rel="icon"&gt; 들을 후보로 모은다 (큰 아이콘 먼저, svg 는 읽을 수 없어 제외).</summary>
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
            if (href.Contains(".svg", StringComparison.OrdinalIgnoreCase) || href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(origin, href, out var url) || (url.Scheme != "http" && url.Scheme != "https")) continue;
            int size = 0;
            if (sizes != null) int.TryParse(sizes.Split('x', 'X')[0], out size);
            found.Add((url, size, rel.Contains("apple-touch") ? 1 : 0)); // 일반 아이콘을 먼저, 애플 터치 아이콘은 뒤에
        }
        return found.OrderBy(f => f.Rank).ThenByDescending(f => f.Size).Select(f => f.Url);
    }
}
