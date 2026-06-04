# build-msix.ps1
# MWC MSIX パッケージビルドスクリプト
# 必要条件: Windows SDK (makeappx.exe, signtool.exe)

[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $Version       = "2.0.1.0",
    [string] $OutputDir     = "$PSScriptRoot\..\..\artifacts\msix",
    [string] $CertPath      = "",            # PFX 証明書パス
    [string] $CertPassword  = "",            # 証明書パスワード
    [switch] $SkipSign                       # CI では -SkipSign で署名スキップ
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root     = Resolve-Path "$PSScriptRoot\..\.."
$StagDir  = "$OutputDir\staging"
$PkgName  = "MWC_${Version}_x64.msix"

Write-Host "=== MWC MSIX ビルド ===" -ForegroundColor Cyan
Write-Host "  Version:       $Version"
Write-Host "  Configuration: $Configuration"
Write-Host "  Output:        $OutputDir"

# 1. ビルド
Write-Host "`n[1/4] ビルド中..." -ForegroundColor Yellow
dotnet publish "$Root\src\MWC.App\MWC.App.csproj" `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output "$StagDir" `
    -p:PublishSingleFile=false `
    -p:EnableCompressionInSingleFile=true

# 2. マニフェスト + アセット配置
Write-Host "`n[2/4] マニフェスト配置中..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path "$StagDir\Assets" | Out-Null
Copy-Item "$PSScriptRoot\Package.appxmanifest" "$StagDir\AppxManifest.xml" -Force

# バージョン番号をマニフェストに埋め込む
(Get-Content "$StagDir\AppxManifest.xml") `
    -replace 'Version="2\.0\.1\.0"', "Version=`"$Version`"" |
    Set-Content "$StagDir\AppxManifest.xml"

# プレースホルダーアセット生成(実際の PNG は別途用意)
$assets = @(
    "StoreLogo.png", "Square150x150Logo.png", "Square44x44Logo.png",
    "Square71x71Logo.png", "Square310x310Logo.png", "Wide310x150Logo.png",
    "SplashScreen.png", "BadgeLogo.png"
)
foreach ($a in $assets) {
    $dst = "$StagDir\Assets\$a"
    if (-not (Test-Path $dst)) {
        # 1x1 透明 PNG プレースホルダー (Base64)
        [System.IO.File]::WriteAllBytes($dst, [System.Convert]::FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="
        ))
    }
}

# 3. MSIX パック
Write-Host "`n[3/4] パック中..." -ForegroundColor Yellow
$MakeAppx = "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\makeappx.exe"
if (-not (Test-Path $MakeAppx)) {
    $MakeAppx = Get-Command makeappx.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
}
if (-not $MakeAppx) { throw "makeappx.exe が見つかりません。Windows SDK をインストールしてください。" }

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
& $MakeAppx pack /d "$StagDir" /p "$OutputDir\$PkgName" /o
if ($LASTEXITCODE -ne 0) { throw "makeappx.exe がエラーを返しました: $LASTEXITCODE" }

# 4. 署名
if (-not $SkipSign) {
    Write-Host "`n[4/4] 署名中..." -ForegroundColor Yellow
    if (-not (Test-Path $CertPath)) { throw "証明書が見つかりません: $CertPath" }
    $SignTool = "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
    & $SignTool sign /fd sha256 /a /f $CertPath /p $CertPassword "$OutputDir\$PkgName"
    if ($LASTEXITCODE -ne 0) { throw "signtool.exe がエラーを返しました: $LASTEXITCODE" }
} else {
    Write-Host "`n[4/4] 署名スキップ (-SkipSign)" -ForegroundColor DarkGray
}

Write-Host "`n✓ 完了: $OutputDir\$PkgName" -ForegroundColor Green
