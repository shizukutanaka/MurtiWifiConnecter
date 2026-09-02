// Serilog の **型検査/実行専用スタブ**。製品には含めない。
//
// BrowserLauncher が使うのは静的 Log.Warning のみ (3 箇所)。この 1 点のために
// BrowserLauncherTests をまるごと検査対象外にするのは割に合わないので、
// 実際に使われている面だけを最小限で供給する。ログ出力は行わない。
using System;

namespace Serilog;

public static class Log
{
    public static void Verbose(string messageTemplate, params object?[] propertyValues) { }
    public static void Debug(string messageTemplate, params object?[] propertyValues) { }
    public static void Information(string messageTemplate, params object?[] propertyValues) { }
    public static void Warning(string messageTemplate, params object?[] propertyValues) { }
    public static void Warning(Exception? exception, string messageTemplate, params object?[] propertyValues) { }
    public static void Error(string messageTemplate, params object?[] propertyValues) { }
    public static void Error(Exception? exception, string messageTemplate, params object?[] propertyValues) { }
    public static void Fatal(string messageTemplate, params object?[] propertyValues) { }
    public static void Fatal(Exception? exception, string messageTemplate, params object?[] propertyValues) { }
}
