using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace JobCrawler.Scheduling;

/// <summary>schtasks 로 Windows 작업 스케줄러에 매일 실행 작업을 등록/해제한다.</summary>
public static class WindowsSchedule
{
    public const string TaskName = "GameJobCrawler_DailyReport";

    private static readonly XNamespace Ns =
        "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static int Install(string exePath, string workingDirectory, string time)
    {
        if (!File.Exists(exePath))
        {
            Console.Error.WriteLine($"실행 파일을 찾을 수 없습니다: {exePath}");
            Console.Error.WriteLine("먼저 다음을 실행해 실행 파일을 만드세요:");
            Console.Error.WriteLine("  dotnet publish src/JobCrawler -c Release -o app");
            return 1;
        }

        // schtasks 의 /TR 옵션은 세부 설정을 건드릴 수 없어 XML 로 등록한다.
        // 특히 노트북 배터리 사용 중에도 돌아야 하고, PC 가 꺼져 있어 시각을 놓쳤다면
        // 다음에 켰을 때라도 리포트가 만들어져야 한다.
        var xmlPath = Path.Combine(Path.GetTempPath(), $"{TaskName}.xml");
        File.WriteAllText(xmlPath, BuildTaskXml(exePath, workingDirectory, time), Encoding.Unicode);

        try
        {
            var exit = Run("schtasks", $"/Create /TN {TaskName} /XML \"{xmlPath}\" /F", out var output);
            Console.WriteLine(output.Trim());

            if (exit != 0) return exit;

            Console.WriteLine();
            Console.WriteLine($"매일 {time} 에 '{TaskName}' 작업이 실행됩니다.");
            Console.WriteLine($"  실행 파일: {exePath}");
            Console.WriteLine($"  시작 위치: {workingDirectory}");
            Console.WriteLine();
            Console.WriteLine($"즉시 실행해 보기: schtasks /Run /TN {TaskName}");
            Console.WriteLine($"해제:             JobCrawler uninstall-schedule");
            return 0;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch (IOException) { }
        }
    }

    public static int Uninstall()
    {
        var exit = Run("schtasks", $"/Delete /TN {TaskName} /F", out var output);
        Console.WriteLine(output.Trim());
        return exit;
    }

    public static int Status()
    {
        var exit = Run("schtasks", $"/Query /TN {TaskName} /V /FO LIST", out var output);
        Console.WriteLine(output.Trim());
        if (exit != 0) Console.WriteLine("등록된 작업이 없습니다. JobCrawler install-schedule 로 등록하세요.");
        return exit;
    }

    private static string BuildTaskXml(string exePath, string workingDirectory, string time)
    {
        // StartBoundary 의 날짜 부분은 무시되지만 형식상 필요하다.
        var start = $"{DateTime.Today:yyyy-MM-dd}T{time}:00";

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(Ns + "Task",
                new XAttribute("version", "1.2"),
                new XElement(Ns + "RegistrationInfo",
                    new XElement(Ns + "Description", "게임잡 서버 프로그래머 공고 일일 리포트 생성")),
                new XElement(Ns + "Triggers",
                    new XElement(Ns + "CalendarTrigger",
                        new XElement(Ns + "StartBoundary", start),
                        new XElement(Ns + "Enabled", true),
                        new XElement(Ns + "ScheduleByDay",
                            new XElement(Ns + "DaysInterval", 1)))),
                new XElement(Ns + "Principals",
                    new XElement(Ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(Ns + "LogonType", "InteractiveToken"),
                        new XElement(Ns + "RunLevel", "LeastPrivilege"))),
                new XElement(Ns + "Settings",
                    // 노트북에서 배터리로 쓰는 중에도 실행되게 한다.
                    new XElement(Ns + "DisallowStartIfOnBatteries", false),
                    new XElement(Ns + "StopIfGoingOnBatteries", false),
                    // 20시에 PC 가 꺼져 있었다면 다음에 켰을 때 만회 실행한다.
                    new XElement(Ns + "StartWhenAvailable", true),
                    new XElement(Ns + "RunOnlyIfNetworkAvailable", true),
                    new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(Ns + "AllowHardTerminate", true),
                    new XElement(Ns + "ExecutionTimeLimit", "PT30M"),
                    new XElement(Ns + "Enabled", true),
                    new XElement(Ns + "Hidden", false),
                    new XElement(Ns + "IdleSettings",
                        new XElement(Ns + "StopOnIdleEnd", false),
                        new XElement(Ns + "RestartOnIdle", false))),
                new XElement(Ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(Ns + "Exec",
                        new XElement(Ns + "Command", exePath),
                        new XElement(Ns + "Arguments", "crawl"),
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
