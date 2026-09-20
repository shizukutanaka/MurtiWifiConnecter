# MWC CLI bash completion
# インストール: source completions/mwc.bash
#   または /etc/bash_completion.d/ に配置

_mwc_completions()
{
    local cur prev words cword
    _init_completion || return

    local commands="list scan connect disconnect profile qr qr-parse export quality history eap-stats plan-channels vpn-advice multi adapter import-cat passpoint privacy help"

    # トップレベルコマンド
    if [[ $cword -eq 1 ]]; then
        COMPREPLY=( $(compgen -W "$commands --help --version" -- "$cur") )
        return
    fi

    case "${words[1]}" in
        list)
            COMPREPLY=( $(compgen -W "--json --status" -- "$cur") )
            return
            ;;
        scan)
            COMPREPLY=( $(compgen -W "--adapter --json --advise --recommend --evil-twin --interference --mesh" -- "$cur") )
            return
            ;;
        connect)
            case "$prev" in
                --auth)
                    COMPREPLY=( $(compgen -W "Open OWE WEP WPAPSK WPA2PSK WPA3SAE WPA3Transition WPA2Enterprise WPA3Enterprise WPA3Enterprise192" -- "$cur") )
                    return
                    ;;
                --eap-type)
                    COMPREPLY=( $(compgen -W "PEAP_MSCHAPv2 EAP_TLS EAP_TTLS" -- "$cur") )
                    return
                    ;;
            esac
            COMPREPLY=( $(compgen -W "--adapter --password -p --auth --timeout --hidden --eap-type --username --domain --server-name --trusted-root-ca" -- "$cur") )
            return
            ;;
        privacy)
            case "$prev" in
                --mac-mode)
                    COMPREPLY=( $(compgen -W "hardware random-per-network random-daily" -- "$cur") )
                    return
                    ;;
            esac
            COMPREPLY=( $(compgen -W "--mac --mac-mode --adapter --ssid --json" -- "$cur") )
            return
            ;;
        passpoint)
            COMPREPLY=( $(compgen -W "--adapter --json --carriers" -- "$cur") )
            return
            ;;
        import-cat)
            COMPREPLY=( $(compgen -W "--username --password -p --adapter --timeout --dry-run --json" -- "$cur") )
            return
            ;;
        disconnect)
            COMPREPLY=( $(compgen -W "--adapter" -- "$cur") )
            return
            ;;
        eap-stats)
            COMPREPLY=( $(compgen -W "--json --clear" -- "$cur") )
            return
            ;;
        vpn-advice)
            COMPREPLY=( $(compgen -W "--adapter --json" -- "$cur") )
            return
            ;;
        profile)
            if [[ $cword -eq 2 ]]; then
                COMPREPLY=( $(compgen -W "list delete" -- "$cur") )
                return
            fi
            COMPREPLY=( $(compgen -W "--adapter" -- "$cur") )
            return
            ;;
        qr)
            COMPREPLY=( $(compgen -W "--password -p --auth --hidden" -- "$cur") )
            return
            ;;
        qr-parse)
            return
            ;;
        export)
            case "$prev" in
                --format|-f)
                    COMPREPLY=( $(compgen -W "csv json txt" -- "$cur") )
                    return
                    ;;
            esac
            COMPREPLY=( $(compgen -W "--adapter --format --output" -- "$cur") )
            return
            ;;
        quality)
            COMPREPLY=( $(compgen -W "--host --samples --json --bufferbloat --load-url" -- "$cur") )
            return
            ;;
        history)
            COMPREPLY=( $(compgen -W "--limit --json --clear" -- "$cur") )
            return
            ;;
        plan-channels)
            case "$prev" in
                --band)
                    COMPREPLY=( $(compgen -W "2.4 5 6" -- "$cur") )
                    return
                    ;;
            esac
            COMPREPLY=( $(compgen -W "--adapter --band --dfs --ranked --json" -- "$cur") )
            return
            ;;
        multi)
            if [[ $cword -eq 2 ]]; then
                COMPREPLY=( $(compgen -W "connect disconnect-all status" -- "$cur") )
                return
            fi
            if [[ "${words[2]}" == "connect" ]]; then
                COMPREPLY=( $(compgen -W "--password" -- "$cur") )
            fi
            return
            ;;
        adapter)
            if [[ $cword -eq 2 ]]; then
                COMPREPLY=( $(compgen -W "list rename band pin unpin enable disable" -- "$cur") )
                return
            fi
            if [[ "${words[2]}" == "band" && $cword -eq 4 ]]; then
                COMPREPLY=( $(compgen -W "any 2.4 5 6" -- "$cur") )
                return
            fi
            return
            ;;
    esac

    # 共通フォールバック
    COMPREPLY=( $(compgen -W "--help --json" -- "$cur") )
}

complete -F _mwc_completions mwc
