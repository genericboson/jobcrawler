using Microsoft.Win32;

namespace JobCrawler.Applying;

/// <summary>
/// jobcrawler:// 링크를 이 프로그램이 받도록 등록한다.
///
/// 리포트 서버는 Windows 서비스(세션 0)로 돌아 화면에 브라우저를 띄울 수 없다.
/// 대신 리포트의 '지원 준비' 를 링크로 만들어 두면, 브라우저에서 누를 때
/// Windows 가 로그인된 세션에서 이 프로그램을 띄워 준다.
///
/// HKCU 아래에만 쓰므로 관리자 권한이 필요 없다.
/// </summary>
public static class UrlProtocol
{
    public const string Scheme = "jobcrawler";

    private const string KeyPath = $@"Software\Classes\{Scheme}";

    public static int Install(string exePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Windows 에서만 등록할 수 있습니다.");
            return 1;
        }

        if (!File.Exists(exePath))
        {
            Console.Error.WriteLine($"실행 파일을 찾을 수 없습니다: {exePath}");
            Console.Error.WriteLine("먼저 scripts/Update-App.ps1 로 게시하세요.");
            return 1;
        }

        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue("", $"URL:{Scheme}");
        key.SetValue("URL Protocol", "");

        using var icon = key.CreateSubKey("DefaultIcon");
        icon.SetValue("", $"\"{exePath}\",0");

        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{exePath}\" apply \"%1\"");

        Console.WriteLine($"{Scheme}:// 링크를 이 프로그램이 받도록 등록했습니다.");
        Console.WriteLine($"  실행 파일: {exePath}");
        Console.WriteLine();
        Console.WriteLine("리포트에서 '지원 준비' 를 누르면 브라우저가 한 번 확인을 묻습니다.");
        Console.WriteLine("'항상 허용' 을 체크하면 다음부터 묻지 않습니다.");
        Console.WriteLine();
        Console.WriteLine($"해제: JobCrawler uninstall-protocol");
        return 0;
    }

    public static int Uninstall()
    {
        if (!OperatingSystem.IsWindows()) return 1;

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath);
            Console.WriteLine($"{Scheme}:// 등록을 해제했습니다.");
            return 0;
        }
        catch (ArgumentException)
        {
            Console.WriteLine("등록되어 있지 않습니다.");
            return 0;
        }
    }

    /// <summary>
    /// jobcrawler://apply?url=... 에서 공고 주소를 꺼낸다.
    /// 브라우저가 넘겨주는 값이므로 http/https 인지 반드시 확인한다.
    /// </summary>
    public static string? ParsePostingUrl(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument)) return null;

        // 프로토콜을 거치지 않고 공고 주소를 바로 넘긴 경우도 받아 준다.
        if (argument.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return argument;

        if (!Uri.TryCreate(argument, UriKind.Absolute, out var uri)) return null;
        if (!uri.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase)) return null;

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var target = query["url"];
        if (string.IsNullOrWhiteSpace(target)) return null;

        if (!Uri.TryCreate(target, UriKind.Absolute, out var posting)) return null;
        if (posting.Scheme != Uri.UriSchemeHttp && posting.Scheme != Uri.UriSchemeHttps) return null;

        return posting.ToString();
    }
}
