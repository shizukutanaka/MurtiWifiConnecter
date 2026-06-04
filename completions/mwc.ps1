# MWC CLI PowerShell completion
# インストール: . .\completions\mwc.ps1
#   または $PROFILE に追記

Register-ArgumentCompleter -Native -CommandName mwc -ScriptBlock {
    param($wordToComplete, $commandAst, $cursorPosition)

    $commands = @(
        'adapters', 'scan', 'connect', 'disconnect',
        'profile', 'qr', 'qr-parse', 'export',
        'quality', 'history', 'help'
    )

    $commandElements = $commandAst.CommandElements
    $command = if ($commandElements.Count -gt 1) { $commandElements[1].Value } else { '' }

    # トップレベルコマンド補完
    if ($commandElements.Count -le 1 -or
        ($commandElements.Count -eq 2 -and $wordToComplete -ne '')) {
        $commands + @('--help', '--version') |
            Where-Object { $_ -like "$wordToComplete*" } |
            ForEach-Object {
                [System.Management.Automation.CompletionResult]::new(
                    $_, $_, 'ParameterValue', $_)
            }
        return
    }

    # サブコマンド・オプション補完
    $suggestions = switch ($command) {
        'profile'  { @('list', 'delete') }
        'export'   { @('--format', '--output', 'csv', 'json', 'txt') }
        'connect'  { @('--adapter', '--password', '--auth', '--timeout') }
        'scan'     { @('--adapter', '--json') }
        'adapters' { @('--adapter', '--json') }
        'history'  { @('--days', '--json') }
        'quality'  { @('--days', '--json') }
        default    { @('--help', '--json') }
    }

    $suggestions |
        Where-Object { $_ -like "$wordToComplete*" } |
        ForEach-Object {
            [System.Management.Automation.CompletionResult]::new(
                $_, $_, 'ParameterValue', $_)
        }
}
