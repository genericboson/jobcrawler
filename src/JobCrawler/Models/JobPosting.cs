using System.Text.Json.Serialization;

namespace JobCrawler.Models;

/// <summary>게임잡 채용공고 한 건.</summary>
public sealed class JobPosting
{
    /// <summary>게임잡 공고 번호(GI_No). 공고를 식별하는 유일한 키.</summary>
    public string Id { get; set; } = "";

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

    /// <summary>마감일 표기(예: "채용시", "09/12(금)").</summary>
    public string Deadline { get; set; } = "";

    /// <summary>등록/수정일 표기(예: "07/15(수) 등록").</summary>
    public string Registered { get; set; } = "";

    /// <summary>이 공고를 처음 발견한 날짜. 리포트의 NEW 배지에 사용.</summary>
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
