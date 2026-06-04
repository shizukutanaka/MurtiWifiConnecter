#!/usr/bin/env pwsh
<#
.SYNOPSIS
    GitHub Releases から MSI をダウンロードして SHA256 を計算し、
    winget manifest を自動更新する。
.EXAMPLE
    .\tools\update-winget-manifest.ps1 -Version 1.5.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Repo     = "shizukutanaka/MurtiWifiConnecter",
    [string]$Manifest = "installer/winget/manifest.yaml"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$tag = "v$Version"
$base = "https://github.com/$Repo/releases/download/$tag"

$archs = @("win-x64", "win-arm64")
$hashes = @{}

foreach ($rid in $archs)
{
    $url  = "$base/MWC-$Version-$rid.msi"
    $tmp  = [System.IO.Path]::GetTempFileName()
    Write-Host "Downloading $url …"
    Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing
    $hash = (Get-FileHash $tmp -Algorithm SHA256).Hash
    $hashes[$rid] = $hash
    Remove-Item $tmp
    Write-Host "  SHA256 ($rid): $hash"
}

# YAML 更新
$yaml = Get-Content $Manifest -Raw

$yaml = $yaml -replace 
    "(?<=InstallerUrl:.*win-x64.*\n.*InstallerSha256: )REPLACE_WITH_ACTUAL_SHA256_AFTER_RELEASE",
    $hashes["win-x64"]

$yaml = $yaml -replace 
    "(?<=InstallerUrl:.*win-arm64.*\n.*InstallerSha256: )REPLACE_WITH_ACTUAL_SHA256_AFTER_RELEASE",
    $hashes["win-arm64"]

# バージョン更新
$yaml = $yaml -replace "PackageVersion: .*", "PackageVersion: $Version"
$yaml = $yaml -replace "v\d+\.\d+\.\d+", $tag

Set-Content -Path $Manifest -Value $yaml -Encoding utf8
Write-Host "Updated: $Manifest"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  git add $Manifest"
Write-Host "  git commit -m 'chore: winget manifest v$Version'"
Write-Host "  # Submit PR to https://github.com/microsoft/winget-pkgs"
