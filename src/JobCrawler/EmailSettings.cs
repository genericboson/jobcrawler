namespace JobCrawler;

/// <summary>
/// 리포트 메일 발송 설정.
///
/// 비밀번호는 config.json 에 두지 않는다. 이 파일은 저장소에 올라가므로
/// 환경변수(기본 JOBCRAWLER_SMTP_PASSWORD) 나 별도 파일에서만 읽는다.
/// </summary>
public sealed class EmailSettings
{
    /// <summary>리포트를 만든 뒤 메일을 보낼지 여부.</summary>
    public bool Enabled { get; set; }

    /// <summary>받는 사람 주소.</summary>
    public string To { get; set; } = "";

    /// <summary>보내는 사람 주소. 대부분 SMTP 계정 자신의 주소여야 한다.</summary>
    public string From { get; set; } = "";

    public string SmtpHost { get; set; } = "smtp.naver.com";
    public int SmtpPort { get; set; } = 587;

    /// <summary>true 면 587 포트 STARTTLS, false 면 465 포트 암시적 SSL 로 접속한다.</summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>SMTP 로그인 아이디. 네이버는 메일 주소의 @ 앞부분.</summary>
    public string UserName { get; set; } = "";

    /// <summary>비밀번호를 담은 환경변수 이름.</summary>
    public string PasswordEnvVar { get; set; } = "JOBCRAWLER_SMTP_PASSWORD";

    /// <summary>
    /// 환경변수 대신 쓸 비밀번호 파일 경로. 상대 경로면 프로젝트 루트 기준.
    /// 비워두면 환경변수만 본다.
    /// </summary>
    public string PasswordFile { get; set; } = "";

    /// <summary>메일에 리포트 HTML 파일을 첨부할지 여부.</summary>
    public bool AttachReport { get; set; } = true;

    /// <summary>
    /// 설정한 곳에서 비밀번호를 읽는다. 환경변수를 먼저 보고, 없으면 파일을 본다.
    /// </summary>
    public string? ResolvePassword(string root)
    {
        if (!string.IsNullOrWhiteSpace(PasswordEnvVar))
        {
            var fromEnv = Environment.GetEnvironmentVariable(PasswordEnvVar);
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();
        }

        if (!string.IsNullOrWhiteSpace(PasswordFile))
        {
            var path = Path.IsPathRooted(PasswordFile) ? PasswordFile : Path.Combine(root, PasswordFile);
            if (File.Exists(path))
            {
                var fromFile = File.ReadAllText(path).Trim();
                if (fromFile.Length > 0) return fromFile;
            }
        }

        return null;
    }

    /// <summary>보내기 전에 빠진 설정이 있는지 확인한다. 문제가 없으면 빈 목록.</summary>
    public List<string> Validate(string root)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(To)) problems.Add("Email.To (받는 주소)");
        if (string.IsNullOrWhiteSpace(From)) problems.Add("Email.From (보내는 주소)");
        if (string.IsNullOrWhiteSpace(SmtpHost)) problems.Add("Email.SmtpHost");
        if (string.IsNullOrWhiteSpace(UserName)) problems.Add("Email.UserName (SMTP 로그인 아이디)");

        if (ResolvePassword(root) is null)
        {
            problems.Add(string.IsNullOrWhiteSpace(PasswordFile)
                ? $"비밀번호 (환경변수 {PasswordEnvVar})"
                : $"비밀번호 (환경변수 {PasswordEnvVar} 또는 파일 {PasswordFile})");
        }

        return problems;
    }
}
