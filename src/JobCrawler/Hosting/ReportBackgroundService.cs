using Microsoft.Extensions.Hosting;

namespace JobCrawler.Hosting;

/// <summary>
/// 리포트 서버를 호스트 수명주기에 얹는다.
///
/// 콘솔로 띄우면 그냥 콘솔 앱처럼 돌고, 서비스 제어 관리자(SCM)가 띄우면
/// UseWindowsService 가 서비스 규약을 대신 처리해 준다. 실행 코드는 같다.
/// </summary>
public sealed class ReportBackgroundService : BackgroundService
{
    private readonly ReportServer _server;
    private readonly IHostApplicationLifetime _lifetime;

    public ReportBackgroundService(ReportServer server, IHostApplicationLifetime lifetime)
    {
        _server = server;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startedCleanly = await _server.RunAsync(stoppingToken);

        // 포트를 못 잡았으면 계속 살아 있을 이유가 없다. 호스트를 내려서
        // 서비스가 '실행 중' 인 척하지 않게 한다.
        if (!startedCleanly && !stoppingToken.IsCancellationRequested)
            _lifetime.StopApplication();
    }
}
