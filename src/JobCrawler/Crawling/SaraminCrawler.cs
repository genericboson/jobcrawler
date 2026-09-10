using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using JobCrawler.Models;

namespace JobCrawler.Crawling;

/// <summary>
/// 사람인(saramin.co.kr) 키워드 검색 크롤러.
///
/// 검색 결과는 평범한 서버 렌더링 HTML 이라 목록 페이지를 그대로 읽는다.
/// 페이지는 recruitPage 파라미터로 넘기고 한 페이지에 40건이 온다.
/// </summary>
public sealed class SaraminCrawler : IJobSource, IDisposable
{
    private const string BaseUrl = "https://www.saramin.co.kr";
    private const int PageSize = 40;

    public string SourceId => "saramin";
    public string SourceName => "사람인";

    private readonly SaraminSettings _settings;
    private readonly int _delayMs;
    private readonly HttpClient _client;
    private readonly HttpClientHandler _handler;

    public SaraminCrawler(SaraminSettings settings, int requestDelayMs)
    {
        _settings = settings;
        _delayMs = Math.Max(0, requestDelayMs);
        _client = CrawlerHttp.Create(out _handler);
    }

    public async Task<List<JobPosting>> CrawlAsync(CancellationToken ct)
    {
        var results = new Dictionary<string, JobPosting>();

        foreach (var keyword in _settings.Keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword)) continue;

            var found = await SearchAsync(keyword, results, ct);
            Console.WriteLine($"  '{keyword}': {found}건");
        }

        return results.Values.ToList();
    }

    private async Task<int> SearchAsync(
        string keyword, Dictionary<string, JobPosting> results, CancellationToken ct)
    {
        var before = results.Count;

        for (var page = 1; page <= _settings.MaxPages; page++)
        {
            if (page > 1 && _delayMs > 0) await Task.Delay(_delayMs, ct);

            var url = $"{BaseUrl}/zf_user/search/recruit" +
                      $"?searchType=search&searchword={HttpUtility.UrlEncode(keyword, Encoding.UTF8)}" +
                      $"&recruitPage={page}";

            var html = await GetStringAsync(url, ct);
            var parsed = Parse(html);
            if (parsed.Count == 0) break;

            var added = 0;
            foreach (var job in parsed)
                if (results.TryAdd(job.Key, job)) added++;

            // 페이지가 꽉 차지 않았으면 마지막 페이지다.
            if (parsed.Count < PageSize) break;
            // 같은 내용이 계속 오면 더 볼 것이 없다.
            if (added == 0) break;
        }

        return results.Count - before;
    }

    /// <summary>검색 결과 HTML 한 페이지에서 공고를 뽑아낸다.</summary>
    public List<JobPosting> Parse(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var items = doc.DocumentNode.SelectNodes("//div[contains(@class,'item_recruit')]");
        if (items is null) return new List<JobPosting>();

        var jobs = new List<JobPosting>();

        foreach (var item in items)
        {
            var id = item.GetAttributeValue("value", "");
            if (string.IsNullOrWhiteSpace(id)) continue;

            var link = item.SelectSingleNode(".//h2[contains(@class,'job_tit')]//a");
            if (link is null) continue;

            // 제목은 검색어가 <b> 로 강조돼 들어오므로 title 속성 쪽이 깔끔하다.
            var title = HtmlEntity.DeEntitize(link.GetAttributeValue("title", "")).Trim();
            if (title.Length == 0) title = Text(link);
            if (title.Length == 0) continue;

            var job = new JobPosting
            {
                SourceId = SourceId,
                SourceName = SourceName,
                Id = id,
                Title = title,
                // 검색 파라미터가 잔뜩 붙은 링크 대신 공고 번호만으로 여는 주소를 쓴다.
                Url = $"{BaseUrl}/zf_user/jobs/relay/view?rec_idx={id}",
                Company = Text(item.SelectSingleNode(".//strong[contains(@class,'corp_name')]//a")),
                CompanyUrl = Absolute(
                    item.SelectSingleNode(".//strong[contains(@class,'corp_name')]//a")
                        ?.GetAttributeValue("href", "") ?? ""),
                Deadline = Text(item.SelectSingleNode(".//div[contains(@class,'job_date')]//span[contains(@class,'date')]")),
                Registered = Text(item.SelectSingleNode(".//span[contains(@class,'job_day')]")),
            };

            FillCondition(job, item.SelectNodes(".//div[contains(@class,'job_condition')]/span"));
            job.Duty = ReadSector(item.SelectSingleNode(".//div[contains(@class,'job_sector')]"));

            jobs.Add(job);
        }

        return jobs;
    }

    /// <summary>
    /// job_condition 의 span 은 (지역, 경력, 학력, 고용형태) 순서지만
    /// 항목이 빠지는 경우가 있어 내용으로 판별한다. 지역 span 안에는 a 태그가 여러 개 들어온다.
    /// </summary>
    private static void FillCondition(JobPosting job, HtmlNodeCollection? spans)
    {
        if (spans is null) return;

        foreach (var span in spans)
        {
            var text = Text(span);
            if (text.Length == 0) continue;

            if (span.SelectSingleNode(".//a") is not null && job.Location.Length == 0)
                job.Location = Regex.Replace(text, @"\s+", " ");
            else if (job.Career.Length == 0 && (text.Contains("경력") || text.Contains("신입")))
                job.Career = text;
            else if (job.Education.Length == 0 && text.Contains("학력"))
                job.Education = text;
            else if (job.EmploymentType.Length == 0)
                job.EmploymentType = text;
        }
    }

    /// <summary>job_sector 에서 직무·산업 태그만 뽑는다. 등록일 span 은 뺀다.</summary>
    private static string ReadSector(HtmlNode? sector)
    {
        if (sector is null) return "";

        var links = sector.SelectNodes(".//a");
        if (links is null) return "";

        var parts = links.Select(Text).Where(t => t.Length > 0).Distinct();
        return string.Join(", ", parts);
    }

    private static string Text(HtmlNode? node) =>
        node is null ? "" : HtmlEntity.DeEntitize(node.InnerText).Replace(' ', ' ').Trim();

    private static string Absolute(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return "";
        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return href;
        return BaseUrl + (href.StartsWith('/') ? href : "/" + href);
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Referer", BaseUrl + "/");

        using var response = await _client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return Encoding.UTF8.GetString(bytes);
    }

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
    }
}
