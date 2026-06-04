using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// Apple HIG "Helpful Error Messages":
///   「接続に失敗しました」で終わらない。
///   「なぜ失敗したか」と「どうすれば解決できるか」を人間語で伝える。
/// </summary>
public static class TroubleshootingHelper
{
    /// <summary>接続失敗の原因に応じた人間語のアドバイス一覧を返す。</summary>
    public static TroubleshootingAdvice GetAdvice(ConnectionFailure failure, AuthMethod auth)
    {
        return failure switch
        {
            ConnectionFailure.BadCredentials => new TroubleshootingAdvice(
                Title:   "パスワードが違います",
                Reason:  "入力されたパスワードがアクセスポイントと一致しませんでした。",
                Steps:
                [
                    "パスワードをもう一度確認してください(大文字・小文字に注意)",
                    "ルーターの裏面やマニュアルに記載されたパスワードを使ってみてください",
                    "パスワードを変更した場合は新しいパスワードを入力してください"
                ],
                Icon: "🔑"),

            ConnectionFailure.Timeout => new TroubleshootingAdvice(
                Title:   "接続がタイムアウトしました",
                Reason:  "アクセスポイントからの応答がありませんでした。",
                Steps:
                [
                    "アクセスポイント(ルーター)の電源が入っているか確認してください",
                    "電波が届く範囲に移動してください",
                    "ルーターを再起動してみてください(電源を10秒オフ→オン)",
                    "同じ場所に他のデバイスが接続できるか確認してください"
                ],
                Icon: "⏱"),

            ConnectionFailure.NotInRange => new TroubleshootingAdvice(
                Title:   "ネットワークが見つかりません",
                Reason:  "選択したネットワークが現在の場所では受信できません。",
                Steps:
                [
                    "アクセスポイントに近づいてから再試行してください",
                    "アクセスポイントの電源が入っているか確認してください",
                    "「再スキャン」ボタンでネットワークを再検索してください"
                ],
                Icon: "📡"),

            ConnectionFailure.AdapterDisabled => new TroubleshootingAdvice(
                Title:   "無線アダプターが無効です",
                Reason:  "お使いのPCの無線LANアダプターがオフになっています。",
                Steps:
                [
                    "キーボードの機内モードキー(飛行機マーク)を押してオフにしてください",
                    "Windows の設定 → ネットワーク → Wi-Fi をオンにしてください",
                    "デバイスマネージャーで無線LANアダプターが有効か確認してください"
                ],
                Icon: "📵"),

            ConnectionFailure.InsufficientPrivilege => new TroubleshootingAdvice(
                Title:   "管理者権限が必要です",
                Reason:  "ネットワークプロファイルの追加には管理者権限が必要です。",
                Steps:
                [
                    "MWC を右クリック → 「管理者として実行」で起動してください",
                    "または管理者アカウントでサインインして再試行してください"
                ],
                Icon: "🔒"),

            ConnectionFailure.BadCredentials when auth == AuthMethod.WPA2Enterprise
                or auth == AuthMethod.WPA3Enterprise => new TroubleshootingAdvice(
                Title:   "企業認証に失敗しました",
                Reason:  "ユーザー名またはパスワードが正しくありません。",
                Steps:
                [
                    "ネットワーク管理者に正しい認証情報を確認してください",
                    "ドメイン名が必要な場合は「ドメイン\\ユーザー名」の形式で入力してください",
                    "証明書の有効期限が切れていないか管理者に確認してください"
                ],
                Icon: "🏢"),

            _ => new TroubleshootingAdvice(
                Title:   "接続できませんでした",
                Reason:  "予期しない問題が発生しました。",
                Steps:
                [
                    "しばらく待ってから再試行してください",
                    "MWC のログ(%LocalAppData%\\MWC\\logs\\)をご確認ください",
                    "問題が続く場合は GitHub Issues にご報告ください"
                ],
                Icon: "❓")
        };
    }
}

public sealed record TroubleshootingAdvice(
    string        Title,
    string        Reason,
    string[]      Steps,
    string        Icon
);
