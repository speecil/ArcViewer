# examples:
#   .\scripts\build.ps1 webgl
#   .\scripts\build.ps1 webgl release
#   .\scripts\build.ps1 win64 dev
#   .\scripts\build.ps1 linux64 release

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Platform,
    [Parameter(Position = 1)]
    [string]$Mode = 'dev',
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

function Show-Usage {
    Get-Content -Path $PSCommandPath -TotalCount 5 |
        ForEach-Object { $_ -replace '^# ?', '' }
}

if ($Help -or -not $Platform) {
    Show-Usage
    if (-not $Platform) { exit 2 } else { exit 0 }
}

if ($Platform -notin @('webgl', 'win64', 'linux64', 'macos')) {
    Write-Error "unknown platform '$Platform' (expected: webgl, win64, linux64, macos)"
    exit 2
}
if ($Mode -notin @('dev', 'release')) {
    Write-Error "unknown mode '$Mode' (expected: dev or release)"
    exit 2
}

$proj = Resolve-Path (Join-Path $PSScriptRoot '..')

function Escape-Json($value) {
    $escaped = $value -replace '\\', '\\'
    return $escaped -replace '"', '\"'
}

function Get-Env-OrDefault($name, $defaultValue) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($value)) { return $defaultValue }
    return $value
}

function Write-WebGLEnv($buildDir) {
    $indexFile = Join-Path $buildDir 'index.html'
    $arcviewerBaseUrl = Escape-Json (Get-Env-OrDefault 'ARCVIEWER_BASE_URL' 'https://scoresaber.com/')
    $scoresaberBaseUrl = Escape-Json (Get-Env-OrDefault 'ARCVIEWER_SCORESABER_BASE_URL' 'https://scoresaber.com/')
    $scoresaberApiUrl = Escape-Json (Get-Env-OrDefault 'ARCVIEWER_SCORESABER_API_URL' 'https://scoresaber.com/api/v2/')

    @"
window.arcviewerEnv = {
  arcViewerBaseUrl: "$arcviewerBaseUrl",
  scoreSaberBaseUrl: "$scoresaberBaseUrl",
  scoreSaberApiUrl: "$scoresaberApiUrl"
};
"@ | Set-Content -Path (Join-Path $buildDir 'arcviewer-env.js') -NoNewline

    if ((Test-Path $indexFile) -and -not (Select-String -Path $indexFile -SimpleMatch 'arcviewer-env.js' -Quiet)) {
        $html = Get-Content -Path $indexFile -Raw
        $html = $html -replace '</head>', "  <script src=`"arcviewer-env.js`"></script>`n</head>"
        Set-Content -Path $indexFile -Value $html -NoNewline
    }
}

$versionFile = Join-Path $proj 'ProjectSettings/ProjectVersion.txt'
if (-not (Test-Path $versionFile)) {
    Write-Error "ProjectVersion.txt not found at $versionFile"
    exit 1
}
$unityVersion = (Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value
if (-not $unityVersion) {
    Write-Error "could not parse Unity version from $versionFile"
    exit 1
}

if ($env:UNITY_PATH) {
    $unity = $env:UNITY_PATH
} else {
    $unity = "C:\Program Files\Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"
}

if (-not (Test-Path $unity)) {
    Write-Error "Unity binary not found at: $unity`ninstall Unity $unityVersion, or override with `$env:UNITY_PATH"
    exit 1
}

$projPattern = [regex]::Escape($proj.Path)
$running = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -match "-projectPath.*$projPattern" }
if ($running) {
    Write-Error "Unity is already open on this project; close it first"
    exit 1
}

$log = if ($env:LOG_FILE) { $env:LOG_FILE } else { Join-Path $env:TEMP "arcviewer-$Platform-$Mode.log" }
Write-Host "building $Platform/$Mode -> $proj\Build"
Write-Host "log: $log"

$env:ARCVIEWER_PLATFORM = $Platform
$env:ARCVIEWER_MODE = $Mode

& $unity `
    -batchmode -nographics -quit `
    -disable-assembly-updater `
    -noUpm `
    -projectPath $proj `
    -executeMethod PlayerBuilder.Build `
    -logFile $log
$rc = $LASTEXITCODE
if ($rc -ne 0) { exit $rc }

$buildDir = Join-Path $proj 'Build'
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
"$Platform $Mode" | Set-Content -Path (Join-Path $buildDir 'last-built.txt') -NoNewline
if ($Platform -eq 'webgl') {
    Write-WebGLEnv $buildDir
}
