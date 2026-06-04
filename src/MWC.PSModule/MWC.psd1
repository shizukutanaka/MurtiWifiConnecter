@{
    # モジュール識別
    RootModule        = 'MWC.psm1'
    ModuleVersion     = '2.0.1'
    GUID              = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890'
    Author            = 'ShizukuTanaka'
    CompanyName       = 'ShizukuTanaka'
    Copyright         = '(c) 2026 ShizukuTanaka. MIT License.'
    Description       = 'PowerShell module for MWC — Multi WiFi Connector. Manage Wi-Fi from the command line.'
    PowerShellVersion = '7.0'

    # 依存
    RequiredModules   = @()
    RequiredAssemblies = @(
        'MWC.Core.dll',
        'MWC.Platform.Windows.dll'
    )

    # エクスポートする関数
    FunctionsToExport = @(
        'Get-WifiAdapter',
        'Get-WifiNetwork',
        'Connect-WifiNetwork',
        'Disconnect-WifiNetwork',
        'Get-WifiQuality',
        'New-WifiQrCode',
        'Export-WifiScan',
        'Get-WifiAdapterPreference',
        'Set-WifiAdapterLabel',
        'Set-WifiAdapterBand',
        'Add-WifiPin',
        'Remove-WifiPin',
        'Get-WifiHistory'
    )

    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @('gwifi', 'cwifi', 'dwifi')

    PrivateData = @{
        PSData = @{
            Tags         = @('wifi', 'wireless', 'network', 'wpa3', 'scanner')
            LicenseUri   = 'https://github.com/shizukutanaka/MurtiWifiConnecter/blob/main/LICENSE'
            ProjectUri   = 'https://github.com/shizukutanaka/MurtiWifiConnecter'
            ReleaseNotes = 'https://github.com/shizukutanaka/MurtiWifiConnecter/blob/main/CHANGELOG.md'
        }
    }
}
