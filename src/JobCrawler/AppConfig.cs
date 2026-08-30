using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobCrawler;

/// <summary>config.json 으로 조정 가능한 설정값.</summary>
public sealed class AppConfig
{
    /// <summary>
    /// 크롤링할 게임잡 직무(duty) 코드. 16 = 기술지원 &gt; 서버.
    /// 참고: 1=게임개발(클라이언트), 2=게임개발(모바일), 17=네트워크, 18=엔진, 19=시스템·DB, 21=클라우드
    /// </summary>
    public List<int> DutyCodes { get; set; } = new() { 16 };

    /// <summary>
    /// 제목/직무에 이 단어 중 하나라도 있어야 리포트에 포함. 비워두면 직무 코드 결과를 전부 포함.
    /// </summary>
    public List<string> IncludeKeywords { get; set; } = new();

    /// <summary>제목에 이 단어가 있으면 제외.</summary>
    public List<string> ExcludeKeywords { get; set; } = new();

    /// <summary>읽어올 최대 페이지 수(안전장치).</summary>
    public int MaxPages { get; set; } = 20;

    /// <summary>한 페이지에 받아올 공고 수. 게임잡이 받아들이는 범위는 20~100.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>페이지 요청 사이 대기 시간(ms). 서버 부하를 주지 않기 위한 값.</summary>
    public int RequestDelayMs { get; set; } = 800;

    /// <summary>리포트 열람 서버(serve 명령)가 사용할 로컬 포트.</summary>
    public int ServerPort { get; set; } = 8777;

    /// <summary>자동 실행 시각(install-schedule 이 등록하는 시각). HH:mm</summary>
    public string ScheduleTime { get; set; } = "20:00";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = true,
    };

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var fresh = new AppConfig();
            fresh.Save(path);
            return fresh;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new AppConfig();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
    }
}
