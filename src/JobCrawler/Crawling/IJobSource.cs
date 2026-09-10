using System.Net;
using JobCrawler.Models;

namespace JobCrawler.Crawling;

/// <summary>채용 사이트 하나를 크롤링하는 대상.</summary>
public interface IJobSource
{
    /// <summary>"gamejob" 처럼 저장 키에 쓰는 식별자. 한 번 정하면 바꾸지 않는다.</summary>
    string SourceId { get; }

    /// <summary>"게임잡" 처럼 사람에게 보여줄 이름.</summary>
    string SourceName { get; }

    /// <summary>공고를 모아 온다. 사이트 안에서의 중복은 여기서 없앤다.</summary>
    Task<List<JobPosting>> CrawlAsync(CancellationToken ct);
}

/// <summary>크롤러들이 공유하는 HttpClient 설정.</summary>
public static class CrawlerHttp
{
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public static HttpClient Create(out HttpClientHandler handler)
    {
        handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ko-KR,ko;q=0.9");
        return client;
    }
}
