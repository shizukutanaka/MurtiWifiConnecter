# MWC CLI bash completion
# インストール: source completions/mwc.bash
#   または /etc/bash_completion.d/ に配置

_mwc_completions()
{
    local cur prev words cword
    _init_completion || return

    local commands="adapters scan connect disconnect profile qr qr-parse export quality history help"
    local profile_subcommands="list delete"

    # トップレベルコマンド
    if [[ $cword -eq 1 ]]; then
        COMPREPLY=( $(compgen -W "$commands --help --version" -- "$cur") )
        return
    fi

    # サブコマンド
    case "${words[1]}" in
        profile)
            if [[ $cword -eq 2 ]]; then
                COMPREPLY=( $(compgen -W "$profile_subcommands" -- "$cur") )
                return
            fi
            ;;
        export)
            case "$prev" in
                --format|-f)
                    COMPREPLY=( $(compgen -W "csv json txt" -- "$cur") )
                    return
                    ;;
            esac
            COMPREPLY=( $(compgen -W "--format --output" -- "$cur") )
            return
            ;;
        connect)
            COMPREPLY=( $(compgen -W "--adapter --password --auth --timeout" -- "$cur") )
            return
            ;;
        scan|adapters)
            COMPREPLY=( $(compgen -W "--adapter --json" -- "$cur") )
            return
            ;;
        history|quality)
            COMPREPLY=( $(compgen -W "--days --json" -- "$cur") )
            return
            ;;
    esac

    # 共通オプション
    COMPREPLY=( $(compgen -W "--help --json" -- "$cur") )
}

complete -F _mwc_completions mwc
