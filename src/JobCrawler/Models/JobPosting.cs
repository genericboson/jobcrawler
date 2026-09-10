using System.Text.Json.Serialization;

namespace JobCrawler.Models;

/// <summary>채용공고 한 건. 사이트가 달라도 이 모양으로 맞춰서 담는다.</summary>
public sealed class JobPosting
{
    /// <summary>사이트 식별자. "gamejob", "saramin", "jobkorea".</summary>
    public string SourceId { get; set; } = "";

    /// <summary>사람이 읽는 사이트 이름. "게임잡", "사람인", "잡코리아".</summary>
    public string SourceName { get; set; } = "";

    /// <summary>사이트 안에서의 공고 번호. 사이트가 다르면 번호가 겹칠 수 있다.</summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// 사이트를 가로질러 공고를 구분하는 키. 지원 이력과 최초 발견일은 이 값으로 기록한다.
    /// </summary>
    [JsonIgnore]
    public string Key => MakeKey(SourceId, Id);

    public static string MakeKey(string sourceId, string id) => $"{sourceId}:{id}";

    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string Url { get; set; } = "";
    public string CompanyUrl { get; set; } = "";

    /// <summary>공고에 붙은 직무 태그(예: "게임개발(모바일), 서버").</summary>
    public string Duty { get; set; } = "";

    public string Career { get; set; } = "";
    public string Education { get; set; } = "";
    public string Location { get; set; } = "";
    public string GameType { get; set; } = "";
    public string EmploymentType { get; set; } = "";

    /// <summary>마감일 표기(예: "채용시", "09/12").</summary>
    public string Deadline { get; set; } = "";

    /// <summary>등록/수정일 표기(예: "07/15 등록").</summary>
    public string Registered { get; set; } = "";

    /// <summary>이 공고를 처음 발견한 날짜. 리포트의 NEW 배지에 쓴다.</summary>
    public DateOnly? FirstSeen { get; set; }

    [JsonIgnore]
    public IEnumerable<string> Tags
    {
        get
        {
            foreach (var t in new[] { Career, Education, Location, GameType, EmploymentType })
                if (!string.IsNullOrWhiteSpace(t)) yield return t;
        }
    }
}
