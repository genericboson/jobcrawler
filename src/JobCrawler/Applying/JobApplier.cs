using Microsoft.Playwright;

namespace JobCrawler.Applying;

/// <summary>
/// 지원 양식을 열어 첨부와 링크를 채워 두는 도우미.
///
/// 제출은 하지 않는다. 사람이 화면을 보고 직접 누른다.
/// 그래서 아래 선택자가 틀려도 '덜 채워짐' 에서 끝나고 잘못 제출될 일은 없다.
/// </summary>
public sealed class JobApplier
{
    private readonly ApplySettings _settings;
    private readonly string _root;

    /// <summary>지원 양식으로 들어가는 버튼. 사이트마다 글자가 다르다.</summary>
    private static readonly string[] ApplyButtonTexts =
    {
        "즉시지원", "입사지원", "지원하기", "온라인 지원", "간편지원",
    };

    /// <summary>포트폴리오 링크를 받는 칸으로 볼 만한 낱말.</summary>
    private static readonly string[] UrlFieldHints =
    {
        "포트폴리오", "portfolio", "github", "깃허브", "url", "link", "링크",
        "주소", "블로그", "blog", "homepage", "홈페이지",
    };

    public JobApplier(ApplySettings settings, string root)
    {
        _settings = settings;
        _root = root;
    }

    /// <summary>공고를 열어 지원 양식을 준비한다. 브라우저는 열어 둔 채 돌아온다.</summary>
    public async Task<int> PrepareAsync(string postingUrl, CancellationToken ct)
    {
        var problems = _settings.Validate(_root);
        if (problems.Count > 0)
        {
            Console.Error.WriteLine("지원 준비 설정에 문제가 있습니다:");
            foreach (var p in problems) Console.Error.WriteLine($"  - {p}");
            return 1;
        }

        var profileDir = _settings.ResolvePath(_root, _settings.BrowserProfileDir);
        Directory.CreateDirectory(profileDir);

        Console.WriteLine($"공고: {postingUrl}");
        Console.WriteLine($"브라우저 프로필: {profileDir}");
        Console.WriteLine();

        using var playwright = await Playwright.CreateAsync();

        var options = new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = false,
            ViewportSize = ViewportSize.NoViewport,
            Args = new[] { "--start-maximized" },
        };
        if (!string.IsNullOrWhiteSpace(_settings.BrowserChannel))
            options.Channel = _settings.BrowserChannel;

        await using var context = await playwright.Chromium
            .LaunchPersistentContextAsync(profileDir, options);

        var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();

        await page.GotoAsync(postingUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 45_000,
        });

        if (await IsLoginWallAsync(page))
        {
            Console.WriteLine("로그인이 필요합니다.");
            Console.WriteLine("브라우저에서 직접 로그인하세요. 한 번 하면 다음부터는 유지됩니다.");
            Console.WriteLine("로그인 뒤 이 공고를 다시 '지원 준비' 로 여시면 양식을 채워 드립니다.");
            await WaitUntilClosedAsync(context, ct);
            return 0;
        }

        var opened = await OpenApplyFormAsync(page, ct);
        if (!opened)
        {
            Console.WriteLine("지원 양식을 자동으로 열지 못했습니다.");
            Console.WriteLine("회사 홈페이지로 지원하는 공고이거나 버튼 모양이 다를 수 있습니다.");
            Console.WriteLine("브라우저에서 직접 진행하세요. 첨부할 파일과 링크는 아래와 같습니다.");
            PrintDocuments();
            await WaitUntilClosedAsync(context, ct);
            return 0;
        }

        // 양식이 팝업으로 열리는 사이트가 있어 새 창까지 살펴본다.
        var form = context.Pages.LastOrDefault() ?? page;
        await form.WaitForTimeoutAsync(1500);

        var filled = await FillFormAsync(form);

        Console.WriteLine();
        Console.WriteLine("── 채워 넣은 것 ──");
        if (filled.Count == 0)
            Console.WriteLine("  (알아볼 수 있는 칸을 찾지 못했습니다)");
        foreach (var line in filled)
            Console.WriteLine($"  {line}");

        Console.WriteLine();
        Console.WriteLine("빠진 칸은 직접 채우신 뒤, 내용을 확인하고 제출 버튼을 눌러 주세요.");
        Console.WriteLine("이 프로그램은 제출하지 않습니다.");
        PrintDocuments();

        await WaitUntilClosedAsync(context, ct);
        return 0;
    }

    private void PrintDocuments()
    {
        Console.WriteLine();
        Console.WriteLine("── 준비된 자료 ──");
        Console.WriteLine($"  이력서·경력기술서: {_settings.ResolvePath(_root, _settings.ResumeFile)}");
        if (!string.IsNullOrWhiteSpace(_settings.PortfolioFile))
            Console.WriteLine($"  포트폴리오 파일 : {_settings.ResolvePath(_root, _settings.PortfolioFile)}");
        if (!string.IsNullOrWhiteSpace(_settings.PortfolioUrl))
            Console.WriteLine($"  포트폴리오 링크 : {_settings.PortfolioUrl}");
    }

    private static async Task<bool> IsLoginWallAsync(IPage page)
    {
        var url = page.Url.ToLowerInvariant();
        return url.Contains("/login") || url.Contains("login.asp") || url.Contains("logintot");
    }

    /// <summary>지원 버튼을 찾아 눌러 양식까지 들어간다.</summary>
    private static async Task<bool> OpenApplyFormAsync(IPage page, CancellationToken ct)
    {
        foreach (var text in ApplyButtonTexts)
        {
            // 링크·버튼 어느 쪽으로 만들어져 있어도 잡히도록 텍스트로 찾는다.
            var candidate = page.GetByText(text, new PageGetByTextOptions { Exact = false }).First;

            try
            {
                if (await candidate.CountAsync() == 0) continue;
                if (!await candidate.IsVisibleAsync()) continue;

                Console.WriteLine($"'{text}' 를 눌러 양식을 엽니다.");
                await candidate.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
                await page.WaitForTimeoutAsync(2000);
                return true;
            }
            catch (PlaywrightException)
            {
                // 이 낱말로는 못 눌렀을 뿐이다. 다음 낱말로 넘어간다.
            }
        }

        return false;
    }

    /// <summary>양식에서 알아볼 수 있는 칸을 채운다. 무엇을 채웠는지 돌려준다.</summary>
    private async Task<List<string>> FillFormAsync(IPage page)
    {
        var done = new List<string>();
        var resume = _settings.ResolvePath(_root, _settings.ResumeFile);
        var portfolio = _settings.ResolvePath(_root, _settings.PortfolioFile);

        // 1) 포트폴리오 링크 칸부터 채운다. 링크를 받는 곳이면 파일은 올리지 않는다.
        var urlFilled = false;
        if (!string.IsNullOrWhiteSpace(_settings.PortfolioUrl))
        {
            var inputs = await page.QuerySelectorAllAsync("input[type=text], input[type=url], input:not([type])");
            foreach (var input in inputs)
            {
                if (!await input.IsVisibleAsync()) continue;

                var hay = string.Join(' ', new[]
                {
                    await input.GetAttributeAsync("name") ?? "",
                    await input.GetAttributeAsync("id") ?? "",
                    await input.GetAttributeAsync("placeholder") ?? "",
                    await input.GetAttributeAsync("title") ?? "",
                }).ToLowerInvariant();

                if (!UrlFieldHints.Any(h => hay.Contains(h, StringComparison.OrdinalIgnoreCase))) continue;

                await input.FillAsync(_settings.PortfolioUrl);
                done.Add($"포트폴리오 링크 → {_settings.PortfolioUrl}");
                urlFilled = true;
                break;
            }
        }

        // 2) 파일 첨부란. 첫 칸에 경력기술서, 남는 칸이 있으면 포트폴리오를 올린다.
        var fileInputs = await page.QuerySelectorAllAsync("input[type=file]");
        var slot = 0;

        foreach (var input in fileInputs)
        {
            try
            {
                if (slot == 0)
                {
                    await input.SetInputFilesAsync(resume);
                    done.Add($"이력서 첨부 → {Path.GetFileName(resume)}");
                    slot++;
                    continue;
                }

                // 링크를 이미 적었으면 포트폴리오 파일까지 올리지는 않는다.
                if (!urlFilled && !string.IsNullOrWhiteSpace(portfolio) && slot == 1)
                {
                    await input.SetInputFilesAsync(portfolio);
                    done.Add($"포트폴리오 첨부 → {Path.GetFileName(portfolio)}");
                    slot++;
                }
            }
            catch (PlaywrightException ex)
            {
                Console.Error.WriteLine($"  첨부 실패: {ex.Message}");
            }
        }

        if (fileInputs.Count == 0)
            done.Add("(파일 첨부란을 찾지 못했습니다 — 직접 올려 주세요)");

        return done;
    }

    /// <summary>브라우저를 닫을 때까지 기다린다. 닫으면 프로그램도 끝난다.</summary>
    private static async Task WaitUntilClosedAsync(IBrowserContext context, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("브라우저를 닫으면 이 창도 닫힙니다.");

        var closed = new TaskCompletionSource();
        context.Close += (_, _) => closed.TrySetResult();

        using var registration = ct.Register(() => closed.TrySetResult());
        await closed.Task;
    }
}
