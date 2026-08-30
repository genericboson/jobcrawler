using System.Net;
using System.Text;
using JobCrawler.Models;

namespace JobCrawler.Reporting;

/// <summary>
/// 메일 본문용 HTML 을 만든다.
///
/// 메일 클라이언트는 &lt;style&gt; 블록과 스크립트를 대부분 걷어내므로
/// 리포트 페이지를 그대로 보내지 않고, 인라인 스타일과 표로만 짠 읽기 전용 버전을 따로 만든다.
/// 체크박스는 메일 안에서 동작할 수 없으니 리포트 주소 안내로 대신한다.
/// </summary>
public static class EmailReportBuilder
{
    public static string Subject(DateOnly date, int total, int newCount) =>
        newCount > 0
            ? $"[게임잡] {date:yyyy-MM-dd} 서버 프로그래머 공고 {total}건 (신규 {newCount}건)"
            : $"[게임잡] {date:yyyy-MM-dd} 서버 프로그래머 공고 {total}건";

    public static string BuildHtml(
        DateOnly date,
        IReadOnlyList<JobPosting> jobs,
        int totalCrawled,
        int excludedCount,
        int serverPort)
    {
        var newCount = jobs.Count(j => j.FirstSeen == date);
        var reportUrl = $"http://localhost:{serverPort}/";
        var sb = new StringBuilder();

        sb.Append($"""
            <div style="margin:0;padding:24px 12px;background:#f6f7f9;font-family:'Malgun Gothic','Apple SD Gothic Neo',sans-serif;color:#16181d;">
            <div style="max-width:680px;margin:0 auto;">

              <h1 style="margin:0 0 4px;font-size:20px;">게임잡 · 서버 프로그래머 공고</h1>
              <div style="color:#6b7280;font-size:13px;">{date:yyyy년 M월 d일} 리포트</div>

              <div style="margin:14px 0 18px;font-size:13px;color:#374151;">
                수집 <b>{totalCrawled}</b>건 ·
                지원한 공고 제외 <b>{excludedCount}</b>건 ·
                <b>오늘 목록 {jobs.Count}건</b> ·
                신규 <b>{newCount}</b>건
              </div>

              <div style="margin:0 0 18px;padding:12px 14px;background:#e8f0fe;border-radius:8px;font-size:13px;color:#1f3f7a;">
                지원한 공고를 체크하려면 PC 에서 <code>JobCrawler serve</code> 를 실행한 뒤
                <a href="{reportUrl}" style="color:#2f6fed;">{reportUrl}</a> 을 여세요.
                메일 안에서는 체크박스가 동작하지 않습니다.
              </div>

            """);

        if (jobs.Count == 0)
        {
            sb.Append("""
                  <div style="padding:36px 20px;text-align:center;color:#6b7280;background:#ffffff;border:1px dashed #e3e6ea;border-radius:10px;">
                    오늘 새로 보여줄 공고가 없습니다.
                  </div>

                """);
        }
        else
        {
            foreach (var job in jobs)
                AppendJob(sb, job, date);
        }

        sb.Append("""
              <div style="margin-top:22px;font-size:12px;color:#9aa1ab;text-align:center;">
                체크한 공고는 oldjoblist 에 기록되어 다음 리포트부터 빠집니다.
              </div>

            </div>
            </div>
            """);

        return sb.ToString();
    }

    private static void AppendJob(StringBuilder sb, JobPosting job, DateOnly today)
    {
        var badge = job.FirstSeen == today
            ? """<span style="display:inline-block;margin-left:6px;padding:1px 6px;border-radius:4px;background:#e3f5ec;color:#0f9960;font-size:11px;font-weight:bold;">NEW</span>"""
            : "";

        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(job.Duty)) tags.Add(job.Duty);
        tags.AddRange(job.Tags);
        if (!string.IsNullOrWhiteSpace(job.Deadline)) tags.Add("마감 " + job.Deadline);

        sb.Append($"""
              <div style="margin:0 0 10px;padding:14px 16px;background:#ffffff;border:1px solid #e3e6ea;border-radius:10px;">
                <div style="font-size:15px;font-weight:bold;line-height:1.4;">
                  <a href="{Attr(job.Url)}" style="color:#16181d;text-decoration:none;">{Html(job.Title)}</a>{badge}
                </div>
                <div style="margin-top:3px;font-size:13px;color:#6b7280;">{Html(job.Company)}</div>
                <div style="margin-top:7px;font-size:12px;color:#6b7280;">{Html(string.Join(" · ", tags))}</div>
              </div>

            """);
    }

    private static string Html(string s) => WebUtility.HtmlEncode(s ?? "");

    private static string Attr(string s) => WebUtility.HtmlEncode(s ?? "").Replace("\"", "&quot;");
}
