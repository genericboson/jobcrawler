using System.Diagnostics;
using System.Text;
using JobCrawler;
using JobCrawler.Crawling;
using JobCrawler.Hosting;
using JobCrawler.Models;
using JobCrawler.Reporting;
using JobCrawler.Scheduling;
using JobCrawler.Storage;

Console.OutputEncoding = Encoding.UTF8;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

// 실행 파일이 어디에 있든 프로젝트 루트(= dailyreport 와 config.json 이 있는 곳)를 찾는다.
var root = ResolveRoot();
var configPath = Path.Combine(root, "config.json");
var config = AppConfig.Load(configPath);
var reportDir = Path.Combine(root, "dailyreport");
var store = new JobStore(reportDir);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    return command switch
    {
        "crawl" => await CrawlAsync(),
        "serve" => await ServeAsync(),
        "run" => await CrawlAsync() == 0 ? await ServeAsync(openBrowser: true) : 1,
        "list" => ListApplied(),
        "install-schedule" => WindowsSchedule.Install(SchedulableExecutable(root), root, ParseTime()),
        "uninstall-schedule" => WindowsSchedule.Uninstall(),
        "schedule-status" => WindowsSchedule.Status(),
        "help" or "-h" or "--help" => Help(),
        _ => Unknown(command),
    };
}
catch (OperationCanceledException)
{
    Console.WriteLine("중단했습니다.");
    return 130;
}

// ---------------------------------------------------------------- 명령 구현

async Task<int> CrawlAsync()
{
    var today = DateOnly.FromDateTime(DateTime.Now);
    Console.WriteLine($"게임잡 크롤링 시작 · {today:yyyy-MM-dd}");
    Console.WriteLine($"대상 직무코드: {string.Join(", ", config.DutyCodes)}");

    List<JobPosting> crawled;
    using (var crawler = new GameJobCrawler(config.MaxPages, config.RequestDelayMs, config.PageSize))
    {
        crawled = await crawler.CrawlAsync(config.DutyCodes, cts.Token);
    }

    Console.WriteLine($"중복 제거 후 {crawled.Count}건");

    var matched = crawled.Where(Matches).ToList();
    if (matched.Count != crawled.Count)
        Console.WriteLine($"키워드 조건으로 {crawled.Count - matched.Count}건 제외 → {matched.Count}건");

    // 최초 발견일을 기록해 NEW 배지 판정에 쓴다. (지원 여부와는 무관)
    store.RecordSeen(matched, today);

    var appliedIds = store.LoadAppliedIds();
    var visible = matched
        .Where(j => !appliedIds.Contains(j.Id))
        .OrderByDescending(j => j.FirstSeen == today)
        .ThenBy(j => j.Company, StringComparer.CurrentCulture)
        .ThenBy(j => j.Title, StringComparer.CurrentCulture)
        .ToList();

    var excluded = matched.Count - visible.Count;
    var path = ReportGenerator.Write(reportDir, today, visible, matched.Count, excluded, config.ServerPort);

    Console.WriteLine($"이미 지원한 공고 {excluded}건 제외 → 리포트 {visible.Count}건");
    Console.WriteLine($"리포트 생성: {path}");
    return 0;
}

async Task<int> ServeAsync(bool openBrowser = false)
{
    var server = new ReportServer(reportDir, store, config.ServerPort);

    if (openBrowser)
    {
        // 서버가 뜬 직후에 브라우저를 연다.
        _ = Task.Run(async () =>
        {
            await Task.Delay(600, cts.Token);
            OpenBrowser(server.RootUrl);
        }, cts.Token);
    }
    else
    {
        Console.WriteLine($"브라우저에서 {server.RootUrl} 을 여세요.");
    }

    await server.RunAsync(cts.Token);
    return 0;
}

int ListApplied()
{
    var applied = store.LoadApplied().OrderByDescending(a => a.AppliedAt).ToList();
    if (applied.Count == 0)
    {
        Console.WriteLine("지원한 공고가 아직 없습니다.");
        Console.WriteLine($"파일 위치: {store.OldJobListPath}");
        return 0;
    }

    Console.WriteLine($"지원한 공고 {applied.Count}건  ({store.OldJobListPath})");
    Console.WriteLine();
    foreach (var job in applied)
        Console.WriteLine($"  {job.AppliedAt:yyyy-MM-dd}  [{job.Id}]  {job.Company} · {job.Title}");

    return 0;
}

// ---------------------------------------------------------------- 보조

/// <summary>IncludeKeywords / ExcludeKeywords 조건을 적용한다.</summary>
bool Matches(JobPosting job)
{
    var haystack = $"{job.Title} {job.Duty}";

    foreach (var word in config.ExcludeKeywords)
        if (!string.IsNullOrWhiteSpace(word) &&
            haystack.Contains(word, StringComparison.OrdinalIgnoreCase))
            return false;

    if (config.IncludeKeywords.Count == 0) return true;

    foreach (var word in config.IncludeKeywords)
        if (!string.IsNullOrWhiteSpace(word) &&
            haystack.Contains(word, StringComparison.OrdinalIgnoreCase))
            return true;

    return false;
}

string ParseTime()
{
    var time = config.ScheduleTime;
    if (!TimeOnly.TryParse(time, out var parsed))
    {
        Console.Error.WriteLine($"config.json 의 ScheduleTime '{time}' 을 읽을 수 없어 20:00 으로 대체합니다.");
        return "20:00";
    }
    return parsed.ToString("HH:mm");
}

/// <summary>
/// dailyreport 와 config.json 을 한곳에 모으려면 프로젝트 루트가 필요하다.
/// 개발 중(bin/Debug/net8.0)에도, 게시한 exe 옆에서도 같은 위치를 가리키도록
/// 위로 올라가며 솔루션 파일이나 dailyreport 폴더를 찾는다.
/// JOBCRAWLER_HOME 환경변수를 두면 그 값이 우선한다.
/// </summary>
static string ResolveRoot()
{
    var env = Environment.GetEnvironmentVariable("JOBCRAWLER_HOME");
    if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(env);

    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "dailyreport")) ||
            // .sln 과 .NET 10 의 .slnx 를 모두 받아들인다.
            dir.EnumerateFiles("*.sln").Any() ||
            dir.EnumerateFiles("*.slnx").Any())
            return dir.FullName;
        dir = dir.Parent;
    }

    return AppContext.BaseDirectory;
}

/// <summary>
/// 스케줄러에 등록할 실행 파일을 고른다.
/// bin/Debug 는 clean 이나 구성 변경으로 사라질 수 있으므로
/// dotnet publish 로 만든 app/JobCrawler.exe 가 있으면 그쪽을 쓴다.
/// </summary>
static string SchedulableExecutable(string root)
{
    var published = Path.Combine(root, "app", "JobCrawler.exe");
    if (File.Exists(published)) return published;

    var path = Environment.ProcessPath;
    if (!string.IsNullOrEmpty(path) &&
        !Path.GetFileName(path).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"참고: app/JobCrawler.exe 가 없어 현재 실행 파일을 등록합니다.");
        Console.WriteLine("      'dotnet publish src/JobCrawler -c Release -o app' 후 다시 등록하면 더 안정적입니다.");
        return path;
    }

    return published;
}

static void OpenBrowser(string url)
{
    try
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"브라우저를 열지 못했습니다({ex.Message}). 직접 {url} 을 여세요.");
    }
}

static int Help()
{
    Console.WriteLine("""
        게임잡 서버 프로그래머 공고 크롤러

        사용법: JobCrawler <명령>

          crawl               게임잡을 크롤링해 dailyreport/yyyy-MM-dd.html 을 만든다.
                              (작업 스케줄러가 매일 실행하는 명령)
          serve               리포트 열람 서버를 띄운다. 체크박스를 켜면 oldjoblist.json 에 기록된다.
          run                 crawl 후 serve 하고 브라우저를 연다.
          list                oldjoblist.json 에 등재된 지원 공고를 출력한다.

          install-schedule    매일 정해진 시각(config.json 의 ScheduleTime)에 crawl 을 돌리도록 등록한다.
          uninstall-schedule  위 작업을 해제한다.
          schedule-status     등록된 작업 상태를 본다.

        설정 파일: config.json  (직무코드, 키워드 필터, 포트, 실행 시각)
        상태 파일: dailyreport/oldjoblist.json  (지원한 공고 - 다음 리포트에서 제외됨)
                   dailyreport/seenjobs.json    (최초 발견일 - NEW 배지 판정용)
        """);
    return 0;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"알 수 없는 명령: {command}");
    Help();
    return 2;
}
