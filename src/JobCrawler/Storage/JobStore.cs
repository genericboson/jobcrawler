using System.Text.Json;
using JobCrawler.Models;

namespace JobCrawler.Storage;

/// <summary>
/// dailyreport 폴더의 상태 파일 두 개를 관리한다.
///
///  - oldjoblist.json : 이미 지원한 공고. 여기 등재된 공고는 다음 리포트부터 제외된다.
///  - seenjobs.json   : 공고를 처음 발견한 날짜. 리포트의 NEW 배지 판정에만 쓴다.
///
/// 리포트 페이지의 체크박스와 크롤러가 같은 파일을 건드리므로 프로세스 간 쓰기 경합을
/// 막기 위해 파일 단위 이름있는 뮤텍스로 직렬화한다.
/// </summary>
public sealed class JobStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _oldJobListPath;
    private readonly string _seenPath;
    private readonly Mutex _lock;

    public JobStore(string reportDirectory)
    {
        Directory.CreateDirectory(reportDirectory);
        _oldJobListPath = Path.Combine(reportDirectory, "oldjoblist.json");
        _seenPath = Path.Combine(reportDirectory, "seenjobs.json");

        // 경로마다 고유한 뮤텍스 이름. 백슬래시는 뮤텍스 이름에 쓸 수 없어 치환한다.
        var key = Path.GetFullPath(reportDirectory).ToLowerInvariant()
            .Replace('\\', '_').Replace('/', '_').Replace(':', '_');
        _lock = new Mutex(false, $"Local\\JobCrawler_{key}");
    }

    public string OldJobListPath => _oldJobListPath;

    // ---------- oldjoblist ----------

    public List<AppliedJob> LoadApplied() => WithLock(() => ReadList<AppliedJob>(_oldJobListPath));

    public HashSet<string> LoadAppliedIds() =>
        LoadApplied().Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

    /// <summary>지원한 공고로 등재한다. 이미 있으면 그대로 둔다. 새로 등재됐으면 true.</summary>
    public bool MarkApplied(AppliedJob job) => WithLock(() =>
    {
        var list = ReadList<AppliedJob>(_oldJobListPath);
        if (list.Any(a => a.Id == job.Id)) return false;

        if (job.AppliedAt == default) job.AppliedAt = DateTimeOffset.Now;
        list.Add(job);
        WriteList(_oldJobListPath, list);
        return true;
    });

    /// <summary>체크를 해제했을 때 oldjoblist 에서 뺀다. 실제로 지웠으면 true.</summary>
    public bool UnmarkApplied(string id) => WithLock(() =>
    {
        var list = ReadList<AppliedJob>(_oldJobListPath);
        var removed = list.RemoveAll(a => a.Id == id);
        if (removed == 0) return false;

        WriteList(_oldJobListPath, list);
        return true;
    });

    // ---------- seenjobs ----------

    /// <summary>
    /// 이번에 수집한 공고들의 최초 발견일을 기록하고, 각 공고에 FirstSeen 을 채워 넣는다.
    /// </summary>
    public void RecordSeen(IEnumerable<JobPosting> jobs, DateOnly today) => WithLock(() =>
    {
        var seen = ReadDictionary(_seenPath);
        var changed = false;

        foreach (var job in jobs)
        {
            if (seen.TryGetValue(job.Id, out var first))
            {
                job.FirstSeen = first;
            }
            else
            {
                seen[job.Id] = today;
                job.FirstSeen = today;
                changed = true;
            }
        }

        if (changed) WriteDictionary(_seenPath, seen);
        return true;
    });

    // ---------- 파일 입출력 ----------

    private static List<T> ReadList<T>(string path)
    {
        if (!File.Exists(path)) return new List<T>();
        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return new List<T>();
            return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? new List<T>();
        }
        catch (JsonException)
        {
            // 파일이 손상됐으면 덮어쓰기 전에 백업을 남긴다.
            BackupCorrupted(path);
            return new List<T>();
        }
    }

    private static void WriteList<T>(string path, List<T> list) =>
        WriteAtomic(path, JsonSerializer.Serialize(list, JsonOpts));

    private static Dictionary<string, DateOnly> ReadDictionary(string path)
    {
        if (!File.Exists(path)) return new Dictionary<string, DateOnly>();
        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, DateOnly>();
            return JsonSerializer.Deserialize<Dictionary<string, DateOnly>>(json, JsonOpts)
                   ?? new Dictionary<string, DateOnly>();
        }
        catch (JsonException)
        {
            BackupCorrupted(path);
            return new Dictionary<string, DateOnly>();
        }
    }

    private static void WriteDictionary(string path, Dictionary<string, DateOnly> map) =>
        WriteAtomic(path, JsonSerializer.Serialize(map, JsonOpts));

    /// <summary>임시 파일에 쓰고 교체해 중간에 끊겨도 기존 파일이 깨지지 않게 한다.</summary>
    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }

    private static void BackupCorrupted(string path)
    {
        var backup = $"{path}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
        try { File.Copy(path, backup, overwrite: true); } catch (IOException) { }
        Console.Error.WriteLine($"경고: {path} 를 읽을 수 없어 {backup} 로 백업하고 새로 시작합니다.");
    }

    private T WithLock<T>(Func<T> action)
    {
        var acquired = false;
        try
        {
            try { acquired = _lock.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { acquired = true; }
            return action();
        }
        finally
        {
            if (acquired) _lock.ReleaseMutex();
        }
    }
}
