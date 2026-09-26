using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace JobCrawler;

/// <summary>config.json 으로 조정 가능한 설정값.</summary>
public sealed class AppConfig
{
    /// <summary>사이트별 크롤링 설정.</summary>
    public SourcesSettings Sources { get; set; } = new();

    /// <summary>페이지 요청 사이 대기 시간(ms). 사이트에 부하를 주지 않기 위한 값.</summary>
    public int RequestDelayMs { get; set; } = 800;

    /// <summary>리포트 열람 서버가 사용할 로컬 포트.</summary>
    public int ServerPort { get; set; } = 8777;

    /// <summary>자동 실행 시각(install-schedule 이 등록하는 시각). HH:mm</summary>
    public string ScheduleTime { get; set; } = "20:00";

    /// <summary>리포트를 만든 뒤 메일로 보내는 설정.</summary>
    public EmailSettings Email { get; set; } = new();

    /// <summary>공고가 나에게 맞는 자리인지 채점하는 규칙.</summary>
    public FitSettings Fit { get; set; } = FitSettings.Default();

    /// <summary>지원 양식을 열어 첨부와 링크를 채워 두는 설정.</summary>
    public ApplySettings Apply { get; set; } = new();

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
        var migrated = MigrateLegacyShape(json, out var changed);

        var config = JsonSerializer.Deserialize<AppConfig>(migrated, JsonOpts) ?? new AppConfig();

        // 예전 모양이었다면 새 모양으로 다시 써 둔다.
        if (changed)
        {
            config.Save(path);
            Console.WriteLine("config.json 을 사이트별 설정(Sources) 구조로 옮겼습니다.");
        }

        return config;
    }

    /// <summary>
    /// 게임잡만 있던 시절의 config.json 은 DutyCodes 등을 최상위에 두었다.
    /// 그 모양이면 Sources.GameJob 아래로 옮겨 담는다.
    /// </summary>
    private static string MigrateLegacyShape(string json, out bool changed)
    {
        changed = false;

        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return json; }

        if (root is not JsonObject obj) return json;
        if (obj.ContainsKey("Sources")) return json;
        if (!obj.ContainsKey("DutyCodes")) return json;

        var gameJob = new JsonObject { ["Enabled"] = true };

        foreach (var key in new[] { "DutyCodes", "IncludeKeywords", "ExcludeKeywords", "MaxPages", "PageSize" })
        {
            if (!obj.TryGetPropertyValue(key, out var value) || value is null) continue;
            gameJob[key] = value.DeepClone();
            obj.Remove(key);
        }

        obj["Sources"] = new JsonObject { ["GameJob"] = gameJob };
        changed = true;
        return obj.ToJsonString(JsonOpts);
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
    }
}
