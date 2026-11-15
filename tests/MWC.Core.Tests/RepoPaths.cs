using System;
using System.IO;

namespace MWC.Core.Tests;

/// <summary>
/// テストが読むリポジトリ内ソースファイル (テーマ .xaml / .resx) の場所解決。
///
/// 以前は [CallerFilePath] + "../.." で辿っていたが、CI では
/// Directory.Build.props が ContinuousIntegrationBuild=true にするため
/// CallerFilePath が実パスではなく決定論的な疑似パス ("/_/tests/...") を
/// 返す。Windows ではそれが "D:\_\src\MWC.App" のような実在しないパスに
/// 化け、テーマ/ロケール系テスト 53 件が DirectoryNotFoundException で
/// 落ちた (ローカルでは GITHUB_ACTIONS 未設定のため実パスが返り再現しない)。
///
/// そのため、実行中アセンブリの配置場所から上方向へ "src/MWC.App" を含む
/// ディレクトリ (リポジトリルート) を探索する。出力レイアウト
/// (artifacts/bin/&lt;project&gt;/&lt;config&gt;/&lt;tfm&gt;/) の深さにも、
/// チェックアウト先の絶対パスにも依存しない。見つからなければ黙って
/// 別パスを指さず例外で失敗させる。
/// </summary>
internal static class RepoPaths
{
    /// <summary>src/MWC.App ディレクトリの絶対パスを返す。</summary>
    public static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "MWC.App");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"src/MWC.App was not found in any ancestor directory of {AppContext.BaseDirectory}");
    }
}
