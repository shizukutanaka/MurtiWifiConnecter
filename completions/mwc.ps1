# MWC CLI PowerShell completion
# インストール: . .\completions\mwc.ps1
#   または $PROFILE に追記

Register-ArgumentCompleter -Native -CommandName mwc -ScriptBlock {
    param($wordToComplete, $commandAst, $cursorPosition)

    $commands = @(
        'list', 'scan', 'connect', 'disconnect', 'import-cat', 'passpoint', 'privacy',
        'profile', 'qr', 'qr-parse', 'export',
        'quality', 'history', 'eap-stats', 'plan-channels', 'vpn-advice',
        'multi', 'adapter', 'help'
    )

    $commandElements = $commandAst.CommandElements
    $command  = if ($commandElements.Count -gt 1) { $commandElements[1].Value } else { '' }
    $subCmd   = if ($commandElements.Count -gt 2) { $commandElements[2].Value } else { '' }
    $depth    = $commandElements.Count

    # トップレベルコマンド補完
    if ($depth -le 1 -or ($depth -eq 2 -and $wordToComplete -ne '')) {
        $commands + @('--help', '--version') |
            Where-Object { $_ -like "$wordToComplete*" } |
            ForEach-Object {
                [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
            }
        return
    }

    # --band の値補完
    $prevToken = if ($commandElements.Count -gt 1) { $commandElements[-2].Value } else { '' }
    if ($prevToken -in @('--band') -and $command -eq 'plan-channels') {
        @('2.4', '5', '6') |
            Where-Object { $_ -like "$wordToComplete*" } |
            ForEach-Object {
                [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
            }
        return
    }

    # adapter band <name> <band> の band 値補完
    if ($command -eq 'adapter' -and $subCmd -eq 'band' -and $depth -eq 4) {
        @('any', '2.4', '5', '6') |
            Where-Object { $_ -like "$wordToComplete*" } |
            ForEach-Object {
                [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
            }
        return
    }

    # サブコマンド・オプション補完
    $suggestions = switch ($command) {
        'list'          { @('--json', '--status', '--adapter') }
        'scan'          { @('--adapter', '--json', '--advise', '--recommend', '--evil-twin', '--interference', '--mesh') }
        'connect'       { @('--adapter', '--password', '-p', '--auth', '--timeout', '--hidden', '--eap-type', '--username', '--domain', '--server-name', '--trusted-root-ca') }
        'passpoint'     { @('--adapter', '--json', '--carriers') }
        'privacy'       { @('--mac-mode', '--adapter', '--ssid', '--json') }
        'import-cat'    { @('--username', '--password', '-p', '--adapter', '--timeout', '--dry-run', '--json') }
        'disconnect'    { @('--adapter') }
        'eap-stats'     { @('--json', '--clear') }
        'vpn-advice'    { @('--adapter', '--json') }
        'profile'       {
            if ($depth -eq 2) { @('list', 'delete') }
            else              { @('--adapter') }
        }
        'qr'            { @('--password', '-p', '--auth', '--hidden') }
        'qr-parse'      { @() }
        'export'        { @('--adapter', '--format', '--output', 'csv', 'json', 'txt') }
        'quality'       { @('--host', '--samples', '--json', '--bufferbloat', '--load-url') }
        'history'       { @('--limit', '--json', '--clear') }
        'plan-channels' { @('--adapter', '--band', '--dfs', '--ranked', '--json') }
        'multi'         {
            if ($depth -eq 2) { @('connect', 'disconnect-all', 'status') }
            else              { @('--password') }
        }
        'adapter'       {
            if ($depth -eq 2) { @('list', 'rename', 'band', 'pin', 'unpin', 'enable', 'disable') }
            else              { @() }
        }
        default         { @('--help', '--json') }
    }

    $suggestions |
        Where-Object { $_ -like "$wordToComplete*" } |
        ForEach-Object {
            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
        }
}
