// .resx を .resources へ変換する最小ツール (型検査/テスト実行ハーネス専用)。
// 製品ビルドでは MSBuild が同じことをする。ここでは resgen が無いので自前で行う。
using System;
using System.Linq;
using System.Resources;
using System.Xml.Linq;

public static class ResxToResources
{
    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: <input.resx> <output.resources>");
            return 2;
        }
        var doc = XDocument.Load(args[0]);
        using var w = new ResourceWriter(args[1]);
        int n = 0;
        foreach (var d in doc.Root!.Elements("data"))
        {
            var name = (string?)d.Attribute("name");
            if (name is null) continue;
            // type/mimetype 付き (バイナリ) は扱わない。MWC の Strings.resx は全て文字列。
            if (d.Attribute("type") is not null || d.Attribute("mimetype") is not null) continue;
            w.AddResource(name, d.Element("value")?.Value ?? "");
            n++;
        }
        w.Generate();
        Console.WriteLine($"{n} resources -> {args[1]}");
        return 0;
    }
}
