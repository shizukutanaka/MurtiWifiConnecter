$ErrorActionPreference = 'Stop'
$toolsDir = "$(Split-Path -parent $MyInvocation.MyCommand.Definition)"

$packageArgs = @{
  packageName    = 'mwc'
  fileType       = 'zip'
  softwareName   = 'MWC*'
  url64bit       = 'https://github.com/shizukutanaka/MurtiWifiConnecter/releases/download/v2.0.1/mwc-2.0.1-win-x64.zip'
  urlArm64       = 'https://github.com/shizukutanaka/MurtiWifiConnecter/releases/download/v2.0.1/mwc-2.0.1-win-arm64.zip'
  checksum64     = 'PLACEHOLDER_SHA256_x64'
  checksumArm64  = 'PLACEHOLDER_SHA256_arm64'
  checksumType   = 'sha256'
  unzipLocation  = $toolsDir
}

$architecture = if ([System.Environment]::Is64BitOperatingSystem) {
  if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
    '64bitArm'
  } else { '64bit' }
} else { throw "MWC requires a 64-bit Windows system." }

switch ($architecture) {
  '64bitArm' {
    Get-ChocolateyWebFile @packageArgs -Url $packageArgs.urlArm64 -Checksum $packageArgs.checksumArm64
  }
  default {
    Get-ChocolateyWebFile @packageArgs
  }
}

Install-ChocolateyZipPackage @packageArgs

# PATH に追加
$installDir = Join-Path $toolsDir "MWC"
Install-ChocolateyPath -PathToInstall $installDir -PathType 'Machine'

Write-Host "MWC installed. Run 'mwc' (CLI) or 'MWC.exe' (GUI)." -ForegroundColor Green
