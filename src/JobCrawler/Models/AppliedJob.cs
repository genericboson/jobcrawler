namespace JobCrawler.Models;

/// <summary>oldjoblist.json 에 기록되는 "이미 지원한 공고" 한 건.</summary>
public sealed class AppliedJob
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>지원 처리한 시각(체크박스를 켠 시각).</summary>
    public DateTimeOffset AppliedAt { get; set; }
}
