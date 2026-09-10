using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using JobCrawler.Models;

namespace JobCrawler.Crawling;

/// <summary>
/// 잡코리아(jobkorea.co.kr) 키워드 검색 크롤러.
///
/// 검색 페이지는 Next.js 로 만들어져 있어 눈에 보이는 마크업은 유틸리티 클래스뿐이라
/// 긁어내기 나쁘다. 대신 서버가 렌더링하면서 react-query 의 상태를 JSON 으로 심어 두는데,
/// 거기에 공고 목록이 필드 이름까지 갖춰 들어 있다. 그 JSON 을 읽는다.
/// 페이지는 Page_No 파라미터로 넘기고 한 페이지에 20건이 온다.
/// </summary>
public sealed class JobKoreaCrawler : IJobSource, IDisposable
{
    private const string BaseUrl = "https://www.jobkorea.co.kr";

    /// <summary>페이지에 심긴 JSON 중 공고 목록을 담은 덩어리를 찾는 표식.</summary>
    private static readonly Regex PagePattern = new(
        @"\{""pageSize"":\d+,""pageNumber"":\d+,""totalElements"":\d+,""totalPages"":\d+,""content"":\[",
        RegexOptions.Compiled);

    public string SourceId => "jobkorea";
    public string SourceName => "잡코리아";

    private readonly JobKoreaSettings _settings;
    private readonly int _delayMs;
    private readonly HttpClient _client;
    private readonly HttpClientHandler _handler;

    public JobKoreaCrawler(JobKoreaSettings settings, int requestDelayMs)
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
        var lastPage = 1;

        for (var page = 1; page <= Math.Min(lastPage, _settings.MaxPages); page++)
        {
            if (page > 1 && _delayMs > 0) await Task.Delay(_delayMs, ct);

            var url = $"{BaseUrl}/Search/?stext={HttpUtility.UrlEncode(keyword, Encoding.UTF8)}&Page_No={page}";
            var html = await GetStringAsync(url, ct);

            if (!TryReadJobPage(html, out var jobs, out var totalPages)) break;
            if (jobs.Count == 0) break;

            var added = 0;
            foreach (var job in jobs)
                if (results.TryAdd(job.Key, job)) added++;

            lastPage = Math.Max(lastPage, totalPages);
            if (added == 0) break;
        }

        return results.Count - before;
    }

    /// <summary>페이지에 심긴 JSON 에서 공고 목록을 읽는다.</summary>
    public bool TryReadJobPage(string html, out List<JobPosting> jobs, out int totalPages)
    {
        jobs = new List<JobPosting>();
        totalPages = 1;

        // 심어 둔 JSON 은 자바스크립트 문자열 안에 있어 따옴표가 이스케이프되어 있다.
        var text = html.Replace("\\\"", "\"").Replace("\\\\", "\\");

        foreach (Match match in PagePattern.Matches(text))
        {
            var blob = ExtractObject(text, match.Index);
            if (blob is null) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(blob); }
            catch (JsonException) { continue; }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("content", out var content) ||
                    content.ValueKind != JsonValueKind.Array ||
                    content.GetArrayLength() == 0)
                    continue;

                // 검색 페이지에는 광고 같은 다른 목록도 함께 심긴다.
                // 공고 목록에만 있는 필드로 골라낸다.
                if (!content[0].TryGetProperty("postingCompanyName", out _)) continue;

                if (doc.RootElement.TryGetProperty("totalPages", out var pages) &&
                    pages.TryGetInt32(out var p))
                    totalPages = Math.Max(1, p);

                foreach (var item in content.EnumerateArray())
                {
                    var job = ReadJob(item);
                    if (job is not null) jobs.Add(job);
                }

                return true;
            }
        }

        return false;
    }

    private JobPosting? ReadJob(JsonElement item)
    {
        var id = Str(item, "id");
        var title = Str(item, "title");
        if (id.Length == 0 || title.Length == 0) return null;

        var company = Str(item, "postingCompanyName");
        if (company.Length == 0) company = Str(item, "companyName");

        return new JobPosting
        {
            SourceId = SourceId,
            SourceName = SourceName,
            Id = id,
            Title = title,
            Company = company,
            Url = $"{BaseUrl}/Recruit/GI_Read/{id}",
            // 코드 목록 대신 사람이 읽을 수 있게 들어 있는 값을 쓴다.
            Duty = TrimSeparators(Str(item, "jobClassificationOrIndustry")),
            Tech = ReadTech(item),
            Location = FirstToken(Str(item, "_internal_featureLocationCode")),
            Registered = FormatDate(Str(item, "createdAt"), "등록"),
            Deadline = ReadDeadline(item),
        };
    }

    /// <summary>
    /// 잡코리아의 직무 분류에는 언어·프레임워크가 없다(대분류만 온다).
    /// 대신 검색 색인용 필드에 기술 스택이 들어 있어 그쪽을 모아 키워드 조건에 쓴다.
    /// 공고마다 채워져 있기도 하고 비어 있기도 하다.
    /// </summary>
    private static string ReadTech(JsonElement item)
    {
        var parts = new List<string>();

        foreach (var field in new[] { "_internal_featureToolCode", "_internal_featureSkillCode" })
        {
            var value = Str(item, field);
            if (value.Length == 0) continue;

            parts.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return string.Join(", ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string ReadDeadline(JsonElement item)
    {
        if (!item.TryGetProperty("applicationPeriod", out var period) ||
            period.ValueKind != JsonValueKind.Object)
            return "";

        var end = Str(period, "end");
        if (end.Length == 0) return "";
        if (!DateTimeOffset.TryParse(end, out var when)) return "";

        // 상시채용은 2070-01-01 같은 먼 날짜로 들어온다. 날짜로 보여주면 오해를 부른다.
        if (when.Year > DateTime.Now.Year + 5) return "채용시";

        return when.ToString("MM/dd");
    }

    private static string FormatDate(string iso, string suffix)
    {
        if (!DateTimeOffset.TryParse(iso, out var when)) return "";
        var text = when.ToString("MM/dd");
        return suffix.Length == 0 ? text : $"{text} {suffix}";
    }

    /// <summary>",게임·애니메이션,게임개발자," 처럼 앞뒤에 구분자가 붙어 오는 값을 다듬는다.</summary>
    private static string TrimSeparators(string value)
    {
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(", ", parts);
    }

    /// <summary>"서초구,남부터미널" 처럼 여러 값이 오면 앞의 것만 쓴다.</summary>
    private static string FirstToken(string value)
    {
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? "" : parts[0];
    }

    private static string Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    /// <summary>
    /// 여는 중괄호 위치부터 짝이 맞는 닫는 중괄호까지를 잘라낸다.
    /// 문자열 안의 괄호를 세지 않도록 따옴표와 역슬래시를 함께 본다.
    /// </summary>
    private static string? ExtractObject(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\') { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return text[start..(i + 1)];
            }
        }

        return null;
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
