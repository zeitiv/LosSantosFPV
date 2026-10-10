<#
.SYNOPSIS
  Builds LosSantosFpv.dll and packs dist\LosSantosFpv-<version>.zip (scripts\LosSantosFpv.dll + README.txt + LICENSE.txt).
.EXAMPLE
  .\build-release.ps1                                  # local build against your game folder's ScriptHookVDotNet3.dll
  .\build-release.ps1 -Version 1.2.0 -ShvdnDir C:\shvdn # what CI does
#>
param(
    [string]$Version = "1.0.0-dev",
    [string]$ShvdnDir = "",
    [string]$RepoUrl = "https://github.com/<owner>/<repo>"
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$props = @("-p:Version=$Version", "-p:CopyToGame=false")
if ($ShvdnDir) { $props += "-p:ShvdnDir=$ShvdnDir" }
dotnet build "$root\src\LosSantosFpv.csproj" -c Release @props | Out-Host
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$stage = Join-Path $root "dist\stage"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $stage "scripts") | Out-Null
Copy-Item (Join-Path $root "src\bin\Release\LosSantosFpv.dll") (Join-Path $stage "scripts")
$readme = (Get-Content (Join-Path $root "release\README.txt") -Raw).Replace("{{VERSION}}", $Version).Replace("{{REPO_URL}}", $RepoUrl)
[IO.File]::WriteAllText((Join-Path $stage "README.txt"), $readme.Replace("`r`n", "`n").Replace("`n", "`r`n"))
Copy-Item (Join-Path $root "LICENSE") (Join-Path $stage "LICENSE.txt")

$zip = Join-Path $root "dist\LosSantosFpv-$Version.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Remove-Item $stage -Recurse -Force
Write-Host "Packed $zip"
if ($env:GITHUB_OUTPUT) { "zip=$zip" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8 }
