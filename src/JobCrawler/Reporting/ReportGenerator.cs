using System.Net;
using System.Text;
using System.Text.Json;
using JobCrawler.Models;

namespace JobCrawler.Reporting;

/// <summary>dailyreport/yyyy-MM-dd.html 리포트를 만든다.</summary>
public static class ReportGenerator
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>리포트를 써 넣고 파일 경로를 돌려준다.</summary>
    public static string Write(
        string reportDirectory,
        DateOnly date,
        IReadOnlyList<JobPosting> jobs,
        int totalCrawled,
        int excludedCount,
        int serverPort)
    {
        Directory.CreateDirectory(reportDirectory);
        var path = Path.Combine(reportDirectory, $"{date:yyyy-MM-dd}.html");
        File.WriteAllText(path, BuildHtml(date, jobs, totalCrawled, excludedCount, serverPort), new UTF8Encoding(false));
        return path;
    }

    private static string BuildHtml(
        DateOnly date,
        IReadOnlyList<JobPosting> jobs,
        int totalCrawled,
        int excludedCount,
        int serverPort)
    {
        var newCount = jobs.Count(j => j.FirstSeen == date);
        var sourceSummary = Html(EmailReportBuilder.SourceSummary(jobs));
        var sb = new StringBuilder();

        sb.Append($$"""
<!DOCTYPE html>
<html lang="ko" data-port="{{serverPort}}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>서버 프로그래머 공고 {{date:yyyy-MM-dd}}</title>
<style>
  :root {
    --bg: #f6f7f9;      --card: #ffffff;   --text: #16181d;   --muted: #6b7280;
    --line: #e3e6ea;    --accent: #2f6fed; --accent-soft: #e8f0fe;
    --new: #0f9960;     --new-soft: #e3f5ec;
    --warn: #b45309;    --warn-soft: #fef3c7;
    --done: #9aa1ab;
    --src: #7c3aed;     --src-soft: #f0e9fe;
  }
  @media (prefers-color-scheme: dark) {
    :root {
      --bg: #14161a;    --card: #1c1f25;   --text: #e8eaed;   --muted: #9aa1ab;
      --line: #2c3038;  --accent: #6c9bff; --accent-soft: #1e2a44;
      --new: #4ade80;   --new-soft: #16301f;
      --warn: #fbbf24;  --warn-soft: #3a2e10;
      --done: #6b7280;
      --src: #c4b5fd;   --src-soft: #2a2140;
    }
  }
  * { box-sizing: border-box; }
  body {
    margin: 0; padding: 24px 16px 64px;
    background: var(--bg); color: var(--text);
    font: 15px/1.6 "Pretendard", -apple-system, "Segoe UI", "Malgun Gothic", sans-serif;
  }
  .wrap { max-width: 960px; margin: 0 auto; }
  header { margin-bottom: 20px; }
  h1 { margin: 0 0 6px; font-size: 22px; letter-spacing: -0.01em; }
  .sub { color: var(--muted); font-size: 13px; }
  .stats { display: flex; flex-wrap: wrap; gap: 8px; margin: 14px 0 0; }
  .stat {
    background: var(--card); border: 1px solid var(--line); border-radius: 8px;
    padding: 6px 12px; font-size: 13px;
  }
  .stat b { font-size: 15px; }
  .bar {
    display: flex; flex-wrap: wrap; gap: 10px; align-items: center;
    margin: 18px 0 14px; padding: 12px; background: var(--card);
    border: 1px solid var(--line); border-radius: 10px;
  }
  .bar input[type=search] {
    flex: 1 1 220px; min-width: 0; padding: 7px 10px; font: inherit; font-size: 14px;
    border: 1px solid var(--line); border-radius: 7px;
    background: var(--bg); color: var(--text);
  }
  .bar label { font-size: 13px; color: var(--muted); display: flex; align-items: center; gap: 6px; cursor: pointer; }
  #status { font-size: 12px; padding: 4px 9px; border-radius: 999px; white-space: nowrap; }
  #status.online  { background: var(--new-soft);  color: var(--new); }
  #status.offline { background: var(--warn-soft); color: var(--warn); }
  .notice {
    display: none; margin: 0 0 14px; padding: 11px 14px; border-radius: 9px;
    background: var(--warn-soft); color: var(--warn); font-size: 13px;
    border: 1px solid color-mix(in srgb, var(--warn) 30%, transparent);
  }
  .notice.show { display: block; }
  .notice code {
    background: color-mix(in srgb, var(--warn) 15%, transparent);
    padding: 1px 5px; border-radius: 4px; font-size: 12px;
  }
  ul.jobs { list-style: none; margin: 0; padding: 0; display: grid; gap: 10px; }
  li.job {
    display: grid; grid-template-columns: auto 1fr; gap: 12px;
    background: var(--card); border: 1px solid var(--line);
    border-radius: 10px; padding: 14px 16px;
  }
  li.job.applied { opacity: .5; }
  li.job.applied .title a { color: var(--done); text-decoration: line-through; }
  li.job.hidden { display: none; }
  .chk { display: flex; align-items: flex-start; padding-top: 2px; }
  .chk input { width: 19px; height: 19px; cursor: pointer; accent-color: var(--accent); }
  .title { display: flex; flex-wrap: wrap; align-items: baseline; gap: 8px; }
  .title a {
    color: var(--text); font-weight: 600; font-size: 15.5px; text-decoration: none;
  }
  .title a:hover { color: var(--accent); text-decoration: underline; }
  .badge {
    font-size: 11px; font-weight: 700; letter-spacing: .03em;
    padding: 1px 6px; border-radius: 5px; background: var(--new-soft); color: var(--new);
  }
  .company { margin-top: 3px; font-size: 13.5px; color: var(--muted); }
  .company a { color: inherit; text-decoration: none; }
  .company a:hover { text-decoration: underline; }
  .tags { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 8px; }
  .tag {
    font-size: 12px; color: var(--muted);
    border: 1px solid var(--line); border-radius: 999px; padding: 1px 8px;
  }
  .tag.duty { background: var(--accent-soft); color: var(--accent); border-color: transparent; }
  .tag.src { background: var(--src-soft); color: var(--src); border-color: transparent; font-weight: 600; }
  .tag.deadline { background: var(--bg); }
  .empty {
    text-align: center; padding: 60px 20px; color: var(--muted);
    background: var(--card); border: 1px dashed var(--line); border-radius: 10px;
  }
  footer { margin-top: 28px; font-size: 12px; color: var(--muted); text-align: center; }
</style>
</head>
<body>
<div class="wrap">
<header>
  <h1>서버 프로그래머 공고</h1>
  <div class="sub">{{date:yyyy년 M월 d일 (ddd)}} 리포트 · {{sourceSummary}}</div>
  <div class="stats">
    <span class="stat">수집 <b>{{totalCrawled}}</b></span>
    <span class="stat">지원한 공고 제외 <b>{{excludedCount}}</b></span>
    <span class="stat">오늘 목록 <b id="visibleCount">{{jobs.Count}}</b></span>
    <span class="stat">신규 <b>{{newCount}}</b></span>
  </div>
</header>

<div class="notice" id="offlineNotice">
  체크 결과를 <b>oldjoblist.json 에 저장할 수 없는 상태</b>입니다.
  브라우저에 임시 보관해 두었다가 서버가 켜지면 자동으로 반영합니다.
  리포트 서버가 꺼져 있는 것 같습니다. <code>JobCrawler install-schedule</code> 을 한 번 실행해 두면
  로그인할 때마다 서버가 자동으로 떠서 체크가 항상 즉시 저장됩니다.
  지금 바로 켜려면 <code>JobCrawler serve</code> 를 실행하세요.
</div>

<div class="bar">
  <input type="search" id="filter" placeholder="회사·공고명·지역으로 거르기" autocomplete="off">
  <label><input type="checkbox" id="hideApplied"> 체크한 공고 숨기기</label>
  <span id="status" class="offline">연결 확인 중…</span>
</div>

""");

        if (jobs.Count == 0)
        {
            sb.AppendLine("""<div class="empty">오늘 새로 보여줄 공고가 없습니다.</div>""");
        }
        else
        {
            sb.AppendLine("""<ul class="jobs">""");
            foreach (var job in jobs)
                AppendJob(sb, job, date);
            sb.AppendLine("</ul>");
        }

        sb.Append($$"""
<footer>
  체크한 공고는 <code>dailyreport/oldjoblist.json</code> 에 기록되어 다음 리포트부터 빠집니다.
  · 생성 {{DateTime.Now:yyyy-MM-dd HH:mm:ss}}
</footer>
</div>

<script>
(function () {
  "use strict";

  var PORT = document.documentElement.dataset.port;
  var API  = "http://localhost:" + PORT + "/api";
  var PENDING_KEY = "jobcrawler.pending";
  var statusEl  = document.getElementById("status");
  var noticeEl  = document.getElementById("offlineNotice");
  var online    = false;

  function readPending() {
    try { return JSON.parse(localStorage.getItem(PENDING_KEY) || "{}"); }
    catch (e) { return {}; }
  }
  function writePending(map) {
    try { localStorage.setItem(PENDING_KEY, JSON.stringify(map)); } catch (e) { /* 저장 못 해도 진행 */ }
  }

  function setStatus(isOnline, pendingCount) {
    online = isOnline;
    statusEl.className = isOnline ? "online" : "offline";
    if (isOnline) {
      statusEl.textContent = "oldjoblist 연결됨";
      noticeEl.classList.remove("show");
    } else {
      statusEl.textContent = pendingCount
        ? "오프라인 · 저장 대기 " + pendingCount + "건"
        : "오프라인 (기록은 브라우저에 임시 보관)";
      noticeEl.classList.add("show");
    }
  }

  function send(entry) {
    return fetch(API + "/applied", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(entry)
    }).then(function (r) {
      if (!r.ok) throw new Error("HTTP " + r.status);
      return true;
    });
  }

  function queue(entry) {
    var map = readPending();
    map[entry.id] = entry;
    writePending(map);
    setStatus(false, Object.keys(map).length);
  }

  function flushPending() {
    var map = readPending();
    var ids = Object.keys(map);
    if (!ids.length) return Promise.resolve();

    return Promise.all(ids.map(function (id) {
      return send(map[id]).then(function () { delete map[id]; }, function () { /* 다음에 재시도 */ });
    })).then(function () {
      writePending(map);
      setStatus(true, Object.keys(map).length);
    });
  }

  // 체크박스 상태를 서버(또는 임시 보관분)의 기록과 맞춘다.
  function syncCheckboxes(appliedIds) {
    var pending = readPending();
    document.querySelectorAll("li.job").forEach(function (li) {
      var box = li.querySelector("input[type=checkbox]");
      var id  = li.dataset.id;
      var checked = appliedIds.indexOf(id) !== -1;
      if (pending[id]) checked = pending[id].applied;
      box.checked = checked;
      li.classList.toggle("applied", checked);
    });
    applyFilter();
  }

  function onToggle(li, box) {
    var checked = box.checked;
    li.classList.toggle("applied", checked);
    applyFilter();

    var entry = {
      id: li.dataset.id,
      title: li.dataset.title,
      company: li.dataset.company,
      url: li.dataset.url,
      applied: checked
    };

    if (!online) { queue(entry); return; }

    send(entry).catch(function () {
      queue(entry);
    });
  }

  var filterEl = document.getElementById("filter");
  var hideEl   = document.getElementById("hideApplied");
  var countEl  = document.getElementById("visibleCount");

  function applyFilter() {
    var q = filterEl.value.trim().toLowerCase();
    var hide = hideEl.checked;
    var visible = 0;

    document.querySelectorAll("li.job").forEach(function (li) {
      var matches = !q || li.dataset.search.indexOf(q) !== -1;
      var isApplied = li.classList.contains("applied");
      var show = matches && !(hide && isApplied);
      li.classList.toggle("hidden", !show);
      if (show) visible++;
    });

    countEl.textContent = visible;
  }

  filterEl.addEventListener("input", applyFilter);
  hideEl.addEventListener("change", applyFilter);

  document.querySelectorAll("li.job").forEach(function (li) {
    var box = li.querySelector("input[type=checkbox]");
    box.addEventListener("change", function () { onToggle(li, box); });
  });

  // 시작 시 서버 연결을 확인하고, 밀린 기록을 밀어 넣은 뒤 체크 상태를 맞춘다.
  fetch(API + "/applied", { method: "GET" })
    .then(function (r) {
      if (!r.ok) throw new Error("HTTP " + r.status);
      return r.json();
    })
    .then(function (ids) {
      setStatus(true, 0);
      return flushPending().then(function () { return ids; });
    })
    .then(function (ids) {
      return fetch(API + "/applied").then(function (r) { return r.json(); }).catch(function () { return ids; });
    })
    .then(syncCheckboxes)
    .catch(function () {
      setStatus(false, Object.keys(readPending()).length);
      syncCheckboxes([]);
    });
})();
</script>
</body>
</html>
""");

        return sb.ToString();
    }

    private static void AppendJob(StringBuilder sb, JobPosting job, DateOnly today)
    {
        var searchBlob = string.Join(' ', new[]
        {
            job.Title, job.Company, job.Location, job.GameType, job.Duty, job.Career, job.SourceName,
        }.Where(s => !string.IsNullOrWhiteSpace(s))).ToLowerInvariant();

        sb.Append("  <li class=\"job\"");
        // 사이트가 다르면 공고 번호가 겹칠 수 있다. 저장 키는 반드시 "사이트:번호" 를 쓴다.
        sb.Append($" data-id=\"{Attr(job.Key)}\"");
        sb.Append($" data-title=\"{Attr(job.Title)}\"");
        sb.Append($" data-company=\"{Attr(job.Company)}\"");
        sb.Append($" data-url=\"{Attr(job.Url)}\"");
        sb.Append($" data-search=\"{Attr(searchBlob)}\"");
        sb.AppendLine(">");

        sb.AppendLine($"""    <div class="chk"><input type="checkbox" aria-label="{Attr(job.Title)} 지원함"></div>""");
        sb.AppendLine("    <div>");

        sb.Append("""      <div class="title">""");
        sb.Append($"""<a href="{Attr(job.Url)}" target="_blank" rel="noopener">{Html(job.Title)}</a>""");
        if (job.FirstSeen == today) sb.Append("""<span class="badge">NEW</span>""");
        sb.AppendLine("</div>");

        sb.Append("""      <div class="company">""");
        sb.Append(string.IsNullOrWhiteSpace(job.CompanyUrl)
            ? Html(job.Company)
            : $"""<a href="{Attr(job.CompanyUrl)}" target="_blank" rel="noopener">{Html(job.Company)}</a>""");
        sb.AppendLine("</div>");

        sb.AppendLine("""      <div class="tags">""");
        sb.AppendLine($"""        <span class="tag src">{Html(job.SourceName)}</span>""");
        if (!string.IsNullOrWhiteSpace(job.Duty))
            sb.AppendLine($"""        <span class="tag duty">{Html(job.Duty)}</span>""");
        foreach (var tag in job.Tags)
            sb.AppendLine($"""        <span class="tag">{Html(tag)}</span>""");
        if (!string.IsNullOrWhiteSpace(job.Deadline))
            sb.AppendLine($"""        <span class="tag deadline">마감 {Html(job.Deadline)}</span>""");
        if (!string.IsNullOrWhiteSpace(job.Registered))
            sb.AppendLine($"""        <span class="tag deadline">{Html(job.Registered)}</span>""");
        sb.AppendLine("      </div>");

        sb.AppendLine("    </div>");
        sb.AppendLine("  </li>");
    }

    private static string Html(string s) => WebUtility.HtmlEncode(s ?? "");

    private static string Attr(string s) => WebUtility.HtmlEncode(s ?? "").Replace("\"", "&quot;");
}
