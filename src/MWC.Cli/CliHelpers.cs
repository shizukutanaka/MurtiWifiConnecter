using System.Text.Json;
using System.Text.Json.Serialization;
using MWC.Core.Models;

namespace MWC.Cli;

internal static class CliHelpers
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    public static void Print(object obj)
        => Console.WriteLine(JsonSerializer.Serialize(obj, JsonOpts));

    public static void Err(string msg) => Console.Error.WriteLine($"error: {msg}");
    public static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n-1)] + "…";
    public static string BandLabel(WifiBand b) => b switch
    {
        WifiBand.Band2_4GHz => "2.4G",
        WifiBand.Band5GHz   => "5G",
        WifiBand.Band6GHz   => "6G",
        _                   => "?"
    };
}
