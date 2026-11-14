using System.Runtime.InteropServices;
using System.Windows.Media;

namespace ShortcutBook;

/// <summary>설치된 앱 한 개. Id 는 AppUserModelID(Windows 스토어 앱) 또는 시작 메뉴 항목 ID.</summary>
public sealed record InstalledApp(string Name, string Id)
{
    /// <summary>실행할 대상 문자열. 탐색기와 같은 "shell:AppsFolder\ID" 형식이다.</summary>
    public string Target => Launcher.AppsFolderPrefix + Id;

    /// <summary>"Publisher.App_xxxx!App" 꼴이면 스토어 앱, 아니면 일반 데스크톱 프로그램.</summary>
    public bool IsStoreApp => Id.Contains('!');

    public string KindLabel => Loc.T(IsStoreApp ? "app_kind_store" : "app_kind_program");

    internal string NameLower { get; } = Name.ToLowerInvariant();
    internal string IdLower { get; } = Id.ToLowerInvariant();
}

/// <summary>
/// 시작 메뉴(shell:AppsFolder)에 있는 앱 목록. 스토어 앱은 실행 파일 경로를 찾기 어려워서
/// 이 목록에서 골라 바로가기를 만든다. 목록 읽기에 0.5초쯤 걸려 한 번만 백그라운드에서 읽고 보관한다.
/// </summary>
internal static class AppCatalog
{
    static Task<IReadOnlyList<InstalledApp>>? _loading;

    public static Task<IReadOnlyList<InstalledApp>> LoadAsync() => _loading ??= StartLoad();

    static Task<IReadOnlyList<InstalledApp>> StartLoad()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<InstalledApp>>();
        // Shell.Application 은 STA 에서만 안전하다. 스레드 풀(MTA) 대신 전용 STA 스레드를 쓴다.
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(Enumerate()); }
            catch (Exception ex)
            {
                Log.Write("앱 목록 읽기 실패: " + ex);
                tcs.SetResult(Array.Empty<InstalledApp>());
            }
        })
        { IsBackground = true, Name = "ShortcutBook.AppCatalog" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    static IReadOnlyList<InstalledApp> Enumerate()
    {
        var result = new List<InstalledApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        dynamic? shell = null, folder = null, items = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            folder = shell.NameSpace(Launcher.AppsFolderPrefix.TrimEnd('\\'));
            items = folder.Items();

            int count = items.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic item = items.Item(i);
                try
                {
                    string name = item.Name ?? "";
                    string id = item.Path ?? "";
                    if (name.Length == 0 || id.Length == 0) continue;
                    // 제어판 항목(::{GUID})과 웹 링크는 앱이 아니다.
                    if (id.StartsWith("::", StringComparison.Ordinal) || id.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                    if (seen.Add(id)) result.Add(new InstalledApp(name, id));
                }
                finally { Marshal.FinalReleaseComObject(item); }
            }
        }
        finally
        {
            if (items != null) Marshal.FinalReleaseComObject(items);
            if (folder != null) Marshal.FinalReleaseComObject(folder);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }

    /// <summary>공백으로 나눈 낱말이 모두 들어 있는 앱을 점수순으로 (이름이 그 글자로 시작 &gt; 단어가 시작 &gt; 이름에 포함 &gt; 앱 ID 에만 포함). 검색어가 없으면 전부.</summary>
    public static List<InstalledApp> Search(IReadOnlyList<InstalledApp> all, string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant()).ToArray();
        if (tokens.Length == 0) return all.ToList();

        var scored = new List<(InstalledApp App, int Score)>();
        foreach (var app in all)
        {
            int total = 0;
            bool matched = true;
            foreach (var token in tokens)
            {
                int s = ScoreToken(app, token);
                if (s < 0) { matched = false; break; }
                total += s;
            }
            if (matched) scored.Add((app, total));
        }
        return scored.OrderBy(x => x.Score).ThenBy(x => x.App.Name.Length)
            .ThenBy(x => x.App.Name, StringComparer.CurrentCultureIgnoreCase).Select(x => x.App).ToList();
    }

    static int ScoreToken(InstalledApp app, string token)
    {
        var name = app.NameLower;
        if (name.StartsWith(token, StringComparison.Ordinal)) return 0;
        if (WordStartsWith(name, token)) return 1;
        if (name.Contains(token, StringComparison.Ordinal)) return 2;
        if (app.IdLower.Contains(token, StringComparison.Ordinal)) return 3;
        return -1;
    }

    static bool WordStartsWith(string name, string token)
    {
        for (int i = 1; i < name.Length; i++)
            if (!char.IsLetterOrDigit(name[i - 1]) && string.CompareOrdinal(name, i, token, 0, token.Length) == 0) return true;
        return false;
    }
}

/// <summary>선택 창의 한 줄. 아이콘은 줄이 화면에 올라올 때 처음 읽는다 (아이콘 캐시를 거친다).</summary>
internal sealed class AppRow
{
    public AppRow(InstalledApp app) { App = app; }
    public InstalledApp App { get; }
    public string Name => App.Name;
    public string KindLabel => App.KindLabel;

    ImageSource? _icon;
    bool _loaded;
    public ImageSource? Icon
    {
        get
        {
            if (!_loaded) { _loaded = true; _icon = IconService.Get(new ListItem { Target = App.Target }, 32, cache: false); }
            return _icon;
        }
    }
}
