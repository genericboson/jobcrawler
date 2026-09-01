# 게임잡 서버 프로그래머 공고 크롤러

게임잡(gamejob.co.kr)에서 서버 직무 채용공고를 모아 날짜별 HTML 리포트를 만든다.
리포트의 체크박스를 켜면 그 공고가 "이미 지원한 공고"로 기록되어 다음 리포트부터 빠진다.

```
jobcrawler/
├── config.json              설정 (직무코드, 키워드 필터, 포트, 실행 시각)
├── app/                     dotnet publish 산출물 (스케줄러가 실행하는 exe)
├── secrets/
│   └── smtp-password.txt    SMTP 비밀번호 (직접 만든다. 저장소에 올라가지 않음)
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
| `install-service` | 위 서버를 Windows 서비스로 등록한다 (관리자 권한 필요) |
| `uninstall-service` | 서비스를 해제한다 (관리자 권한 필요) |
| `service-status` | 서비스 상태를 본다 |
| `run` | `crawl` 후 `serve` 하고 브라우저를 연다 |
| `list` | 지원한 공고 목록을 콘솔에 출력한다 |
| `test-email` | 크롤링 없이 메일 설정이 맞는지 시험 발송해 본다 |
| `install-schedule` | 매일 정해진 시각에 `crawl` 이 돌도록 작업 스케줄러에 등록한다 |
| `uninstall-schedule` | 위 작업을 해제한다 |
| `schedule-status` | 등록된 작업 상태를 본다 |

## 쓰는 방법

`http://localhost:8777/` 을 열면 가장 최근 리포트가 나온다.
공고 왼쪽 체크박스를 켜면 곧바로 `dailyreport/oldjoblist.json` 에 기록되고,
다음 `crawl` 부터 그 공고는 리포트에 나오지 않는다. 체크를 풀면 기록에서 다시 빠진다.

체크가 저장되려면 리포트 서버가 떠 있어야 한다.
**아래 서비스 등록을 한 번 해 두면 부팅할 때부터 항상 떠 있으므로 신경 쓸 일이 없다.**
임시로 띄우려면 `serve` 를 직접 실행해도 된다.

메일에 첨부된 HTML 을 파일 탐색기에서 직접(`file://`) 열어도 서버만 떠 있으면 체크가 그대로 저장된다.
서버가 꺼져 있으면 브라우저에 임시 보관해 두었다가, 서버가 켜진 뒤 같은 리포트를 다시 열 때 자동으로 반영된다.

## 리포트 서버를 Windows 서비스로 등록

서비스로 두면 로그인하지 않아도 부팅 직후부터 서버가 뜨고, 죽어도 스스로 되살아난다.
**서비스 등록은 관리자 권한이 필요하다.**

먼저 실행 파일을 만든다.

```bash
dotnet publish src/JobCrawler -c Release -o app
```

그 다음 **관리자 권한 명령 프롬프트**에서 (시작 메뉴 > 명령 프롬프트 > 오른쪽 클릭 > 관리자 권한으로 실행):

```bash
cd /d D:\projects\jobcrawler\app && JobCrawler.exe install-service
```

등록되는 내용은 이렇다.

- 서비스 이름 `JobCrawlerReportServer`, 시작 유형 자동
- 죽으면 5초 뒤 재시작, 두 번째까지 실패하면 1분 간격으로 재시도
- `LocalSystem` 계정으로 돌며 `http://localhost:8777/` 만 연다

상태 확인과 해제:

```bash
JobCrawler.exe service-status
```

```bash
JobCrawler.exe uninstall-service
```

`uninstall-service` 도 관리자 권한이 필요하다.
`config.json` 의 `ServerPort` 를 바꿨다면 서비스를 지웠다가 다시 등록해야 한다.

## 리포트 메일로 받기

`crawl` 이 리포트를 만들 때마다 `config.json` 의 `Email.To` 로 메일을 보낸다.
메일에는 공고 목록이 본문으로 들어가고, 리포트 HTML 파일이 첨부된다.
메일 클라이언트는 스크립트를 걷어내므로 **메일 안에서는 체크박스가 동작하지 않는다.**
체크는 `serve` 를 띄우고 `http://localhost:8777/` 에서 한다.

### 1. 네이버 메일에서 SMTP 켜기

네이버 메일 > 환경설정 > POP3/IMAP 설정 > **IMAP/SMTP 사용**을 '사용함' 으로 바꾼다.
켜지 않으면 로그인 단계에서 실패한다.

### 2. 비밀번호 넣기

비밀번호는 `config.json` 에 넣지 않는다. 이 파일은 저장소에 올라간다.
아래 둘 중 하나를 쓴다. 스케줄러로 돌릴 거라면 파일 쪽이 확실하다.

**파일로 두기** (`secrets/` 는 `.gitignore` 에 들어 있다)

```bash
mkdir -p secrets
```

만든 뒤 `secrets/smtp-password.txt` 에 비밀번호 한 줄만 저장한다.

**환경변수로 두기** (설정 후 새 터미널을 열어야 적용된다)

```bash
setx JOBCRAWLER_SMTP_PASSWORD "여기에-비밀번호"
```

### 3. 확인

```bash
dotnet run --project src/JobCrawler -- test-email
```

`you@example.com` 으로 확인용 메일 한 통이 간다.

### 메일 설정 항목

| 항목 | 설명 |
|---|---|
| `Email.Enabled` | `false` 로 두면 메일을 보내지 않는다 |
| `Email.To` | 받는 주소 |
| `Email.From` | 보내는 주소. 보통 SMTP 계정 자신의 주소여야 한다 |
| `Email.SmtpHost` / `SmtpPort` | 네이버는 `smtp.naver.com` / `587` |
| `Email.UseStartTls` | `true` 면 587 STARTTLS, `false` 면 465 SSL |
| `Email.UserName` | SMTP 로그인 아이디. 네이버는 주소의 `@` 앞부분 |
| `Email.PasswordEnvVar` | 비밀번호를 담은 환경변수 이름 |
| `Email.PasswordFile` | 환경변수가 없을 때 읽을 파일 경로 |
| `Email.AttachReport` | 리포트 HTML 파일을 첨부할지 여부 |

지메일로 보내려면 `SmtpHost` 를 `smtp.gmail.com`, `UserName` 을 전체 메일 주소로 두고,
계정 비밀번호 대신 **앱 비밀번호**를 발급받아 쓴다.

메일 발송이 실패해도 리포트 파일은 그대로 남는다.

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
