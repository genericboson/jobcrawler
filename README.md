# 게임잡 서버 프로그래머 공고 크롤러

게임잡(gamejob.co.kr)에서 서버 직무 채용공고를 모아 날짜별 HTML 리포트를 만든다.
리포트의 체크박스를 켜면 그 공고가 "이미 지원한 공고"로 기록되어 다음 리포트부터 빠진다.

```
jobcrawler/
├── config.json              설정 (직무코드, 키워드 필터, 포트, 실행 시각)
├── app/                     dotnet publish 산출물 (스케줄러가 실행하는 exe)
├── dailyreport/
│   ├── 2026-08-30.html      날짜별 리포트
│   ├── oldjoblist.json      이미 지원한 공고 - 다음 리포트에서 제외됨
│   └── seenjobs.json        공고별 최초 발견일 (NEW 배지 판정용)
└── src/JobCrawler/          C# 소스 (.NET 8)
```

## 준비

```bash
dotnet build
```

빌드 없이 바로 돌리려면 아래 명령의 `JobCrawler` 자리에 `dotnet run --project src/JobCrawler --` 를 쓰면 된다.

## 명령

| 명령 | 하는 일 |
|---|---|
| `crawl` | 게임잡을 긁어 `dailyreport/yyyy-MM-dd.html` 을 만든다. 스케줄러가 매일 실행하는 명령 |
| `serve` | 리포트 열람 서버를 띄운다. 체크박스가 `oldjoblist.json` 에 기록되려면 이게 떠 있어야 한다 |
| `run` | `crawl` 후 `serve` 하고 브라우저를 연다 |
| `list` | 지원한 공고 목록을 콘솔에 출력한다 |
| `install-schedule` | 매일 정해진 시각에 `crawl` 이 돌도록 작업 스케줄러에 등록한다 |
| `uninstall-schedule` | 위 작업을 해제한다 |
| `schedule-status` | 등록된 작업 상태를 본다 |

## 쓰는 방법

리포트를 볼 때는 **`serve` 를 띄우고 `http://localhost:8777/` 로 연다.**

```bash
dotnet run --project src/JobCrawler -- serve
```

`http://localhost:8777/` 은 가장 최근 리포트로 넘겨준다.
공고 왼쪽 체크박스를 켜면 곧바로 `dailyreport/oldjoblist.json` 에 기록되고,
다음 `crawl` 부터 그 공고는 리포트에 나오지 않는다. 체크를 풀면 기록에서 다시 빠진다.

HTML 파일을 파일 탐색기에서 직접(`file://`) 열어도 목록은 그대로 보이지만,
브라우저는 파일을 쓸 수 없으므로 체크 결과가 바로 반영되지 않는다.
이때는 브라우저에 임시 보관해 두었다가 나중에 `serve` 가 떠 있는 상태에서
`http://localhost:8777/` 로 같은 리포트를 열면 밀린 기록이 자동으로 반영된다.

## 매일 자동 실행

스케줄러가 실행할 exe 를 먼저 만든다. `bin/Debug` 는 clean 하면 사라지므로 `app/` 에 따로 게시한다.

```bash
dotnet publish src/JobCrawler -c Release -o app
```

```bash
dotnet run --project src/JobCrawler -- install-schedule
```

`config.json` 의 `ScheduleTime`(기본 `20:00`)에 맞춰 Windows 작업 스케줄러에
`GameJobCrawler_DailyReport` 작업을 등록한다. 리포트만 만들 뿐 서버를 띄우지는 않으므로,
결과를 보고 체크하려면 그때 `serve` 를 실행하면 된다.

등록되는 작업은 이렇게 설정된다.

- 배터리로 쓰는 중에도 실행한다
- 20시에 PC 가 꺼져 있었다면 다음에 켰을 때 만회 실행한다
- 네트워크가 있을 때만 실행하고, 30분을 넘기면 중단한다

`ScheduleTime` 이나 게시 경로를 바꿨다면 `install-schedule` 을 다시 실행하면 덮어쓴다.

등록 확인·수동 실행·해제:

```bash
schtasks /Query /TN GameJobCrawler_DailyReport
```

```bash
schtasks /Run /TN GameJobCrawler_DailyReport
```

```bash
dotnet run --project src/JobCrawler -- uninstall-schedule
```

## 설정 (`config.json`)

| 항목 | 설명 |
|---|---|
| `DutyCodes` | 크롤링할 게임잡 직무 코드. 기본 `[16]` = 기술지원 > 서버 |
| `IncludeKeywords` | 제목·직무에 이 중 하나라도 있어야 리포트에 넣는다. 비우면 직무 코드 결과를 전부 넣는다 |
| `ExcludeKeywords` | 제목·직무에 이 단어가 있으면 뺀다 |
| `MaxPages` | 읽어올 최대 페이지 수 (안전장치) |
| `PageSize` | 한 요청에 받아올 공고 수 (20~100) |
| `RequestDelayMs` | 페이지 요청 사이 대기 시간 |
| `ServerPort` | `serve` 가 쓰는 로컬 포트. 바꾸면 리포트를 다시 만들어야 한다 |
| `ScheduleTime` | `install-schedule` 이 등록할 시각 (`HH:mm`) |

### 직무 코드

| 코드 | 직무 | 코드 | 직무 |
|---|---|---|---|
| 1 | 게임개발(클라이언트) | 17 | 네트워크 |
| 2 | 게임개발(모바일) | 18 | 엔진 |
| 3 | 게임AI 개발 | 19 | 시스템·DB |
| 12 | 플랫폼 개발 | 20 | 보안 |
| 16 | **서버** | 21 | 클라우드 |

범위를 넓히되 서버 쪽만 남기고 싶다면 이런 식으로 조합한다.

```json
{
  "DutyCodes": [16, 17, 19, 21],
  "IncludeKeywords": ["서버", "server", "백엔드", "backend"]
}
```

## 동작 메모

게임잡 목록은 `/Recruit/_GI_Job_List/` 로 보내는 POST 로 페이지를 넘긴다.
검색 조건을 매 요청 본문(`condition[duty][]`)에 실어 보내는 방식이라 크롤러도 같은 요청을 쓴다.
같은 경로에 GET 으로 `Page=N` 만 붙이면 직무 필터가 풀린 전체 목록이 돌아오므로 쓰면 안 된다.

공고 식별자는 게임잡의 공고 번호(`GI_No`)다. 제목이 바뀌어도 같은 공고로 인식한다.
