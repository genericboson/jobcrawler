using JobCrawler.Models;

namespace JobCrawler;

/// <summary>적합도 판정 규칙 하나.</summary>
public sealed class FitRule
{
    /// <summary>근거에 표시할 이름. 예) "C++ 사용"</summary>
    public string Label { get; set; } = "";

    /// <summary>이 중 하나라도 제목·직무·기술에 있으면 맞은 것으로 본다.</summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>
    /// 양수면 맞았을 때 점수를 얻고, 음수면 맞았을 때 점수를 깎는다.
    /// 최종 점수는 (얻은 양수 배점 / 전체 양수 배점) × 100 에서 감점을 뺀 값이다.
    /// </summary>
    public int Weight { get; set; }
}

/// <summary>공고 하나에 대한 규칙 적용 결과.</summary>
public sealed class FitReason
{
    public string Label { get; set; } = "";
    public int Weight { get; set; }
    public bool Matched { get; set; }

    /// <summary>실제로 맞은 단어. 왜 맞았는지 보여주는 데 쓴다.</summary>
    public string? MatchedOn { get; set; }
}

/// <summary>
/// 공고가 나에게 맞는 자리인지 규칙으로 채점한다.
///
/// 목록에 실린 제목·직무·기술만 보고 판단한다. 공고 본문은 읽지 않으므로
/// 본문에만 적힌 조건은 반영되지 않는다. 점수는 '훑어볼 순서'를 정해 주는
/// 값이지 합격 가능성이 아니다.
/// </summary>
public sealed class FitSettings
{
    /// <summary>false 면 점수를 매기지 않고 리포트에도 표시하지 않는다.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>이 점수 이상이면 '눈에 띄게' 칠한다.</summary>
    public int HighlightThreshold { get; set; } = 60;

    public List<FitRule> Rules { get; set; } = new();

    /// <summary>공고를 채점하고 근거를 함께 돌려준다.</summary>
    public (int Score, List<FitReason> Reasons) Evaluate(JobPosting job)
    {
        var haystack = $"{job.Title} {job.Duty} {job.Tech} {job.Career} {job.Location} {job.EmploymentType}";

        var reasons = new List<FitReason>();
        var positiveTotal = 0;
        var gained = 0;
        var penalty = 0;

        foreach (var rule in Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Label)) continue;

            var hit = rule.Keywords.FirstOrDefault(k =>
                !string.IsNullOrWhiteSpace(k) &&
                haystack.Contains(k, StringComparison.OrdinalIgnoreCase));

            var matched = hit is not null;

            if (rule.Weight >= 0)
            {
                positiveTotal += rule.Weight;
                if (matched) gained += rule.Weight;
            }
            else if (matched)
            {
                penalty += -rule.Weight;
            }

            reasons.Add(new FitReason
            {
                Label = rule.Label,
                Weight = rule.Weight,
                Matched = matched,
                MatchedOn = hit,
            });
        }

        if (positiveTotal == 0) return (0, reasons);

        var score = (int)Math.Round(gained * 100.0 / positiveTotal) - penalty;
        return (Math.Clamp(score, 0, 100), reasons);
    }

    /// <summary>
    /// 아직 이직전략을 반영하지 않은 상태의 기본 규칙.
    /// 이 저장소에서 확인된 조건(서버 프로그래머, C++/C#, 게임 업계)만 담았다.
    /// </summary>
    public static FitSettings Default() => new()
    {
        Enabled = true,
        HighlightThreshold = 60,
        Rules = new List<FitRule>
        {
            new() { Label = "C++ 사용",     Keywords = new() { "C++", "C/C++" },                       Weight = 30 },
            new() { Label = "C# / .NET",    Keywords = new() { "C#", ".NET" },                         Weight = 20 },
            new() { Label = "게임 업계",     Keywords = new() { "게임", "MMORPG", "RPG", "모바일게임" }, Weight = 25 },
            new() { Label = "서버 직무",     Keywords = new() { "서버", "백엔드", "backend" },           Weight = 20 },
            new() { Label = "경력직",        Keywords = new() { "경력" },                               Weight = 15 },
            new() { Label = "수도권",        Keywords = new() { "서울", "경기", "성남", "판교" },        Weight = 10 },

            // 감점 항목
            new() { Label = "신입·인턴 대상", Keywords = new() { "신입", "인턴", "교육생", "전환형" },    Weight = -20 },
            new() { Label = "계약·파견",      Keywords = new() { "계약직", "파견", "프리랜서" },          Weight = -15 },
        },
    };
}
