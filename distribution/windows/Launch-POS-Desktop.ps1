$ErrorActionPreference = 'Continue'
$appDir = $PSScriptRoot
$desktopExe = Join-Path $appDir 'PosDesktop.exe'
$apiExe = Join-Path $appDir 'LocalApi\PosServer.exe'
$stateDir = Join-Path $env:LOCALAPPDATA 'POS Desktop'
$logDir = Join-Path $stateDir 'Logs'
$logFile = Join-Path $logDir 'posdesktop.log'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$apiProcess = $null

function Test-ApiHealth([string]$url) {
    try { $null = Invoke-RestMethod -Uri ($url.TrimEnd('/') + '/health') -TimeoutSec 2; return $true }
    catch { return $false }
}

try {
    if (-not (Test-Path $desktopExe)) { throw "Executable tidak ditemukan: $desktopExe" }
    if ($env:POS_START_LOCAL_API -eq '1' -or $env:POS_START_LOCAL_API -eq 'true') {
        $apiUrl = if ($env:POS_LOCAL_API_URL) { $env:POS_LOCAL_API_URL } else { 'http://127.0.0.1:5077/' }
        $env:POS_API_URL = $apiUrl
        if (Test-ApiHealth $apiUrl) {
            Write-Host "API lokal sudah aktif di $apiUrl; launcher tidak akan menghentikannya."
        } elseif (-not $env:POS_CONNECTION_STRING) {
            Write-Warning 'POS_CONNECTION_STRING belum diatur; API tidak dimulai. Login tetap dibuka.'
        } elseif (Test-Path $apiExe) {
            $env:ASPNETCORE_ENVIRONMENT = 'Development'
            $env:POS_API_ALLOW_HTTP_DEV = 'true'
            $env:ASPNETCORE_URLS = $apiUrl
            $stdout = Join-Path $logDir 'posserver.stdout.log'
            $stderr = Join-Path $logDir 'posserver.stderr.log'
            $apiProcess = Start-Process -FilePath $apiExe -WorkingDirectory (Split-Path $apiExe) -PassThru -NoNewWindow `
                -RedirectStandardOutput $stdout -RedirectStandardError $stderr
            $ready = $false
            for ($i = 0; $i -lt 30; $i++) {
                if (Test-ApiHealth $apiUrl) { $ready = $true; break }
                if ($apiProcess.HasExited) { break }
                Start-Sleep -Seconds 1
            }
            if (-not $ready) { Write-Warning "API lokal tidak siap di $apiUrl; login tetap dibuka." }
        } else {
            Write-Warning 'LocalApi\PosServer.exe tidak ditemukan; login tetap dibuka.'
        }
    }

    # Do not pass the SQL connection string into the desktop process.
    Remove-Item Env:POS_CONNECTION_STRING -ErrorAction SilentlyContinue

    $client = Start-Process -FilePath $desktopExe -WorkingDirectory $appDir -PassThru -Wait
    exit $client.ExitCode
}
catch {
    Write-Error $_
    Add-Content -Path $logFile -Value "$(Get-Date -Format o) $_" -ErrorAction SilentlyContinue
    exit 1
}
finally {
    if ($null -ne $apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $apiProcess.Id -Timeout 5 -ErrorAction SilentlyContinue
    }
}
