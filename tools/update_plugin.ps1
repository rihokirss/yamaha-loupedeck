param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$service = Join-Path $env:LOCALAPPDATA 'Logi\LogiPluginService'
$plugin = Join-Path $service 'Plugins\YamahaDirect'
$apps = Join-Path $service 'Applications\Loupedeck20'
if (-not (Test-Path $plugin)) { throw 'YamahaDirect is not installed; use the initial installation guide' }
function TreeHashes([string]$Path) {
    $map = @{}
    Get-ChildItem -LiteralPath $Path -Recurse -File | ForEach-Object { $map[$_.FullName.Substring($Path.Length)] = (Get-FileHash -LiteralPath $_.FullName).Hash }
    return $map
}
function AssertSameProfiles($Expected) {
    $actual = TreeHashes $apps
    if ($Expected.Count -ne $actual.Count) { throw 'Application profile file count changed; no profile overwrite attempted' }
    foreach ($key in $Expected.Keys) { if ($Expected[$key] -ne $actual[$key]) { throw "Application profile changed: $key" } }
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $root "backups\yamaha-plugin-update-$stamp"
New-Item -ItemType Directory -Path $backup | Out-Null
Copy-Item -LiteralPath $plugin -Destination (Join-Path $backup 'Plugin') -Recurse
Copy-Item -LiteralPath $apps -Destination (Join-Path $backup 'Applications') -Recurse
$settings = Join-Path $env:LOCALAPPDATA 'YamahaDirect'
if (Test-Path $settings) { Copy-Item -LiteralPath $settings -Destination (Join-Path $backup 'Settings') -Recurse }
Copy-Item -LiteralPath (Join-Path $service 'LoupedeckSettings.ini') -Destination $backup
$profileHashes = TreeHashes $apps
& (Join-Path $PSScriptRoot 'build_package.ps1') -Dotnet $Dotnet
$uiRunning = [bool](Get-Process -Name Loupedeck -ErrorAction SilentlyContinue)
$serviceRunning = [bool](Get-Process -Name LogiPluginService -ErrorAction SilentlyContinue)
$agentRunning = [bool](Get-Process -Name logioptionsplus_agent -ErrorAction SilentlyContinue)
$copied = $false
try {
    Get-Process -Name logioptionsplus_agent -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-Process -Name Loupedeck -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-Process -Name LogiPluginServiceExt -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-Process -Name LogiPluginService -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    if (Get-Process -Name LogiPluginService -ErrorAction SilentlyContinue) { throw 'Service did not stop; plugin files were not changed' }
    AssertSameProfiles $profileHashes
    $copied = $true
    Copy-Item -Path (Join-Path $root 'dist\YamahaDirect\*') -Destination $plugin -Recurse -Force
} catch {
    if ($copied) { Copy-Item -Path (Join-Path $backup 'Plugin\*') -Destination $plugin -Recurse -Force }
    throw
} finally {
    if ($agentRunning) { Start-Process -FilePath 'C:\Program Files\LogiOptionsPlus\logioptionsplus_agent.exe' -WindowStyle Hidden; Start-Sleep -Seconds 2 }
    if ($serviceRunning -and -not (Get-Process -Name LogiPluginService -ErrorAction SilentlyContinue)) { Start-Process -FilePath 'C:\Program Files\Logi\LogiPluginService\LogiPluginService.exe' -WindowStyle Hidden }
    if ($uiRunning) { Start-Sleep -Seconds 3; Start-Process -FilePath 'C:\Program Files (x86)\Loupedeck\Loupedeck2\configui2\Loupedeck.exe' -WindowStyle Hidden }
}
AssertSameProfiles $profileHashes
Write-Output 'Updated YamahaDirect only; all CT application/profile files are unchanged'
Write-Output "Backup: $backup"
