using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace JobCrawler.Hosting;

/// <summary>
/// 리포트 서버를 Windows 서비스로 등록/해제한다.
///
/// 서비스로 두면 로그인하지 않아도 부팅 직후부터 서버가 떠 있어
/// 리포트의 체크박스가 언제나 즉시 저장된다.
/// 서비스 등록은 관리자 권한이 필요하다.
/// </summary>
public static class WindowsServiceInstaller
{
    public const string ServiceName = "JobCrawlerReportServer";
    private const string DisplayName = "게임잡 리포트 서버";
    private const string Description =
        "게임잡 일일 리포트의 체크박스를 oldjoblist.json 에 기록하는 로컬 서버입니다.";

    public static int Install(string exePath, int port)
    {
        if (!File.Exists(exePath))
        {
            Console.Error.WriteLine($"실행 파일을 찾을 수 없습니다: {exePath}");
            Console.Error.WriteLine("먼저 다음을 실행하세요:  dotnet publish src/JobCrawler -c Release -o app");
            return 1;
        }

        if (!IsElevated())
        {
            PrintElevationHelp("install-service");
            return 1;
        }

        // sc.exe 는 'binPath= "값"' 처럼 등호 뒤에 공백이 있어야 한다.
        var binPath = $"\\\"{exePath}\\\" serve";

        var exit = Run("sc.exe",
            $"create {ServiceName} binPath= \"{binPath}\" start= auto DisplayName= \"{DisplayName}\"",
            out var output);
        Console.WriteLine(output.Trim());

        if (exit != 0)
        {
            Console.Error.WriteLine("서비스를 만들지 못했습니다.");
            return exit;
        }

        Run("sc.exe", $"description {ServiceName} \"{Description}\"", out _);

        // 서버가 죽어도 스스로 되살아나게 한다. 5초, 5초, 그 뒤로는 1분 간격.
        Run("sc.exe",
            $"failure {ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/60000",
            out _);

        var startExit = Run("sc.exe", $"start {ServiceName}", out var startOutput);
        Console.WriteLine(startOutput.Trim());

        Console.WriteLine();
        if (startExit == 0)
        {
            Console.WriteLine($"'{DisplayName}' 서비스가 등록되어 실행 중입니다.");
            Console.WriteLine($"  부팅할 때 자동으로 뜨고, 죽으면 스스로 다시 뜹니다.");
            Console.WriteLine($"  리포트 주소: http://localhost:{port}/");
        }
        else
        {
            Console.Error.WriteLine("서비스는 등록됐지만 시작하지 못했습니다.");
            Console.Error.WriteLine($"확인:  sc.exe query {ServiceName}");
        }

        Console.WriteLine();
        Console.WriteLine($"해제:  JobCrawler uninstall-service   (관리자 권한 필요)");
        return startExit;
    }

    public static int Uninstall()
    {
        if (!IsElevated())
        {
            PrintElevationHelp("uninstall-service");
            return 1;
        }

        Run("sc.exe", $"stop {ServiceName}", out var stopOutput);
        Console.WriteLine(stopOutput.Trim());

        // 정지 요청 직후에는 아직 STOP_PENDING 이라 삭제가 거부될 수 있다.
        WaitUntilStopped(TimeSpan.FromSeconds(15));

        var exit = Run("sc.exe", $"delete {ServiceName}", out var output);
        Console.WriteLine(output.Trim());
        return exit;
    }

    public static int Status()
    {
        var exit = Run("sc.exe", $"query {ServiceName}", out var output);
        Console.WriteLine(output.Trim());

        if (exit != 0)
        {
            Console.WriteLine();
            Console.WriteLine("등록된 서비스가 없습니다.");
            Console.WriteLine("관리자 명령 프롬프트에서 JobCrawler install-service 를 실행하세요.");
        }

        return exit;
    }

    private static void WaitUntilStopped(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Run("sc.exe", $"query {ServiceName}", out var output);
            if (!output.Contains("STOP_PENDING", StringComparison.OrdinalIgnoreCase)) return;
            Thread.Sleep(500);
        }
    }

    public static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void PrintElevationHelp(string command)
    {
        Console.Error.WriteLine("서비스를 다루려면 관리자 권한이 필요합니다.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("시작 메뉴에서 '명령 프롬프트' 를 오른쪽 클릭 > '관리자 권한으로 실행' 한 뒤,");
        Console.Error.WriteLine("아래 두 줄을 실행하세요.");
        Console.Error.WriteLine();
        Console.Error.WriteLine($"  cd /d {AppContext.BaseDirectory.TrimEnd('\\')}");
        Console.Error.WriteLine($"  JobCrawler.exe {command}");
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
