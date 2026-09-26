namespace JobCrawler;

/// <summary>
/// 지원 준비 설정.
///
/// 이 프로그램은 지원서를 대신 제출하지 않는다. 브라우저를 띄워 지원 양식까지 열고
/// 첨부 파일과 포트폴리오 링크를 채워 둔 뒤 멈춘다. 제출 버튼은 사람이 누른다.
/// </summary>
public sealed class ApplySettings
{
    /// <summary>false 면 리포트에 '지원 준비' 버튼이 나오지 않는다.</summary>
    public bool Enabled { get; set; }

    /// <summary>이력서·경력기술서 첨부란에 올릴 파일.</summary>
    public string ResumeFile { get; set; } = "";

    /// <summary>포트폴리오 링크란이 없고 파일만 받는 곳에 올릴 파일.</summary>
    public string PortfolioFile { get; set; } = "";

    /// <summary>포트폴리오 링크란에 써 넣을 주소.</summary>
    public string PortfolioUrl { get; set; } = "";

    /// <summary>
    /// 로그인 상태를 담아 둘 브라우저 프로필 폴더. 상대 경로면 프로젝트 루트 기준.
    /// 평소 쓰는 크롬 프로필과 섞이지 않게 따로 둔다. 처음 한 번 직접 로그인하면
    /// 이후로는 이 폴더에 세션이 남아 그대로 쓰인다.
    /// </summary>
    public string BrowserProfileDir { get; set; } = "secrets/browser-profile";

    /// <summary>
    /// 띄울 브라우저. "chrome" 이면 이미 깔려 있는 크롬을 쓴다.
    /// 비워 두면 Playwright 가 받아 둔 크로미움을 쓴다.
    /// </summary>
    public string BrowserChannel { get; set; } = "chrome";

    public string ResolvePath(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        return Path.IsPathRooted(path) ? path : Path.Combine(root, path);
    }

    /// <summary>빠진 설정이나 없는 파일을 찾아낸다. 문제가 없으면 빈 목록.</summary>
    public List<string> Validate(string root)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(ResumeFile))
            problems.Add("Apply.ResumeFile (이력서·경력기술서 파일)");
        else if (!File.Exists(ResolvePath(root, ResumeFile)))
            problems.Add($"이력서 파일을 찾을 수 없음: {ResolvePath(root, ResumeFile)}");

        if (!string.IsNullOrWhiteSpace(PortfolioFile) &&
            !File.Exists(ResolvePath(root, PortfolioFile)))
            problems.Add($"포트폴리오 파일을 찾을 수 없음: {ResolvePath(root, PortfolioFile)}");

        return problems;
    }
}
