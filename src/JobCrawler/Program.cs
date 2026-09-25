using System.Diagnostics;
using System.Text;
using JobCrawler;
using JobCrawler.Crawling;
using JobCrawler.Hosting;
using JobCrawler.Mailing;
using JobCrawler.Models;
using JobCrawler.Reporting;
using JobCrawler.Scheduling;
using JobCrawler.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// 서비스로 실행되면 콘솔이 없어 인코딩 지정이 실패한다. 실패해도 그냥 진행한다.
try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { }

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
        "test-email" => await TestEmailAsync(),
        "install-schedule" => WindowsSchedule.InstallDaily(SchedulableExecutable(root), root, ParseTime()),
        "install-service" => WindowsServiceInstaller.Install(SchedulableExecutable(root), config.ServerPort),
        "uninstall-service" => WindowsServiceInstaller.Uninstall(),
        "service-status" => WindowsServiceInstaller.Status(),
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
    Console.WriteLine($"크롤링 시작 · {today:yyyy-MM-dd}");

    var collected = new Dictionary<string, JobPosting>();
    var perSource = new List<string>();

    foreach (var (source, settings) in BuildSources())
    {
        Console.WriteLine($"[{source.SourceName}]");

        List<JobPosting> found;
        try
        {
            using (source as IDisposable)
                found = await source.CrawlAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 한 사이트가 막히거나 구조가 바뀌어도 나머지 사이트 결과는 살린다.
            Console.Error.WriteLine($"  {source.SourceName} 크롤링 실패: {ex.Message}");
            perSource.Add($"{source.SourceName} 실패");
            continue;
        }

        var kept = found.Where(settings.Matches).ToList();
        if (kept.Count != found.Count)
            Console.WriteLine($"  키워드 조건으로 {found.Count - kept.Count}건 제외");

        foreach (var job in kept)
            collected.TryAdd(job.Key, job);

        Console.WriteLine($"  {kept.Count}건");
        perSource.Add($"{source.SourceName} {kept.Count}건");
    }

    var matched = collected.Values.ToList();
    Console.WriteLine($"합계 {matched.Count}건 ({string.Join(", ", perSource)})");

    // 최초 발견일을 기록해 NEW 배지 판정에 쓴다. (지원 여부와는 무관)
    store.RecordSeen(matched, today);

    if (config.Fit.Enabled)
    {
        foreach (var job in matched)
            (job.FitScore, job.FitReasons) = config.Fit.Evaluate(job);

        var highlighted = matched.Count(j => j.FitScore >= config.Fit.HighlightThreshold);
        Console.WriteLine($"적합도 {config.Fit.HighlightThreshold}% 이상 {highlighted}건");
    }

    var appliedIds = store.LoadAppliedIds();
    var visible = matched
        .Where(j => !appliedIds.Contains(j.Key))
        // 사이트 안에서는 맞는 자리부터 보여 준다. 위에서부터 훑으면 되도록.
        .OrderBy(j => j.SourceName, StringComparer.CurrentCulture)
        .ThenByDescending(j => j.FitScore)
        .ThenByDescending(j => j.FirstSeen == today)
        .ThenBy(j => j.Company, StringComparer.CurrentCulture)
        .ThenBy(j => j.Title, StringComparer.CurrentCulture)
        .ToList();

    var excluded = matched.Count - visible.Count;
    var path = ReportGenerator.Write(reportDir, today, visible, matched.Count, excluded, config.ServerPort, config.Fit.HighlightThreshold);

    Console.WriteLine($"이미 지원한 공고 {excluded}건 제외 → 리포트 {visible.Count}건");
    Console.WriteLine($"리포트 생성: {path}");

    if (config.Email.Enabled)
        await SendReportMailAsync(today, visible, matched.Count, excluded, path);

    // 메일 실패로 리포트 생성까지 실패로 취급하지는 않는다.
    return 0;
}

/// <summary>config.json 에서 켜 둔 사이트의 크롤러를 만든다.</summary>
List<(IJobSource Source, SourceSettings Settings)> BuildSources()
{
    var sources = new List<(IJobSource, SourceSettings)>();
    var s = config.Sources;

    if (s.GameJob.Enabled)
        sources.Add((new GameJobCrawler(s.GameJob, config.RequestDelayMs), s.GameJob));

    if (s.Saramin.Enabled)
        sources.Add((new SaraminCrawler(s.Saramin, config.RequestDelayMs), s.Saramin));

    if (s.JobKorea.Enabled)
        sources.Add((new JobKoreaCrawler(s.JobKorea, config.RequestDelayMs), s.JobKorea));

    if (sources.Count == 0)
        Console.Error.WriteLine("켜져 있는 사이트가 없습니다. config.json 의 Sources 에서 Enabled 를 확인하세요.");

    return sources;
}

async Task SendReportMailAsync(
    DateOnly date, List<JobPosting> jobs, int totalCrawled, int excluded, string reportPath)
{
    var newCount = jobs.Count(j => j.FirstSeen == date);

    await ReportMailer.SendAsync(
        config.Email,
        root,
        EmailReportBuilder.Subject(date, jobs.Count, newCount),
        EmailReportBuilder.BuildHtml(date, jobs, totalCrawled, excluded, config.ServerPort),
        reportPath,
        cts.Token);
}

/// <summary>크롤링 없이 메일 설정만 확인한다.</summary>
async Task<int> TestEmailAsync()
{
    if (!config.Email.Enabled)
        Console.WriteLine("참고: config.json 의 Email.Enabled 가 false 입니다. 시험 발송은 그대로 진행합니다.");

    var today = DateOnly.FromDateTime(DateTime.Now);
    var sample = new List<JobPosting>
    {
        new()
        {
            SourceId = "gamejob",
            SourceName = "게임잡",
            Id = "0",
            Title = "메일 설정 확인용 예시 공고",
            Company = "JobCrawler",
            Url = $"http://localhost:{config.ServerPort}/",
            Duty = "서버",
            Career = "경력무관",
            Location = "서울 > 강남구",
            Deadline = "채용시",
            FirstSeen = today,
        },
    };

    var ok = await ReportMailer.SendAsync(
        config.Email,
        root,
        $"[게임잡] 메일 설정 확인 ({today:yyyy-MM-dd})",
        EmailReportBuilder.BuildHtml(today, sample, 1, 0, config.ServerPort),
        attachmentPath: null,
        cts.Token);

    if (ok) Console.WriteLine($"{config.Email.To} 의 받은편지함을 확인하세요.");
    return ok ? 0 : 1;
}

/// <summary>
/// 리포트 서버를 띄운다.
/// 콘솔에서 실행하면 콘솔 앱으로, 서비스 제어 관리자가 실행하면 서비스로 돈다.
/// </summary>
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

    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddSingleton(server);
    builder.Services.AddHostedService<ReportBackgroundService>();
    builder.Services.AddWindowsService(options => options.ServiceName = WindowsServiceInstaller.ServiceName);

    await builder.Build().RunAsync(cts.Token);
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
          test-email          크롤링 없이 메일 설정이 맞는지 시험 발송해 본다.

          install-schedule    매일 정해진 시각(config.json 의 ScheduleTime)에 crawl 을 돌리도록 등록한다.
          uninstall-schedule  위 작업을 해제한다.
          schedule-status     등록된 작업 상태를 본다.

          install-service     리포트 서버를 Windows 서비스로 등록한다. (관리자 권한 필요)
                              부팅 직후부터 떠 있어 체크가 항상 즉시 저장된다.
          uninstall-service   서비스를 해제한다. (관리자 권한 필요)
          service-status      서비스 상태를 본다.

        설정 파일: config.json  (사이트별 검색 조건, 포트, 실행 시각, 메일)
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
