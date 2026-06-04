#Requires -Version 7.0
# MWC.psm1 — Multi WiFi Connector PowerShell Module
# mwc CLI のラッパー + 型安全な出力

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# mwc CLI のパスを解決
$script:MwcCli = $null
function Initialize-MwcCli {
    if ($script:MwcCli) { return }
    $candidates = @(
        (Join-Path $PSScriptRoot '..' 'mwc.exe'),
        (Get-Command 'mwc' -ErrorAction SilentlyContinue)?.Source,
        (Join-Path $env:ProgramFiles 'MWC' 'mwc.exe')
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { $script:MwcCli = $c; return }
    }
    throw "mwc.exe が見つかりません。MWC をインストールしてください。"
}

function Invoke-Mwc {
    param([string[]]$Args)
    Initialize-MwcCli
    $json = & $script:MwcCli @Args '--output' 'json' 2>&1
    if ($LASTEXITCODE -ne 0) { throw "mwc エラー: $json" }
    return $json | ConvertFrom-Json
}

# ═══════════════════════════════════════════════
#  アダプター
# ═══════════════════════════════════════════════

<#
.SYNOPSIS
    Wi-Fi アダプター一覧を取得します。
.EXAMPLE
    Get-WifiAdapter
    Get-WifiAdapter | Where-Object { $_.State -eq 'Connected' }
#>
function Get-WifiAdapter {
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param()
    Invoke-Mwc 'adapter', 'list'
}
New-Alias -Name gwifi -Value Get-WifiAdapter -Force

<#
.SYNOPSIS
    現在のアダプターのネットワーク設定を取得します。
.PARAMETER AdapterId
    アダプター ID。省略時はデフォルトアダプターを使用します。
.EXAMPLE
    Get-WifiAdapterPreference
    Get-WifiAdapterPreference -AdapterId 'xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx'
#>
function Get-WifiAdapterPreference {
    [CmdletBinding()]
    param(
        [Guid] $AdapterId
    )
    $args = @('adapter', 'pref')
    if ($AdapterId -ne [Guid]::Empty) { $args += '--id', $AdapterId }
    Invoke-Mwc $args
}

<#
.SYNOPSIS
    アダプターの表示名を設定します。
.EXAMPLE
    Set-WifiAdapterLabel -Label '自宅用ドングル'
#>
function Set-WifiAdapterLabel {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] [string] $Label,
        [Guid] $AdapterId
    )
    if ($PSCmdlet.ShouldProcess($Label, 'ラベルを設定')) {
        $args = @('adapter', 'rename', '--label', $Label)
        if ($AdapterId -ne [Guid]::Empty) { $args += '--id', $AdapterId }
        Invoke-Mwc $args | Out-Null
        Write-Host "ラベル設定: $Label" -ForegroundColor Green
    }
}

<#
.SYNOPSIS
    アダプターのバンドフィルターを設定します。
.PARAMETER Band
    Any / 2.4GHz / 5GHz / 6GHz
.EXAMPLE
    Set-WifiAdapterBand -Band '5GHz'
#>
function Set-WifiAdapterBand {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Any', '2.4GHz', '5GHz', '6GHz')]
        [string] $Band,
        [Guid] $AdapterId
    )
    if ($PSCmdlet.ShouldProcess($Band, 'バンド設定')) {
        $args = @('adapter', 'band', '--band', $Band)
        if ($AdapterId -ne [Guid]::Empty) { $args += '--id', $AdapterId }
        Invoke-Mwc $args | Out-Null
        Write-Host "バンド設定: $Band" -ForegroundColor Green
    }
}

# ═══════════════════════════════════════════════
#  スキャン / 接続
# ═══════════════════════════════════════════════

<#
.SYNOPSIS
    Wi-Fi ネットワークをスキャンして一覧を返します。
.PARAMETER Band
    フィルターするバンド (Any / 2.4GHz / 5GHz / 6GHz)。
.EXAMPLE
    Get-WifiNetwork
    Get-WifiNetwork -Band '5GHz' | Sort-Object -Property SignalQuality -Descending
    Get-WifiNetwork | Where-Object { $_.Auth -like 'WPA3*' }
#>
function Get-WifiNetwork {
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param(
        [ValidateSet('Any', '2.4GHz', '5GHz', '6GHz')]
        [string] $Band = 'Any',
        [Guid] $AdapterId
    )
    $args = @('scan')
    if ($Band -ne 'Any') { $args += '--band', $Band }
    if ($AdapterId -ne [Guid]::Empty) { $args += '--id', $AdapterId }
    Invoke-Mwc $args
}

<#
.SYNOPSIS
    Wi-Fi ネットワークに接続します。
.EXAMPLE
    Connect-WifiNetwork -Ssid 'MyHome' -Passphrase 'secret'
    Connect-WifiNetwork -Ssid 'Corp' -Auth WPA2Enterprise -Username 'user@corp.com'
#>
function Connect-WifiNetwork {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] [string] $Ssid,
        [string] $Passphrase,
        [ValidateSet('WPA3SAE', 'WPA2PSK', 'WPA2Enterprise', 'Open')]
        [string] $Auth,
        [string] $Username,
        [Guid] $AdapterId
    )
    if ($PSCmdlet.ShouldProcess($Ssid, '接続')) {
        $args = @('connect', '--ssid', $Ssid)
        if ($Passphrase)                        { $args += '--pass', $Passphrase }
        if ($Auth)                              { $args += '--auth', $Auth }
        if ($Username)                          { $args += '--user', $Username }
        if ($AdapterId -ne [Guid]::Empty)       { $args += '--id', $AdapterId }
        $result = Invoke-Mwc $args
        if ($result.Success) { Write-Host "✓ 接続しました: $Ssid" -ForegroundColor Green }
        else { Write-Warning "接続失敗: $($result.FailureReason)" }
        return $result
    }
}
New-Alias -Name cwifi -Value Connect-WifiNetwork -Force

<#
.SYNOPSIS
    Wi-Fi を切断します。
.EXAMPLE
    Disconnect-WifiNetwork
#>
function Disconnect-WifiNetwork {
    [CmdletBinding(SupportsShouldProcess)]
    param([Guid] $AdapterId)
    if ($PSCmdlet.ShouldProcess('Wi-Fi', '切断')) {
        $args = @('disconnect')
        if ($AdapterId -ne [Guid]::Empty) { $args += '--id', $AdapterId }
        Invoke-Mwc $args | Out-Null
        Write-Host "切断しました" -ForegroundColor Yellow
    }
}
New-Alias -Name dwifi -Value Disconnect-WifiNetwork -Force

# ═══════════════════════════════════════════════
#  品質 / 履歴 / エクスポート
# ═══════════════════════════════════════════════

<#
.SYNOPSIS
    現在のネットワーク品質を計測します。
.EXAMPLE
    Get-WifiQuality
    Get-WifiQuality | Select-Object LatencyMs, LossPercent, Grade
#>
function Get-WifiQuality {
    [CmdletBinding()]
    Invoke-Mwc 'quality'
}

<#
.SYNOPSIS
    接続履歴を取得します。
.EXAMPLE
    Get-WifiHistory -Last 20
#>
function Get-WifiHistory {
    [CmdletBinding()]
    param([int] $Last = 10)
    Invoke-Mwc 'history', '--last', $Last
}

<#
.SYNOPSIS
    スキャン結果をファイルに書き出します。
.EXAMPLE
    Export-WifiScan -Path './scan.csv' -Format CSV
    Export-WifiScan -Path './scan.json' -Format JSON
#>
function Export-WifiScan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [ValidateSet('CSV', 'JSON', 'TXT')] [string] $Format = 'CSV'
    )
    $args = @('export', '--path', $Path, '--format', $Format.ToLower())
    Invoke-Mwc $args | Out-Null
    Write-Host "エクスポート完了: $Path" -ForegroundColor Green
}

<#
.SYNOPSIS
    選択したネットワークの Wi-Fi QR コード(WIFI:スキーム)を生成します。
.EXAMPLE
    New-WifiQrCode -Ssid 'Home' -Passphrase 'secret' -Path './qr.png'
#>
function New-WifiQrCode {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Ssid,
        [string] $Passphrase,
        [string] $Path = ".\mwc-qr-${Ssid}.png"
    )
    $args = @('qr', '--ssid', $Ssid, '--path', $Path)
    if ($Passphrase) { $args += '--pass', $Passphrase }
    Invoke-Mwc $args | Out-Null
    Write-Host "QR コード生成: $Path" -ForegroundColor Green
}

# ═══════════════════════════════════════════════
#  ピン留め
# ═══════════════════════════════════════════════

<#
.SYNOPSIS
    SSID をアダプターのピン済みネットワークに追加します。
.EXAMPLE
    Add-WifiPin -Ssid 'HomeNetwork'
#>
function Add-WifiPin {
    [CmdletBinding(SupportsShouldProcess)]
    param([Parameter(Mandatory)] [string] $Ssid, [Guid] $AdapterId)
    if ($PSCmdlet.ShouldProcess($Ssid, 'ピン留め')) {
        $args = @('adapter', 'pin', '--ssid', $Ssid)
        if ($AdapterId -ne [Guid]::Empty) { $args += '--id', $AdapterId }
        Invoke-Mwc $args | Out-Null
        Write-Host "ピン留め: $Ssid" -ForegroundColor Green
    }
}

<#
.SYNOPSIS
    SSID のピン留めを解除します。
.EXAMPLE
    Remove-WifiPin -Ssid 'OldNetwork'
#>
function Remove-WifiPin {
    [CmdletBinding(SupportsShouldProcess)]
    param([Parameter(Mandatory)] [string] $Ssid, [Guid] $AdapterId)
    if ($PSCmdlet.ShouldProcess($Ssid, 'ピン解除')) {
        $args = @('adapter', 'unpin', '--ssid', $Ssid)
        if ($AdapterId -ne [Guid]::Empty) { $args += '--id', $AdapterId }
        Invoke-Mwc $args | Out-Null
        Write-Host "ピン解除: $Ssid" -ForegroundColor Yellow
    }
}
