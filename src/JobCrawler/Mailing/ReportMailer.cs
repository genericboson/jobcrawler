using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace JobCrawler.Mailing;

/// <summary>리포트를 메일로 보낸다.</summary>
public static class ReportMailer
{
    /// <summary>
    /// 메일을 보낸다. 보냈으면 true, 설정이 모자라거나 실패하면 false.
    /// 크롤링과 리포트 생성은 이미 끝난 뒤이므로, 메일이 실패해도 예외를 밖으로 던지지 않는다.
    /// </summary>
    public static async Task<bool> SendAsync(
        EmailSettings settings,
        string root,
        string subject,
        string htmlBody,
        string? attachmentPath,
        CancellationToken ct)
    {
        var problems = settings.Validate(root);
        var password = settings.ResolvePassword(root);

        if (problems.Count > 0 || password is null)
        {
            Console.Error.WriteLine("메일을 보내지 못했습니다. 다음 설정이 비어 있습니다:");
            foreach (var problem in problems)
                Console.Error.WriteLine($"  - {problem}");
            Console.Error.WriteLine("자세한 설정 방법은 README.md 의 '리포트 메일로 받기' 를 보세요.");
            return false;
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(settings.From));
        message.To.Add(MailboxAddress.Parse(settings.To));
        message.Subject = subject;

        var builder = new BodyBuilder { HtmlBody = htmlBody };

        if (settings.AttachReport && attachmentPath is not null && File.Exists(attachmentPath))
            await builder.Attachments.AddAsync(attachmentPath, ct);

        message.Body = builder.ToMessageBody();

        try
        {
            using var client = new SmtpClient();

            var security = settings.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.SslOnConnect;

            await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, security, ct);
            await client.AuthenticateAsync(settings.UserName, password, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            Console.WriteLine($"메일 발송: {settings.To}");
            return true;
        }
        catch (AuthenticationException ex)
        {
            Console.Error.WriteLine($"SMTP 로그인 실패: {ex.Message}");
            Console.Error.WriteLine("아이디/비밀번호와, 메일 서비스에서 SMTP 사용이 켜져 있는지 확인하세요.");
            Console.Error.WriteLine("네이버는 [메일 > 환경설정 > POP3/IMAP 설정] 에서 IMAP/SMTP 를 '사용함' 으로 두어야 합니다.");
            return false;
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException or IOException
                                      or System.Net.Sockets.SocketException)
        {
            Console.Error.WriteLine($"메일 발송 실패: {ex.Message}");
            return false;
        }
    }
}
