param(
    [switch]$Package,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$version = '1.0.12'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$source = Join-Path $root 'Source\App'
$release = Join-Path $root ("Release_v{0}" -f $version)
$packageDir = Join-Path $root 'Package'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF'

if (-not (Test-Path -LiteralPath $csc)) { throw 'Missing .NET Framework 4.8 C# compiler.' }
if (-not $SkipTests) { & (Join-Path $PSScriptRoot 'Test.ps1') }

$resolvedRoot = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
$resolvedRelease = [IO.Path]::GetFullPath($release).TrimEnd('\') + '\'
if (-not $resolvedRelease.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe release path.' }
if (Test-Path -LiteralPath $release) { Remove-Item -LiteralPath $release -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $release 'lib'), (Join-Path $release 'Reports') | Out-Null

$files = Get-ChildItem -LiteralPath $source -Filter '*.cs' | ForEach-Object { $_.FullName }
$references = @(
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Management.dll', '/r:System.Web.Extensions.dll', '/r:System.Xaml.dll',
    "/r:$wpf\WindowsBase.dll", "/r:$wpf\PresentationCore.dll", "/r:$wpf\PresentationFramework.dll"
)
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', "/out:$release\ROG-LiquidMetal-Inspector.exe", "/win32manifest:$source\app.manifest") + $references + $files
& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "Compilation failed, exit code: $LASTEXITCODE" }

$lhm = Join-Path $root 'Source\LibreHardwareMonitor'
if (-not (Test-Path -LiteralPath (Join-Path $lhm 'LibreHardwareMonitorLib.dll'))) {
    $cache = Join-Path $root 'Source\.cache'
    $archive = Join-Path $cache 'LibreHardwareMonitor-v0.9.6.zip'
    $lhm = Join-Path $cache 'LibreHardwareMonitor-v0.9.6'
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    if (-not (Test-Path -LiteralPath $archive)) {
        Write-Host 'Downloading LibreHardwareMonitor v0.9.6 from the official GitHub release...'
        Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.6/LibreHardwareMonitor.zip' -OutFile $archive
    }
    if (-not (Test-Path -LiteralPath (Join-Path $lhm 'LibreHardwareMonitorLib.dll'))) {
        if (Test-Path -LiteralPath $lhm) { Remove-Item -LiteralPath $lhm -Recurse -Force }
        Expand-Archive -LiteralPath $archive -DestinationPath $lhm -Force
    }
}
Get-ChildItem -LiteralPath $lhm -Filter '*.dll' | Copy-Item -Destination (Join-Path $release 'lib') -Force
Get-ChildItem -LiteralPath (Join-Path $root 'Source\Config') -Filter '*.json' | Copy-Item -Destination $release -Force
$profiles = Join-Path $root 'Source\Profiles'
if (Test-Path -LiteralPath $profiles) { Copy-Item -LiteralPath $profiles -Destination $release -Recurse -Force }
Get-ChildItem -LiteralPath (Join-Path $root 'Docs') -Filter '*.md' | Copy-Item -Destination $release -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $release -Force

if ($Package) {
    New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
    $zip = Join-Path $packageDir ("ROG-LiquidMetal-Inspector_v{0}_win-x64.zip" -f $version)
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $release '*') -DestinationPath $zip -Force
    Write-Host "Package created: $zip"
}
Write-Host "Build completed: $release\ROG-LiquidMetal-Inspector.exe"
