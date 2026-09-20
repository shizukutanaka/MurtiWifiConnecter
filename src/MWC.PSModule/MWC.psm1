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

# JSON 出力対応コマンド (adapter list / scan / quality / history) は -Json を付ける。
# その他のコマンドは --json オプションを持たないため素通しで呼ぶ。
function Invoke-Mwc {
    param(
        [Parameter(Mandatory, ValueFromRemainingArguments)]
        [string[]] $CmdArgs,
        [switch] $Json
    )
    Initialize-MwcCli
    if ($Json) { $CmdArgs += '--json' }
    $out = & $script:MwcCli @CmdArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "mwc エラー: $out" }
    if ($Json) { return $out | ConvertFrom-Json }
    return $out
}

# 位置引数の adapter は省略不可 — 未指定時は先頭アダプターを解決する。
function Resolve-MwcAdapter {
    param([Guid] $AdapterId)
    if ($AdapterId -ne [Guid]::Empty) { return $AdapterId.ToString() }
    $ads = @(Invoke-Mwc 'adapter' 'list' -Json)
    if ($ads.Count -eq 0) { throw "Wi-Fi アダプターが見つかりません。" }
    return $ads[0].name
}

# ═══════════════════════════════════════════════
#  アダプター
# ═══════════════════════════════════════════════

<#
.SYNOPSIS
    Wi-Fi アダプター一覧を取得します。
.EXAMPLE
    Get-WifiAdapter
    Get-WifiAdapter | Where-Object { $_.state -eq 'Connected' }
#>
function Get-WifiAdapter {
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param()
    Invoke-Mwc 'adapter' 'list' -Json
}
New-Alias -Name gwifi -Value Get-WifiAdapter -Force

<#
.SYNOPSIS
    アダプターのネットワーク設定(バンド・ピン・ラベル)を取得します。
.PARAMETER AdapterId
    アダプター ID。省略時は全アダプターの設定を返します。
.EXAMPLE
    Get-WifiAdapterPreference
    Get-WifiAdapterPreference -AdapterId 'xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx'
#>
function Get-WifiAdapterPreference {
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param(
        [Guid] $AdapterId
    )
    $ads = @(Invoke-Mwc 'adapter' 'list' -Json)
    if ($AdapterId -ne [Guid]::Empty) {
        return $ads | Where-Object { $_.id -eq $AdapterId.ToString() }
    }
    return $ads
}

<#
.SYNOPSIS
    アダプターにカスタムラベルを設定します。
.PARAMETER Label
    設定するラベル文字列。
.PARAMETER AdapterId
    対象アダプター ID。省略時は先頭のアダプター。
.EXAMPLE
    Set-WifiAdapterLabel -Label 'USB-WiFi'
#>
function Set-WifiAdapterLabel {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] [string] $Label,
        [Guid] $AdapterId
    )
    if ($PSCmdlet.ShouldProcess($Label, 'ラベル設定')) {
        Invoke-Mwc 'adapter' 'rename' (Resolve-MwcAdapter $AdapterId) $Label | Out-Null
        Write-Host "ラベル設定: $Label" -ForegroundColor Green
    }
}

<#
.SYNOPSIS
    アダプターの優先バンドを設定します。
.PARAMETER Band
    Any / 2.4GHz / 5GHz / 6GHz。
.PARAMETER AdapterId
    対象アダプター ID。省略時は先頭のアダプター。
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
    # CLI は 'any | 2.4 | 5 | 6' を受け付ける
    $cliBand = @{ Any = 'any'; '2.4GHz' = '2.4'; '5GHz' = '5'; '6GHz' = '6' }[$Band]
    if ($PSCmdlet.ShouldProcess($Band, 'バンド設定')) {
        Invoke-Mwc 'adapter' 'band' (Resolve-MwcAdapter $AdapterId) $cliBand | Out-Null
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
    $cmd = @('scan')
    if ($AdapterId -ne [Guid]::Empty) { $cmd += '--adapter', $AdapterId.ToString() }
    $nets = @(Invoke-Mwc @cmd -Json)
    if ($Band -eq 'Any') { return $nets }
    # scan には --band オプションがないためクライアント側でフィルター
    $prefix = @{ '2.4GHz' = '2.4'; '5GHz' = '5'; '6GHz' = '6' }[$Band]
    return $nets | Where-Object { "$($_.Band)".StartsWith($prefix) }
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
        # connect は ssid が位置引数。成功時は JSON ({ssid,internet,captive}) を出力する
        $cmd = @('connect', $Ssid)
        if ($Passphrase)                  { $cmd += '-p', $Passphrase }
        if ($Auth)                        { $cmd += '--auth', $Auth }
        if ($Username)                    { $cmd += '--username', $Username }
        if ($AdapterId -ne [Guid]::Empty) { $cmd += '--adapter', $AdapterId.ToString() }
        try {
            $result = (Invoke-Mwc @cmd) | ConvertFrom-Json
        }
        catch {
            Write-Warning "接続失敗: $_"
            return
        }
        Write-Host "✓ 接続しました: $($result.ssid)" -ForegroundColor Green
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
        $cmd = @('disconnect')
        if ($AdapterId -ne [Guid]::Empty) { $cmd += '--adapter', $AdapterId.ToString() }
        Invoke-Mwc @cmd | Out-Null
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
    [OutputType([PSCustomObject])]
    param()
    Invoke-Mwc 'quality' -Json
}

<#
.SYNOPSIS
    接続履歴を取得します。
.PARAMETER Last
    取得する最大件数。
.EXAMPLE
    Get-WifiHistory -Last 20
#>
function Get-WifiHistory {
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param([int] $Last = 10)
    Invoke-Mwc 'history' '--limit' $Last -Json
}

<#
.SYNOPSIS
    スキャン結果をファイルにエクスポートします。
.PARAMETER Path
    出力先ファイルパス。
.PARAMETER Format
    出力形式 (Csv / Json)。
.EXAMPLE
    Export-WifiScan -Path './scan.json' -Format Json
#>
function Export-WifiScan {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [ValidateSet('Csv', 'Json')] [string] $Format = 'Csv',
        [Guid] $AdapterId
    )
    if ($PSCmdlet.ShouldProcess($Path, 'エクスポート')) {
        $cmd = @('export', '--output', $Path, '--format', $Format.ToLower())
        if ($AdapterId -ne [Guid]::Empty) { $cmd += '--adapter', $AdapterId.ToString() }
        Invoke-Mwc @cmd | Out-Null
        Write-Host "エクスポート: $Path" -ForegroundColor Green
    }
}

<#
.SYNOPSIS
    Wi-Fi QR コード用の WIFI: スキーム URI を生成します。
.PARAMETER Ssid
    対象 SSID。
.PARAMETER Passphrase
    パスフレーズ(省略時はオープンネットワーク)。
.PARAMETER Path
    指定時は URI テキストをファイルに保存します。
.EXAMPLE
    New-WifiQrCode -Ssid 'Home' -Passphrase 'secret'
#>
function New-WifiQrCode {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string] $Ssid,
        [string] $Passphrase,
        [string] $Path
    )
    $cmd = @('qr', $Ssid)
    if ($Passphrase) { $cmd += '-p', $Passphrase }
    $uri = Invoke-Mwc @cmd
    if ($Path) {
        $uri | Set-Content -Path $Path -Encoding utf8
        Write-Host "WIFI: URI 保存: $Path" -ForegroundColor Green
    }
    return $uri
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
        Invoke-Mwc 'adapter' 'pin' (Resolve-MwcAdapter $AdapterId) $Ssid | Out-Null
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
        Invoke-Mwc 'adapter' 'unpin' (Resolve-MwcAdapter $AdapterId) $Ssid | Out-Null
        Write-Host "ピン解除: $Ssid" -ForegroundColor Yellow
    }
}
