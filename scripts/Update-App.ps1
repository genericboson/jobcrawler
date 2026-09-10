<#
.SYNOPSIS
    소스를 다시 게시해 app\JobCrawler.exe 를 최신으로 만든다.

.DESCRIPTION
    리포트 서버가 Windows 서비스로 돌고 있으면 app\JobCrawler.dll 을 붙잡고 있어
    dotnet publish 가 실패한다. 그래서 서비스를 내리고, 게시하고, 다시 올린다.

    서비스를 멈추고 시작하려면 관리자 권한이 필요하다.
    서비스가 등록되어 있지 않으면 게시만 하고 끝낸다.

.EXAMPLE
    관리자 권한 PowerShell 에서:
    D:\projects\jobcrawler\scripts\Update-App.ps1
#>

[CmdletBinding()]
param(
    [string] $ServiceName = 'JobCrawlerReportServer',

    # 서비스가 파일 잠금을 놓을 때까지 기다리는 최대 시간(초)
    [int] $StopTimeoutSeconds = 20
)

$ErrorActionPreference = 'Stop'

# 이 스크립트는 scripts\ 안에 있으므로 한 단계 위가 프로젝트 루트다.
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\JobCrawler'
$output = Join-Path $root 'app'

function Test-Administrator {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-ServiceState {
    param([string] $Name)
    $svc = Get-CimInstance -ClassName Win32_Service -Filter "Name='$Name'" -ErrorAction SilentlyContinue
    if ($null -eq $svc) { return $null }
    return $svc.State
}

Write-Host "프로젝트 루트: $root"

$state = Get-ServiceState -Name $ServiceName
$wasRunning = $false

if ($null -eq $state) {
    Write-Host "'$ServiceName' 서비스가 등록되어 있지 않습니다. 게시만 진행합니다."
}
else {
    Write-Host "'$ServiceName' 서비스 상태: $state"

    if (-not (Test-Administrator)) {
        Write-Error @"
서비스를 멈추려면 관리자 권한이 필요합니다.

시작 메뉴에서 'PowerShell' 을 오른쪽 클릭 > '관리자 권한으로 실행' 한 뒤
아래를 실행하세요.

  $PSCommandPath
"@
    }

    if ($state -eq 'Running') {
        $wasRunning = $true
        Write-Host '서비스를 내리는 중...'
        Stop-Service -Name $ServiceName -Force

        # 프로세스가 완전히 사라져야 파일 잠금이 풀린다.
        $deadline = (Get-Date).AddSeconds($StopTimeoutSeconds)
        while ((Get-Date) -lt $deadline) {
            if ((Get-ServiceState -Name $ServiceName) -eq 'Stopped') { break }
            Start-Sleep -Milliseconds 500
        }

        if ((Get-ServiceState -Name $ServiceName) -ne 'Stopped') {
            Write-Error "$StopTimeoutSeconds 초 안에 서비스가 멈추지 않았습니다."
        }

        # 서비스가 Stopped 로 보고한 뒤에도 핸들이 잠깐 남아 있는 경우가 있다.
        Start-Sleep -Seconds 2
        Write-Host '서비스를 내렸습니다.'
    }
}

Write-Host '게시하는 중...'
dotnet publish $project -c Release -o $output

if ($LASTEXITCODE -ne 0) {
    # 게시가 실패했어도 서버는 다시 올려 둔다. 체크박스 저장이 멈추면 안 된다.
    if ($wasRunning) {
        Write-Host '게시에 실패했지만 서비스는 다시 올립니다.'
        Start-Service -Name $ServiceName
    }
    Write-Error "dotnet publish 가 실패했습니다 (종료 코드 $LASTEXITCODE)."
}

Write-Host "게시 완료: $output"

if ($wasRunning) {
    Write-Host '서비스를 다시 올리는 중...'
    Start-Service -Name $ServiceName
    Write-Host "서비스 상태: $(Get-ServiceState -Name $ServiceName)"
}

Write-Host ''
Write-Host '끝났습니다. 리포트는 http://localhost:8777/ 에서 볼 수 있습니다.'
