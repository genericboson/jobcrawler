using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using JobCrawler.Models;

namespace JobCrawler.Crawling;

/// <summary>
/// 게임잡(gamejob.co.kr) 채용공고 목록 크롤러.
///
/// 목록 페이지의 검색/페이지 이동은 /Recruit/_GI_Job_List/ 로 보내는 POST 로 처리된다.
/// 검색 조건을 매 요청 본문에 실어 보내는 방식이라 이 엔드포인트를 그대로 쓴다.
/// (같은 경로에 GET 으로 Page 만 붙이면 직무 필터가 풀린 전체 목록이 돌아온다.)
/// </summary>
public sealed class GameJobCrawler : IDisposable
{
    private const string BaseUrl = "https://www.gamejob.co.kr";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private static readonly Regex GiNoPattern = new(@"GI_No=(\d+)", RegexOptions.Compiled);
    private static readonly Regex GaArgPattern =
        new(@"IsNullOrWhiteSpace\('([^']*)'\)", RegexOptions.Compiled);

    private readonly int _delayMs;
    private readonly int _maxPages;
    private readonly int _pageSize;
    private HttpClient? _client;
    private HttpClientHandler? _handler;

    public GameJobCrawler(int maxPages, int requestDelayMs, int pageSize)
    {
        _maxPages = Math.Max(1, maxPages);
        _delayMs = Math.Max(0, requestDelayMs);
        _pageSize = Math.Clamp(pageSize, 20, 100);
    }

    /// <summary>지정한 직무 코드들의 공고를 공고번호 기준으로 중복 제거해 돌려준다.</summary>
    public async Task<List<JobPosting>> CrawlAsync(IEnumerable<int> dutyCodes, CancellationToken ct = default)
    {
        var codes = dutyCodes.Distinct().ToList();
        if (codes.Count == 0) return new List<JobPosting>();

        ResetSession();

        // 목록 페이지를 한 번 열어 쿠키를 받아둔다. 이후 요청의 Referer 로도 쓴다.
        var referer = $"{BaseUrl}/Recruit/joblist?menucode=duty&duty={codes[0]}";
        await GetStringAsync(referer, BaseUrl, ct);

        var results = new Dictionary<string, JobPosting>();
        var lastPage = 1;

        for (var page = 1; page <= _maxPages; page++)
        {
            if (page > 1 && _delayMs > 0) await Task.Delay(_delayMs, ct);

            var html = await PostListAsync(codes, page, referer, ct);
            var parsed = Parse(html);
            if (parsed.Count == 0) break;

            AddAll(results, parsed, out var added);
            Console.WriteLine($"  {page}페이지: {parsed.Count}건 (신규 {added}건)");

            // 페이지 번호가 10개씩 끊겨 나오므로 매 페이지마다 상한을 다시 읽는다.
            lastPage = Math.Max(lastPage, ParseLastPage(html));
            if (page >= lastPage) break;
        }

        return results.Values.ToList();
    }

    /// <summary>
    /// 목록 페이지가 실제로 쓰는 POST 요청을 그대로 흉내낸다.
    /// jQuery 가 중첩 객체를 condition[duty][] 형태로 직렬화하므로 같은 이름을 쓴다.
    /// </summary>
    private async Task<string> PostListAsync(
        List<int> dutyCodes, int page, string referer, CancellationToken ct)
    {
        var fields = new List<KeyValuePair<string, string>>();
        foreach (var code in dutyCodes)
            fields.Add(new("condition[duty][]", code.ToString()));

        fields.Add(new("page", page.ToString()));
        fields.Add(new("direct", "0"));      // 즉시지원만 보기 끔
        fields.Add(new("order", "1"));       // 추천순
        fields.Add(new("pagesize", _pageSize.ToString()));
        fields.Add(new("tabcode", "1"));     // 전체 채용정보 탭

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/Recruit/_GI_Job_List/")
        {
            Content = new FormUrlEncodedContent(fields),
        };
        request.Headers.TryAddWithoutValidation("Referer", referer);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

        using var response = await _client!.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void AddAll(
        Dictionary<string, JobPosting> target, List<JobPosting> source, out int added)
    {
        added = 0;
        foreach (var job in source)
            if (target.TryAdd(job.Id, job)) added++;
    }

    /// <summary>
    /// 페이지네이션 영역에서 마지막 페이지 번호를 읽는다.
    /// 페이지 번호를 못 찾으면 1(= 더 볼 페이지 없음)을 돌려준다.
    /// </summary>
    public static int ParseLastPage(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var pagination = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'pagination')]");
        if (pagination is null) return 1;

        var last = 1;

        // 다른 페이지로 가는 링크
        var links = pagination.SelectNodes(".//a[@data-page]");
        if (links is not null)
            foreach (var link in links)
                if (int.TryParse(link.GetAttributeValue("data-page", ""), out var n))
                    last = Math.Max(last, n);

        // 현재 페이지는 링크가 아니라 span 으로 찍힌다.
        var current = pagination.SelectSingleNode(".//span[contains(@class,'now')]");
        if (current is not null && int.TryParse(current.InnerText.Trim(), out var cur))
            last = Math.Max(last, cur);

        return last;
    }

    /// <summary>목록 HTML 한 페이지에서 공고들을 추출한다.</summary>
    public static List<JobPosting> Parse(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var anchors = doc.DocumentNode.SelectNodes(
            "//div[contains(@class,'tit')]/a[contains(@href,'GI_Read/View')]");
        if (anchors is null) return new List<JobPosting>();

        var jobs = new List<JobPosting>();
        var seen = new HashSet<string>();

        foreach (var anchor in anchors)
        {
            var href = anchor.GetAttributeValue("href", "");
            var idMatch = GiNoPattern.Match(href);
            if (!idMatch.Success) continue;

            var id = idMatch.Groups[1].Value;
            if (!seen.Add(id)) continue;

            var titleNode = anchor.SelectSingleNode(".//strong") ?? anchor;
            var row = anchor.Ancestors("tr").FirstOrDefault();
            var titleBox = anchor.ParentNode;

            var job = new JobPosting
            {
                Id = id,
                Title = Text(titleNode),
                Url = Absolute(href),
                Company = Text(row?.SelectSingleNode(".//div[contains(@class,'company')]//strong")),
                CompanyUrl = Absolute(
                    row?.SelectSingleNode(".//div[contains(@class,'company')]//a")
                        ?.GetAttributeValue("href", "") ?? ""),
                Deadline = Text(row?.SelectSingleNode(
                    ".//span[contains(@class,'date') and not(contains(@class,'modifyDate'))]")),
                Registered = Text(row?.SelectSingleNode(".//span[contains(@class,'modifyDate')]")),
                Duty = ExtractDuty(anchor.GetAttributeValue("onclick", "")),
            };

            FillInfoSpans(job, titleBox?.SelectNodes(".//p[contains(@class,'info')]/span"));

            if (string.IsNullOrWhiteSpace(job.Title)) continue;
            jobs.Add(job);
        }

        return jobs;
    }

    /// <summary>
    /// 목록의 메타 정보는 p.info 안의 span 순서로 들어온다.
    /// (경력, 학력, 지역, 게임유형, 고용형태) 순서지만 항목이 빠질 수 있어 내용으로 판별한다.
    /// </summary>
    private static void FillInfoSpans(JobPosting job, HtmlNodeCollection? spans)
    {
        if (spans is null) return;

        var rest = new List<string>();
        foreach (var span in spans)
        {
            var text = Text(span);
            if (text.Length == 0) continue;

            if (job.Career.Length == 0 && (text.Contains("경력") || text.Contains("신입")))
                job.Career = text;
            else if (job.Education.Length == 0 && text.Contains("학력"))
                job.Education = text;
            else if (job.Location.Length == 0 && text.Contains('>'))
                job.Location = text;
            else
                rest.Add(text);
        }

        if (rest.Count > 0) job.GameType = rest[0];
        if (rest.Count > 1) job.EmploymentType = rest[1];
    }

    /// <summary>
    /// 목록 링크의 onclick 에 심긴 GA 파라미터에서 직무 태그를 꺼낸다.
    /// 첫 번째 IsNullOrWhiteSpace('...') 값이 직무 태그다.
    /// </summary>
    private static string ExtractDuty(string onclick)
    {
        if (string.IsNullOrEmpty(onclick)) return "";
        var m = GaArgPattern.Match(onclick);
        if (!m.Success) return "";
        var duty = HtmlEntity.DeEntitize(m.Groups[1].Value).Trim();
        return duty == "없음" ? "" : duty;
    }

    private static string Text(HtmlNode? node) =>
        node is null ? "" : HtmlEntity.DeEntitize(node.InnerText).Replace(' ', ' ').Trim();

    private static string Absolute(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return "";
        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return href;
        return BaseUrl + (href.StartsWith('/') ? href : "/" + href);
    }

    private void ResetSession()
    {
        _client?.Dispose();
        _handler?.Dispose();

        _handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        _client = new HttpClient(_handler) { Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        _client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ko-KR,ko;q=0.9");
    }

    private async Task<string> GetStringAsync(string url, string referer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(referer))
            request.Headers.TryAddWithoutValidation("Referer", referer);

        using var response = await _client!.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        // 게임잡은 응답 헤더에 charset 을 주지 않는 경우가 있으나 본문은 UTF-8 이다.
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return Encoding.UTF8.GetString(bytes);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _handler?.Dispose();
    }
}
