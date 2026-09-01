using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace JobCrawler.Scheduling;

/// <summary>
/// schtasks 로 매일 정해진 시각의 리포트 생성 작업을 등록/해제한다.
///
/// 리포트 서버는 예전엔 로그온 작업으로 띄웠지만 지금은 Windows 서비스가 맡는다.
/// Uninstall 은 예전에 등록된 로그온 작업도 함께 지워, 서버가 둘 뜨는 일을 막는다.
/// </summary>
public static class WindowsSchedule
{
    public const string DailyTaskName = "GameJobCrawler_DailyReport";
    public const string ServerTaskName = "GameJobCrawler_ReportServer";

    private static readonly XNamespace Ns =
        "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static string CurrentUser =>
        $@"{Environment.UserDomainName}\{Environment.UserName}";

    // ---------------------------------------------------------------- 일일 리포트

    public static int InstallDaily(string exePath, string workingDirectory, string time)
    {
        if (!EnsureExe(exePath)) return 1;

        // schtasks 의 /TR 옵션은 세부 설정을 건드릴 수 없어 XML 로 등록한다.
        // 특히 노트북 배터리 사용 중에도 돌아야 하고, PC 가 꺼져 있어 시각을 놓쳤다면
        // 다음에 켰을 때라도 리포트가 만들어져야 한다.
        var trigger = new XElement(Ns + "CalendarTrigger",
            new XElement(Ns + "StartBoundary", $"{DateTime.Today:yyyy-MM-dd}T{time}:00"),
            new XElement(Ns + "Enabled", true),
            new XElement(Ns + "ScheduleByDay",
                new XElement(Ns + "DaysInterval", 1)));

        var xml = BuildTaskXml(
            description: "게임잡 서버 프로그래머 공고 일일 리포트 생성",
            trigger: trigger,
            command: exePath,
            arguments: "crawl",
            workingDirectory: workingDirectory,
            executionTimeLimit: "PT30M",
            requireNetwork: true);

        var exit = Register(DailyTaskName, xml);
        if (exit != 0) return exit;

        Console.WriteLine();
        Console.WriteLine($"매일 {time} 에 '{DailyTaskName}' 작업이 리포트를 만듭니다.");
        Console.WriteLine($"  실행 파일: {exePath}");
        Console.WriteLine($"  시작 위치: {workingDirectory}");
        Console.WriteLine();
        Console.WriteLine($"즉시 실행해 보기: schtasks /Run /TN {DailyTaskName}");
        return 0;
    }

    // ---------------------------------------------------------------- 공통

    public static int Uninstall()
    {
        var daily = Run("schtasks", $"/Delete /TN {DailyTaskName} /F", out var dailyOut);
        Console.WriteLine(dailyOut.Trim());

        // 예전 버전이 등록해 둔 로그온 작업이 남아 있으면 함께 지운다.
        Run("schtasks", $"/Delete /TN {ServerTaskName} /F", out _);

        return daily;
    }

    public static int Status()
    {
        var found = false;

        foreach (var name in new[] { DailyTaskName, ServerTaskName })
        {
            Console.WriteLine($"--- {name} ---");
            var exit = Run("schtasks", $"/Query /TN {name} /V /FO LIST", out var output);
            Console.WriteLine(output.Trim());
            Console.WriteLine();
            if (exit == 0) found = true;
        }

        if (!found)
            Console.WriteLine("등록된 작업이 없습니다. JobCrawler install-schedule 로 등록하세요.");

        return found ? 0 : 1;
    }

    private static bool EnsureExe(string exePath)
    {
        if (File.Exists(exePath)) return true;

        Console.Error.WriteLine($"실행 파일을 찾을 수 없습니다: {exePath}");
        Console.Error.WriteLine("먼저 다음을 실행해 실행 파일을 만드세요:");
        Console.Error.WriteLine("  dotnet publish src/JobCrawler -c Release -o app");
        return false;
    }

    private static int Register(string taskName, string xml)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"{taskName}.xml");
        File.WriteAllText(xmlPath, xml, Encoding.Unicode);

        try
        {
            var exit = Run("schtasks", $"/Create /TN {taskName} /XML \"{xmlPath}\" /F", out var output);
            Console.WriteLine(output.Trim());
            return exit;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch (IOException) { }
        }
    }

    private static string BuildTaskXml(
        string description,
        XElement trigger,
        string command,
        string arguments,
        string workingDirectory,
        string executionTimeLimit,
        bool requireNetwork)
    {
        var settings = new XElement(Ns + "Settings",
            // 노트북에서 배터리로 쓰는 중에도 실행되게 한다.
            new XElement(Ns + "DisallowStartIfOnBatteries", false),
            new XElement(Ns + "StopIfGoingOnBatteries", false),
            // 시각을 놓쳤다면 다음에 켰을 때 만회 실행한다.
            new XElement(Ns + "StartWhenAvailable", true),
            new XElement(Ns + "RunOnlyIfNetworkAvailable", requireNetwork),
            new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
            new XElement(Ns + "AllowHardTerminate", true),
            new XElement(Ns + "ExecutionTimeLimit", executionTimeLimit),
            new XElement(Ns + "Enabled", true),
            new XElement(Ns + "Hidden", false),
            new XElement(Ns + "IdleSettings",
                new XElement(Ns + "StopOnIdleEnd", false),
                new XElement(Ns + "RestartOnIdle", false)));

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(Ns + "Task",
                new XAttribute("version", "1.2"),
                new XElement(Ns + "RegistrationInfo",
                    new XElement(Ns + "Description", description)),
                new XElement(Ns + "Triggers", trigger),
                new XElement(Ns + "Principals",
                    new XElement(Ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(Ns + "UserId", CurrentUser),
                        new XElement(Ns + "LogonType", "InteractiveToken"),
                        new XElement(Ns + "RunLevel", "LeastPrivilege"))),
                settings,
                new XElement(Ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(Ns + "Exec",
                        new XElement(Ns + "Command", command),
                        new XElement(Ns + "Arguments", arguments),
                        new XElement(Ns + "WorkingDirectory", workingDirectory)))));

        return doc.Declaration + Environment.NewLine + doc;
    }

    private static int Run(string file, string arguments, out string output)
    {
        var psi = new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        output = string.IsNullOrWhiteSpace(stderr) ? stdout : stdout + Environment.NewLine + stderr;
        return process.ExitCode;
    }
}
