// MWC.App.App.Version の **型検査/実行専用スタブ**。製品には含めない。
//
// 本物は WPF の Application 派生クラス (App.xaml.cs) にある静的プロパティで、
// AppUpdateService が User-Agent とバージョン比較にのみ使う。WPF 参照パックが
// 無い環境ではその 1 プロパティのためだけに App クラス全体が解決できなくなるので、
// ここで最小限を供給する。
namespace MWC.App;

public static class App
{
    public static string Version => "0.0.0";
}
