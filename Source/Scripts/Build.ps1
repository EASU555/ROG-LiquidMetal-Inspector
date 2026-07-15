param(
    [switch]$Package,
    [switch]$SkipTests,
    [string]$SignCertificateThumbprint
)

$ErrorActionPreference = 'Stop'
$version = '1.2.2'
$lhmArchiveSha256 = '086D9F1B5A99E643EDC2CFAAAC16051685B551E4C5AC0B32A57C58C0E529C001'
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
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Management.dll', '/r:System.Web.Extensions.dll', '/r:System.Xml.dll', '/r:System.Xaml.dll',
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
    $actualArchiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash
    if ($actualArchiveHash -ne $lhmArchiveSha256) { throw "LibreHardwareMonitor archive SHA-256 mismatch. Expected $lhmArchiveSha256, got $actualArchiveHash" }
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

$executable = Join-Path $release 'ROG-LiquidMetal-Inspector.exe'
if ($SignCertificateThumbprint) {
    $certificate = Get-Item -LiteralPath ("Cert:\CurrentUser\My\{0}" -f $SignCertificateThumbprint) -ErrorAction Stop
    $signature = Set-AuthenticodeSignature -LiteralPath $executable -Certificate $certificate -HashAlgorithm SHA256
    if ($signature.Status -ne 'Valid') { throw "Authenticode signing failed: $($signature.StatusMessage)" }
    Write-Host "Executable signed with certificate $SignCertificateThumbprint"
} else {
    Write-Warning 'No signing certificate supplied. This local build is unsigned; SHA-256 checksums are generated below.'
}

$dependencyManifest = [ordered]@{
    product = 'ROG Liquid Metal Inspector'
    version = $version
    targetFramework = '.NET Framework 4.8'
    architecture = 'x64'
    dependencies = @(
        [ordered]@{ name = 'LibreHardwareMonitor'; version = '0.9.6'; source = 'official GitHub release'; archiveSha256 = $lhmArchiveSha256 },
        [ordered]@{ name = 'OpenCL'; version = 'system vendor runtime'; source = 'installed GPU driver' },
        [ordered]@{ name = 'nvidia-smi'; version = 'installed NVIDIA driver'; source = 'Windows System32'; optional = $true }
    )
}
$dependencyManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $release 'dependency-manifest.json') -Encoding UTF8

$checksumPath = Join-Path $release 'checksums.sha256'
$checksumLines = Get-ChildItem -LiteralPath $release -Recurse -File | Where-Object { $_.FullName -ne $checksumPath } | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($release.Length).TrimStart('\').Replace('\', '/')
    "{0}  {1}" -f (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant(), $relative
}
$checksumLines | Set-Content -LiteralPath $checksumPath -Encoding UTF8

if ($Package) {
    New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
    $zip = Join-Path $packageDir ("ROG-LiquidMetal-Inspector_v{0}_win-x64.zip" -f $version)
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $release '*') -DestinationPath $zip -Force
    ((Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLowerInvariant() + '  ' + (Split-Path -Leaf $zip)) | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ASCII
    Write-Host "Package created: $zip"
}
Write-Host "Build completed: $release\ROG-LiquidMetal-Inspector.exe"
