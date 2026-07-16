using FluentAssertions;
using MWC.App.Services;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  BrowserLauncher — http/https のみ許可するシェル起動シンクの防御
//
//  正のケース (実 http URL) はブラウザを実起動するため CI では検証しない。
//  ここでは「不正スキーム・相対 URI・null を拒否する」防御契約のみを検証する
//  (拒否は Process.Start に到達する前に false を返すため副作用なし)。
// ══════════════════════════════════════════════════════════════
public class BrowserLauncherTests
{
    [Theory]
    [InlineData("file:///etc/passwd")]          // ローカルファイル
    [InlineData("file://C:/Windows/System32")]  // ローカルパス
    [InlineData("javascript:alert(1)")]         // スクリプトスキーム
    [InlineData("ms-settings:network")]         // カスタム OS スキーム
    [InlineData("ftp://example.com/x")]         // 非 http スキーム
    [InlineData("mailto:a@b.com")]              // mailto
    public void OpenHttp_NonHttpScheme_Refused(string url)
    {
        BrowserLauncher.OpenHttp(url).Should().BeFalse(
            "non-http(s) schemes must never reach the shell-execute sink");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a uri")]
    [InlineData("/relative/path")]
    [InlineData("github.com/foo")]   // スキームなし → 絶対 URI ではない
    public void OpenHttp_InvalidOrRelative_Refused(string? url)
    {
        BrowserLauncher.OpenHttp(url).Should().BeFalse(
            "only absolute http/https URIs are permitted");
    }

    [Fact]
    public void OpenHttp_NullUri_Refused()
    {
        BrowserLauncher.OpenHttp((System.Uri?)null).Should().BeFalse();
    }
}
