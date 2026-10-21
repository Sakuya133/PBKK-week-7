$ErrorActionPreference = 'Continue'
$appDir = $PSScriptRoot
$desktopExe = Join-Path $appDir 'PosDesktop.exe'
$stateDir = Join-Path $env:LOCALAPPDATA 'POS Desktop'
$logDir = Join-Path $stateDir 'Logs'
$logFile = Join-Path $logDir 'posdesktop.log'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

try {
    if (-not (Test-Path $desktopExe)) { throw "Executable tidak ditemukan: $desktopExe" }
    # The downloadable demo uses local SQLite and needs no server configuration.
    Remove-Item Env:POS_CONNECTION_STRING -ErrorAction SilentlyContinue
    $env:POS_DESKTOP_MODE = 'offline'

    $client = Start-Process -FilePath $desktopExe -WorkingDirectory $appDir -PassThru -Wait
    exit $client.ExitCode
}
catch {
    Write-Error $_
    Add-Content -Path $logFile -Value "$(Get-Date -Format o) $_" -ErrorAction SilentlyContinue
    exit 1
}
