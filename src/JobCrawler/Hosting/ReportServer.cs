using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobCrawler.Models;
using JobCrawler.Storage;

namespace JobCrawler.Hosting;

/// <summary>
/// 리포트를 열람하고 체크박스 결과를 oldjoblist 에 반영하는 로컬 전용 웹 서버.
///
/// 브라우저는 파일을 직접 쓸 수 없으므로, 리포트 페이지의 체크박스가
/// 여기로 요청을 보내면 이 서버가 oldjoblist.json 을 갱신한다.
/// localhost 에만 바인딩하고 외부 요청은 받지 않는다.
/// </summary>
public sealed class ReportServer
{
    private readonly string _reportDirectory;
    private readonly JobStore _store;
    private readonly int _port;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public ReportServer(string reportDirectory, JobStore store, int port)
    {
        _reportDirectory = reportDirectory;
        _store = store;
        _port = port;
    }

    public string RootUrl => $"http://localhost:{_port}/";

    public async Task RunAsync(CancellationToken ct)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(RootUrl);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            Console.Error.WriteLine($"포트 {_port} 를 열지 못했습니다: {ex.Message}");
            Console.Error.WriteLine("다른 프로그램이 포트를 쓰고 있다면 config.json 의 ServerPort 를 바꾸세요.");
            Console.Error.WriteLine($"권한 문제라면 관리자 명령 프롬프트에서 다음을 한 번 실행하세요:");
            Console.Error.WriteLine($"  netsh http add urlacl url=http://localhost:{_port}/ user=%USERNAME%");
            throw;
        }

        Console.WriteLine($"리포트 서버 실행 중: {RootUrl}");
        Console.WriteLine("종료하려면 Ctrl+C 를 누르세요.");

        using var ctRegistration = ct.Register(() => { try { listener.Stop(); } catch (ObjectDisposedException) { } });

        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }

            // 요청 하나가 실패해도 서버는 계속 돌아야 한다.
            _ = Task.Run(async () =>
            {
                try { await HandleAsync(context); }
                catch (Exception ex) { Console.Error.WriteLine($"요청 처리 실패: {ex.Message}"); }
            }, CancellationToken.None);
        }

        Console.WriteLine("리포트 서버를 종료했습니다.");
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // file:// 로 연 리포트에서도 요청할 수 있어야 하므로 CORS 를 허용한다.
        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.AddHeader("Access-Control-Allow-Headers", "Content-Type");
        response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");

        if (request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = 204;
            response.Close();
            return;
        }

        // 로컬에서 온 요청만 처리한다.
        if (request.RemoteEndPoint is not null && !IPAddress.IsLoopback(request.RemoteEndPoint.Address))
        {
            await WriteTextAsync(response, 403, "text/plain", "로컬에서만 접근할 수 있습니다.");
            return;
        }

        var path = request.Url?.AbsolutePath ?? "/";

        switch (path)
        {
            case "/api/applied" when request.HttpMethod == "GET":
                await WriteJsonAsync(response, 200, _store.LoadApplied().Select(a => a.Id).ToList());
                return;

            case "/api/applied" when request.HttpMethod == "POST":
                await HandleAppliedPostAsync(request, response);
                return;

            case "/":
                await WriteIndexAsync(response);
                return;

            default:
                await ServeReportFileAsync(response, path);
                return;
        }
    }

    private async Task HandleAppliedPostAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
        var body = await reader.ReadToEndAsync();

        AppliedRequest? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AppliedRequest>(body, JsonOpts);
        }
        catch (JsonException)
        {
            await WriteJsonAsync(response, 400, new { error = "본문을 JSON 으로 읽을 수 없습니다." });
            return;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Id))
        {
            await WriteJsonAsync(response, 400, new { error = "id 가 필요합니다." });
            return;
        }

        if (payload.Applied)
        {
            var added = _store.MarkApplied(new AppliedJob
            {
                Id = payload.Id,
                Title = payload.Title ?? "",
                Company = payload.Company ?? "",
                Url = payload.Url ?? "",
                AppliedAt = DateTimeOffset.Now,
            });
            Console.WriteLine(added
                ? $"[지원함] {payload.Company} · {payload.Title} ({payload.Id})"
                : $"[이미 등재] {payload.Id}");
        }
        else
        {
            var removed = _store.UnmarkApplied(payload.Id);
            Console.WriteLine(removed
                ? $"[지원 취소] {payload.Company} · {payload.Title} ({payload.Id})"
                : $"[목록에 없음] {payload.Id}");
        }

        await WriteJsonAsync(response, 200, new { ok = true, id = payload.Id, applied = payload.Applied });
    }

    /// <summary>날짜별 리포트 목록 페이지.</summary>
    private async Task WriteIndexAsync(HttpListenerResponse response)
    {
        var files = Directory.Exists(_reportDirectory)
            ? Directory.GetFiles(_reportDirectory, "*.html")
                .Select(Path.GetFileName)
                .Where(n => n is not null)
                .OrderByDescending(n => n, StringComparer.Ordinal)
                .ToList()
            : new List<string?>();

        // 가장 최근 리포트가 있으면 바로 그리로 보낸다.
        if (files.Count > 0)
        {
            response.StatusCode = 302;
            response.AddHeader("Location", "/" + files[0]);
            response.Close();
            return;
        }

        await WriteTextAsync(response, 200, "text/html; charset=utf-8", """
            <!DOCTYPE html><html lang="ko"><meta charset="utf-8">
            <title>게임잡 리포트</title>
            <body style="font-family:sans-serif;padding:40px">
            <h1>아직 리포트가 없습니다</h1>
            <p><code>JobCrawler crawl</code> 을 먼저 실행하세요.</p>
            </body></html>
            """);
    }

    private async Task ServeReportFileAsync(HttpListenerResponse response, string path)
    {
        var name = Path.GetFileName(Uri.UnescapeDataString(path));

        // 경로 탈출 방지: 파일 이름만 받아들이고 리포트 폴더 안인지 다시 확인한다.
        if (string.IsNullOrEmpty(name) || !name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            await WriteTextAsync(response, 404, "text/plain; charset=utf-8", "없는 경로입니다.");
            return;
        }

        var full = Path.GetFullPath(Path.Combine(_reportDirectory, name));
        var root = Path.GetFullPath(_reportDirectory);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(full))
        {
            await WriteTextAsync(response, 404, "text/plain; charset=utf-8", "리포트를 찾을 수 없습니다.");
            return;
        }

        var bytes = await File.ReadAllBytesAsync(full);
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int status, object payload) =>
        await WriteTextAsync(response, status, "application/json; charset=utf-8",
            JsonSerializer.Serialize(payload, JsonOpts));

    private static async Task WriteTextAsync(
        HttpListenerResponse response, int status, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        response.StatusCode = status;
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private sealed class AppliedRequest
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("company")] public string? Company { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("applied")] public bool Applied { get; set; }
    }
}
