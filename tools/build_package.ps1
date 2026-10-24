param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& $Dotnet build (Join-Path $root 'Yamaha.Loupedeck') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed' }
$package = Join-Path $root 'dist\YamahaDirect'
New-Item -ItemType Directory -Path (Join-Path $package 'bin') -Force | Out-Null
Copy-Item -Path (Join-Path $root 'package\*') -Destination $package -Recurse -Force
$build = Join-Path $root 'Yamaha.Loupedeck\bin\Release'
foreach ($name in @('YamahaDirectPlugin.dll','YamahaDirectPlugin.deps.json','Yamaha.Core.dll')) {
    Copy-Item -LiteralPath (Join-Path $build $name) -Destination (Join-Path $package 'bin') -Force
}
New-Item -ItemType Directory -Path (Join-Path $package 'licenses') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'icons\LICENSE') -Destination (Join-Path $package 'licenses\Lucide.txt') -Force
if (Test-Path (Join-Path $root 'LICENSE')) { Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $package 'licenses\Project.txt') -Force }
Write-Output "Plugin package directory: $package"
