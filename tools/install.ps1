param(
    [Parameter(Mandatory)][string]$ReceiverHost,
    [Parameter(Mandatory)][string]$SystemId,
    [string]$Python = 'python',
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$serviceRoot = Join-Path $env:LOCALAPPDATA 'Logi\LogiPluginService'
$appRoot = Join-Path $serviceRoot 'Applications\Loupedeck20\@_defaultwin'
$profileId = (Get-Content (Join-Path $appRoot 'ApplicationInfo.json') -Raw | ConvertFrom-Json).defaultProfileName
$profileDir = Join-Path $appRoot "Profiles\$profileId"
$settingsDir = Join-Path $env:LOCALAPPDATA 'YamahaDirect'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $root "backups\yamaha-install-$stamp"
New-Item -ItemType Directory -Path $backup | Out-Null
Copy-Item -LiteralPath $appRoot -Destination (Join-Path $backup 'System') -Recurse
$codexInfo = Join-Path $serviceRoot 'Applications\Loupedeck20\chatgpt\ApplicationInfo.json'
if (Test-Path $codexInfo) { Copy-Item -LiteralPath (Split-Path $codexInfo -Parent) -Destination (Join-Path $backup 'Codex') -Recurse }
Copy-Item -LiteralPath (Join-Path $serviceRoot 'LoupedeckSettings.ini') -Destination $backup
if (Test-Path (Join-Path $serviceRoot 'Plugins')) { Copy-Item -LiteralPath (Join-Path $serviceRoot 'Plugins') -Destination (Join-Path $backup 'Plugins') -Recurse }
if (Test-Path $settingsDir) { Copy-Item -LiteralPath $settingsDir -Destination (Join-Path $backup 'YamahaSettings') -Recurse }
& (Join-Path $PSScriptRoot 'build_package.ps1') -Dotnet $Dotnet
& $Python (Join-Path $PSScriptRoot 'prepare_profile.py')
if ($LASTEXITCODE -ne 0) { throw 'Profile staging failed; live store was not changed' }
$profileBefore = (Get-FileHash (Join-Path $profileDir 'ProfileInfo.json')).Hash
$codexBefore = if (Test-Path $codexInfo) { (Get-FileHash $codexInfo).Hash } else { $null }
$uiWasRunning = [bool](Get-Process -Name Loupedeck -ErrorAction SilentlyContinue)
$serviceWasRunning = [bool](Get-Process -Name LogiPluginService -ErrorAction SilentlyContinue)
$watchdogWasRunning = [bool](Get-Process -Name logioptionsplus_agent -ErrorAction SilentlyContinue)
Get-Process -Name logioptionsplus_agent -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name Loupedeck -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name LogiPluginServiceExt -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name LogiPluginService -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
try {
    if (Get-Process -Name LogiPluginService -ErrorAction SilentlyContinue) { throw 'Service still running; no live files changed' }
    if ((Get-FileHash (Join-Path $profileDir 'ProfileInfo.json')).Hash -ne $profileBefore) { throw 'Profile changed during staging; re-run installation' }
    $pluginDir = Join-Path $serviceRoot 'Plugins\YamahaDirect'
    New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
    Copy-Item -Path (Join-Path $root 'dist\YamahaDirect\*') -Destination $pluginDir -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $root '.tmp\yamaha-profile\ProfileInfo.json') -Destination (Join-Path $profileDir 'ProfileInfo.json') -Force
    New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
    if (-not (Test-Path (Join-Path $settingsDir 'settings.json'))) {
        $legacy = Join-Path $env:LOCALAPPDATA 'CodexController\Yamaha\settings.json'
        if (Test-Path $legacy) { Copy-Item -LiteralPath $legacy -Destination (Join-Path $settingsDir 'settings.json') }
        else {
            $settings = Get-Content (Join-Path $root 'settings.example.json') -Raw | ConvertFrom-Json
            $settings.Host = $ReceiverHost; $settings.SystemId = $SystemId
            $settings | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $settingsDir 'settings.json') -Encoding utf8
        }
    }
} catch {
    Copy-Item -LiteralPath (Join-Path $backup "System\Profiles\$profileId\ProfileInfo.json") -Destination (Join-Path $profileDir 'ProfileInfo.json') -Force
    if (Test-Path (Join-Path $backup 'Plugins\YamahaDirect')) {
        Copy-Item -Path (Join-Path $backup 'Plugins\YamahaDirect\*') -Destination (Join-Path $serviceRoot 'Plugins\YamahaDirect') -Recurse -Force
    }
    if (Test-Path (Join-Path $backup 'YamahaSettings')) {
        Copy-Item -Path (Join-Path $backup 'YamahaSettings\*') -Destination $settingsDir -Recurse -Force
    }
    throw
} finally {
    if ($watchdogWasRunning) { Start-Process -FilePath 'C:\Program Files\LogiOptionsPlus\logioptionsplus_agent.exe' -WindowStyle Hidden }
    if ($watchdogWasRunning) { Start-Sleep -Seconds 2 }
    if ($serviceWasRunning -and -not (Get-Process -Name LogiPluginService -ErrorAction SilentlyContinue)) { Start-Process -FilePath 'C:\Program Files\Logi\LogiPluginService\LogiPluginService.exe' -WindowStyle Hidden }
    if ($uiWasRunning) { Start-Sleep -Seconds 4; Start-Process -FilePath 'C:\Program Files (x86)\Loupedeck\Loupedeck2\configui2\Loupedeck.exe' -WindowStyle Hidden }
}
if ($codexBefore -and (Get-FileHash $codexInfo).Hash -ne $codexBefore) { throw 'Unexpected Codex application change' }
Write-Output "Installed Yamaha workspace. Backup: $backup"
