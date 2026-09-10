using JobCrawler.Models;

namespace JobCrawler;

/// <summary>사이트별 공통 설정.</summary>
public abstract class SourceSettings
{
    /// <summary>false 면 이 사이트는 크롤링하지 않는다.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 제목·직무에 이 중 하나라도 있어야 리포트에 넣는다.
    /// 비워두면 그 사이트에서 가져온 결과를 전부 넣는다.
    /// </summary>
    public List<string> IncludeKeywords { get; set; } = new();

    /// <summary>제목·직무에 이 단어가 있으면 뺀다.</summary>
    public List<string> ExcludeKeywords { get; set; } = new();

    /// <summary>읽어올 최대 페이지 수(안전장치).</summary>
    public int MaxPages { get; set; } = 20;

    /// <summary>제목·직무·기술 스택을 합쳐 키워드 조건을 적용한다.</summary>
    public bool Matches(JobPosting job) => Matches(job.Title, job.Duty, job.Tech);

    public bool Matches(string title, string duty, string tech = "")
    {
        var haystack = $"{title} {duty} {tech}";

        foreach (var word in ExcludeKeywords)
            if (!string.IsNullOrWhiteSpace(word) &&
                haystack.Contains(word, StringComparison.OrdinalIgnoreCase))
                return false;

        if (IncludeKeywords.Count == 0) return true;

        foreach (var word in IncludeKeywords)
            if (!string.IsNullOrWhiteSpace(word) &&
                haystack.Contains(word, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }
}

/// <summary>
/// 게임잡 설정. 게임잡은 키워드 검색이 아니라 직무 코드로 목록을 받아온다.
/// </summary>
public sealed class GameJobSettings : SourceSettings
{
    /// <summary>
    /// 게임잡 직무(duty) 코드. 16 = 기술지원 &gt; 서버.
    /// 1=게임개발(클라이언트), 2=게임개발(모바일), 17=네트워크, 18=엔진, 19=시스템·DB, 21=클라우드
    /// </summary>
    public List<int> DutyCodes { get; set; } = new() { 16 };

    /// <summary>한 페이지에 받아올 공고 수. 게임잡이 받아들이는 범위는 20~100.</summary>
    public int PageSize { get; set; } = 100;
}

/// <summary>키워드로 검색하는 사이트(사람인, 잡코리아)의 설정.</summary>
public class KeywordSourceSettings : SourceSettings
{
    /// <summary>검색어. 여러 개를 넣으면 각각 검색해 합친 뒤 중복을 없앤다.</summary>
    public List<string> Keywords { get; set; } = new() { "서버 프로그래머" };
}

public sealed class SaraminSettings : KeywordSourceSettings;

public sealed class JobKoreaSettings : KeywordSourceSettings;

/// <summary>사이트별 설정 묶음.</summary>
public sealed class SourcesSettings
{
    public GameJobSettings GameJob { get; set; } = new();
    public SaraminSettings Saramin { get; set; } = new();
    public JobKoreaSettings JobKorea { get; set; } = new();
}
