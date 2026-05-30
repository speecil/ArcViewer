# examples:
#   .\scripts\start.ps1                       run the last build (or build webgl release)
#   .\scripts\start.ps1 -Rebuild              rebuild the last config first
#   .\scripts\start.ps1 -Port 8080            webgl server on a different port
#   .\scripts\start.ps1 -Port 1234 -Rebuild

[CmdletBinding()]
param(
    [int]$Port = 1183,
    [switch]$Rebuild,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

function Show-Usage {
    Get-Content -Path $PSCommandPath -TotalCount 5 |
        ForEach-Object { $_ -replace '^# ?', '' }
}

if ($Help) { Show-Usage; exit 0 }

$proj = Resolve-Path (Join-Path $PSScriptRoot '..')
$configFile = Join-Path $proj 'Build/last-built.txt'

function Get-Artifact($plat) {
    switch ($plat) {
        'webgl'   { Join-Path $proj 'Build/index.html' }
        'win64'   { Join-Path $proj 'Build/win64/ArcViewer.exe' }
        'linux64' { Join-Path $proj 'Build/linux64/ArcViewer' }
        'macos'   { Join-Path $proj 'Build/macos/ArcViewer.app' }
        default   { throw "unknown platform '$plat'" }
    }
}

function Escape-Json($value) {
    $escaped = $value -replace '\\', '\\'
    return $escaped -replace '"', '\"'
}

function Get-Env-OrDefault($name, $defaultValue) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($value)) { return $defaultValue }
    return $value
}

function Write-WebGLEnv($serveDir) {
    $indexFile = Join-Path $serveDir 'index.html'
    $arcviewerBaseUrl = Escape-Json (Get-Env-OrDefault 'ARCVIEWER_BASE_URL' 'https://scoresaber.com/')
    $scoresaberBaseUrl = Escape-Json (Get-Env-OrDefault 'ARCVIEWER_SCORESABER_BASE_URL' 'https://scoresaber.com/')
    $scoresaberApiUrl = Escape-Json (Get-Env-OrDefault 'ARCVIEWER_SCORESABER_API_URL' 'https://scoresaber.com/api/v2/')

    @"
window.arcviewerEnv = {
  arcViewerBaseUrl: "$arcviewerBaseUrl",
  scoreSaberBaseUrl: "$scoresaberBaseUrl",
  scoreSaberApiUrl: "$scoresaberApiUrl"
};
"@ | Set-Content -Path (Join-Path $serveDir 'arcviewer-env.js') -NoNewline

    if ((Test-Path $indexFile) -and -not (Select-String -Path $indexFile -SimpleMatch 'arcviewer-env.js' -Quiet)) {
        $html = Get-Content -Path $indexFile -Raw
        $html = $html -replace '</head>', "  <script src=`"arcviewer-env.js`"></script>`n</head>"
        Set-Content -Path $indexFile -Value $html -NoNewline
    }
}

if (Test-Path $configFile) {
    $parts = (Get-Content $configFile -Raw).Trim() -split '\s+'
    $platform = $parts[0]
    $mode = $parts[1]
} else {
    $platform = 'webgl'
    $mode = 'release'
}

$artifact = Get-Artifact $platform
if ($Rebuild -or -not (Test-Path $artifact)) {
    & (Join-Path $PSScriptRoot 'build.ps1') $platform $mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

switch ($platform) {
    'webgl' {
        $serveDir = Join-Path $proj 'Build'
        Write-WebGLEnv $serveDir
        Write-Host "serving $serveDir at http://localhost:$Port"

        $listener = [System.Net.HttpListener]::new()
        $listener.Prefixes.Add("http://localhost:$Port/")
        $listener.Start()

        $mime = @{
            '.html' = 'text/html; charset=utf-8'
            '.htm'  = 'text/html; charset=utf-8'
            '.js'   = 'application/javascript'
            '.json' = 'application/json'
            '.css'  = 'text/css'
            '.wasm' = 'application/wasm'
            '.data' = 'application/octet-stream'
            '.png'  = 'image/png'
            '.jpg'  = 'image/jpeg'
            '.jpeg' = 'image/jpeg'
            '.svg'  = 'image/svg+xml'
            '.ico'  = 'image/x-icon'
            '.txt'  = 'text/plain; charset=utf-8'
        }

        try {
            while ($listener.IsListening) {
                $ctx = $listener.GetContext()
                try {
                    $req = $ctx.Request
                    $res = $ctx.Response
                    $rel = [Uri]::UnescapeDataString($req.Url.AbsolutePath.TrimStart('/'))
                    if (-not $rel) { $rel = 'index.html' }
                    $path = Join-Path $serveDir $rel
                    if ((Test-Path $path -PathType Container)) {
                        $path = Join-Path $path 'index.html'
                    }
                    if (-not (Test-Path $path -PathType Leaf)) {
                        $res.StatusCode = 404
                        $res.Close()
                        continue
                    }
                    $ext = [IO.Path]::GetExtension($path).ToLowerInvariant()
                    if ($ext -eq '.br') {
                        $res.AddHeader('Content-Encoding', 'br')
                        $inner = [IO.Path]::GetExtension([IO.Path]::GetFileNameWithoutExtension($path)).ToLowerInvariant()
                        $res.ContentType = if ($mime.ContainsKey($inner)) { $mime[$inner] } else { 'application/octet-stream' }
                    } elseif ($ext -eq '.gz') {
                        $res.AddHeader('Content-Encoding', 'gzip')
                        $inner = [IO.Path]::GetExtension([IO.Path]::GetFileNameWithoutExtension($path)).ToLowerInvariant()
                        $res.ContentType = if ($mime.ContainsKey($inner)) { $mime[$inner] } else { 'application/octet-stream' }
                    } else {
                        $res.ContentType = if ($mime.ContainsKey($ext)) { $mime[$ext] } else { 'application/octet-stream' }
                    }
                    $bytes = [IO.File]::ReadAllBytes($path)
                    $res.ContentLength64 = $bytes.Length
                    $res.OutputStream.Write($bytes, 0, $bytes.Length)
                    $res.Close()
                } catch {
                    try { $ctx.Response.StatusCode = 500; $ctx.Response.Close() } catch {}
                }
            }
        } finally {
            $listener.Stop()
        }
    }
    'win64' {
        & $artifact
    }
    'macos' {
        Write-Error "macOS .app cannot be launched from Windows"
        exit 1
    }
    'linux64' {
        Write-Error "Linux binary cannot be launched from Windows"
        exit 1
    }
}
