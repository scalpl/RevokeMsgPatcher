param([string]$OriginalDll)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$output = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$sdk = (& dotnet --list-sdks | Select-Object -Last 1)
if ($sdk -notmatch '^([^ ]+) \[(.+)\]$') { throw 'A .NET SDK is required.' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '\Roslyn\bincore\csc.dll')
$arguments = @('/nologo','/noconfig','/nostdlib+','/target:exe','/platform:x64',"/out:$output\RegressionTests.exe")
foreach ($name in @('mscorlib','System','System.Core','System.Web.Extensions','System.Xml','System.Xml.Linq')) { $arguments += "/r:$framework\$name.dll" }
foreach ($name in @('BusinessException.cs','Matcher\ModifyFinder.cs','Matcher\FuzzyMatcher.cs','Matcher\BoyerMooreMatcher.cs','Model\Bag.cs','Model\App.cs','Model\TargetInfo.cs','Model\ModifyInfo.cs','Model\CommonModifyInfo.cs','Model\ReplacePattern.cs','Model\Change.cs','Utils\FileUtil.cs','Utils\VersionUtil.cs')) { $arguments += Join-Path $repo "RevokeMsgPatcher\$name" }
$arguments += Join-Path $PSScriptRoot 'RegressionTests.cs'
& dotnet $compiler @arguments
if ($LASTEXITCODE) { throw 'Test build failed.' }
& "$output\RegressionTests.exe" $repo $OriginalDll
if ($LASTEXITCODE) { throw 'Regression tests failed.' }
