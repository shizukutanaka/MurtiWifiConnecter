using System.Text.Json;
using System.Text.Json.Serialization;
using MWC.Core.Models;
using MWC.Core.Services;

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

    // CLI Console output is exempt from the Strings.resx rule, so interference
    // factors/recommendations are rendered to plain English here.
    public static string InterferenceFactorLabel(InterferenceFactor f) => f.Kind switch
    {
        InterferenceFactorKind.CoChannel       => $"{f.Count} AP(s) on ch {f.Channel} — co-channel",
        InterferenceFactorKind.AdjacentChannel => $"{f.Count} adjacent-channel AP(s) (2.4GHz)",
        _                                      => "2.4GHz Bluetooth/Zigbee co-existence",
    };

    public static string InterferenceRecommendationLabel(InterferenceRecommendationKind r) => r switch
    {
        InterferenceRecommendationKind.SwitchBand    => "Switch to 5GHz/6GHz (or use ch 1/6/11)",
        InterferenceRecommendationKind.SwitchChannel => "Switch channel or move to 6GHz",
        _                                            => "No action needed",
    };
}
