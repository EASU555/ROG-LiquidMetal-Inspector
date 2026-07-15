$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bin = Join-Path $root 'Source\Diagnostics\bin'
$output = Join-Path $bin 'AnalysisEngineTests.exe'
New-Item -ItemType Directory -Force -Path $bin | Out-Null

$files = @(
    (Join-Path $root 'Source\Diagnostics\AnalysisEngineTests.cs'),
    (Join-Path $root 'Source\App\Models.cs'),
    (Join-Path $root 'Source\App\RulesConfig.cs'),
    (Join-Path $root 'Source\App\MachineProfile.cs'),
    (Join-Path $root 'Source\App\NvidiaSmiTelemetry.cs'),
    (Join-Path $root 'Source\App\HistoryStore.cs'),
    (Join-Path $root 'Source\App\AnalysisEngine.cs'),
    (Join-Path $root 'Source\App\ReportWriter.cs')
)
& $csc /nologo /target:exe /platform:x64 "/out:$output" /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Xml.dll $files
if ($LASTEXITCODE -ne 0) { throw "Test compilation failed, exit code: $LASTEXITCODE" }
& $output
if ($LASTEXITCODE -ne 0) { throw "Analysis tests failed, exit code: $LASTEXITCODE" }
