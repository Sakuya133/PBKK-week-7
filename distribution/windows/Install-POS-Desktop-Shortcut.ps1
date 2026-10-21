$ErrorActionPreference = 'Stop'
$appDir = $PSScriptRoot
$launcher = Join-Path $appDir 'Launch-POS-Desktop.vbs'
$icon = Join-Path $appDir 'Assets\mikogomi.ico'
if (-not (Test-Path $launcher)) { throw "Launcher tidak ditemukan: $launcher" }

$desktop = [Environment]::GetFolderPath('DesktopDirectory')
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'MiKuGoMi POS'
New-Item -ItemType Directory -Force -Path $desktop, $startMenu | Out-Null
$shell = New-Object -ComObject WScript.Shell

foreach ($shortcutPath in @(
    (Join-Path $desktop 'MiKuGoMi POS.lnk'),
    (Join-Path $startMenu 'MiKuGoMi POS.lnk')
)) {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $launcher
    $shortcut.WorkingDirectory = $appDir
    $shortcut.Description = 'Buka aplikasi kasir MiKuGoMi POS'
    if (Test-Path $icon) { $shortcut.IconLocation = "$icon,0" }
    $shortcut.Save()
    Write-Host "Shortcut dibuat: $shortcutPath"
}
