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
            // Guarded cases must precede unguarded cases for the same discriminant.
            ConnectionFailure.BadCredentials when auth == AuthMethod.WPA2Enterprise
                or auth == AuthMethod.WPA3Enterprise => new TroubleshootingAdvice(
                Title:   "Enterprise Authentication Failed",
                Reason:  "The username or password is incorrect.",
                Steps:
                [
                    "Verify your credentials with the network administrator",
                    "If a domain is required, enter it as DOMAIN\\username",
                    "Ask your administrator whether the certificate has expired"
                ],
                Icon: "🏢"),

            ConnectionFailure.BadCredentials => new TroubleshootingAdvice(
                Title:   "Wrong Password",
                Reason:  "The password you entered does not match the access point.",
                Steps:
                [
                    "Double-check the password (case-sensitive)",
                    "Try the password printed on the router label or in its manual",
                    "If you recently changed the password, enter the new one"
                ],
                Icon: "🔑"),

            ConnectionFailure.Timeout => new TroubleshootingAdvice(
                Title:   "Connection Timed Out",
                Reason:  "The access point did not respond.",
                Steps:
                [
                    "Make sure the access point (router) is powered on",
                    "Move closer to the access point",
                    "Restart the router (power off for 10 seconds, then on)",
                    "Check whether other devices can connect from the same location"
                ],
                Icon: "⏱"),

            ConnectionFailure.NotInRange => new TroubleshootingAdvice(
                Title:   "Network Not Found",
                Reason:  "The selected network is not reachable from your current location.",
                Steps:
                [
                    "Move closer to the access point and try again",
                    "Check that the access point is powered on",
                    "Use the Rescan button to search for networks again"
                ],
                Icon: "📡"),

            ConnectionFailure.AdapterDisabled => new TroubleshootingAdvice(
                Title:   "Wi-Fi Adapter Disabled",
                Reason:  "The Wi-Fi adapter on your PC is turned off.",
                Steps:
                [
                    "Press the Airplane mode key on your keyboard to turn it off",
                    "Go to Windows Settings → Network → Wi-Fi and turn it on",
                    "Check in Device Manager that the Wi-Fi adapter is enabled"
                ],
                Icon: "📵"),

            ConnectionFailure.InsufficientPrivilege => new TroubleshootingAdvice(
                Title:   "Administrator Privileges Required",
                Reason:  "Adding a network profile requires administrator privileges.",
                Steps:
                [
                    "Right-click MWC and choose Run as administrator",
                    "Or sign in with an administrator account and try again"
                ],
                Icon: "🔒"),

            _ => new TroubleshootingAdvice(
                Title:   "Connection Failed",
                Reason:  "An unexpected error occurred.",
                Steps:
                [
                    "Wait a moment and try again",
                    "Check the MWC log files (%LocalAppData%\\MWC\\logs\\)",
                    "If the problem persists, report it on GitHub Issues"
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
