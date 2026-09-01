// MWC.App.Services.NotificationService の **型検査専用スタブ**。製品には含めない。
//
// 本物は Windows のトースト通知に依存するため Linux では読み込めないが、
// AutoReconnectService / AdapterFailoverService / ErrorHandlerService という
// **WPF に依存しない App 層サービス**が型として参照するだけなので、
// 名前と署名が解決できれば十分。中身は一切実行しない。
//
// 署名は本物 (src/MWC.App/Services/NotificationService.cs) と一致させること。
// ずれると偽のエラー/偽の安心を生む。
using MWC.Core.Models;

namespace MWC.App.Services;

public sealed class NotificationService
{
    public void NotifyConnected(string ssid, bool hasInternet, bool captive) { }
    public void NotifyFailover(string title, bool hasInternet, bool captive) { }
    public void NotifyDisconnected(string ssid) { }
    public void NotifyFailed(string ssid, ConnectionFailure failure) { }
}
