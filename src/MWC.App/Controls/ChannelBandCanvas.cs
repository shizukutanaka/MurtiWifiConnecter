using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using MWC.Core.Models;

namespace MWC.App.Controls;

/// <summary>
/// チャンネル帯域可視化。2.4GHz / 5GHz / 6GHz 切替。
/// inSSIDer / Acrylic WiFi の「チャンネルグラフ」相当。
/// DataContext から IReadOnlyList(WifiNetwork) を受け取る。
/// </summary>
public sealed class ChannelBandCanvas : FrameworkElement
{
    public static readonly DependencyProperty NetworksProperty =
        DependencyProperty.Register(nameof(Networks),
            typeof(IEnumerable<WifiNetwork>), typeof(ChannelBandCanvas),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                (d, _) => ((ChannelBandCanvas)d).Rebuild()));

    public static readonly DependencyProperty BandFilterProperty =
        DependencyProperty.Register(nameof(BandFilter),
            typeof(WifiBand), typeof(ChannelBandCanvas),
            new FrameworkPropertyMetadata(WifiBand.Band2_4GHz,
                FrameworkPropertyMetadataOptions.AffectsRender,
                (d, _) => ((ChannelBandCanvas)d).Rebuild()));

    public IEnumerable<WifiNetwork>? Networks
    {
        get => (IEnumerable<WifiNetwork>?)GetValue(NetworksProperty);
        set => SetValue(NetworksProperty, value);
    }
    public WifiBand BandFilter
    {
        get => (WifiBand)GetValue(BandFilterProperty);
        set => SetValue(BandFilterProperty, value);
    }

    private readonly VisualCollection _visuals;

    private static readonly Brush[] Palette = {
        MkBrush(0, 196, 204, 180),   MkBrush(34, 197, 94, 180),
        MkBrush(251, 191, 36, 180),  MkBrush(239, 68, 68, 180),
        MkBrush(167, 139, 250, 180), MkBrush(251, 113, 133, 180),
        MkBrush(56, 189, 248, 180),  MkBrush(163, 230, 53, 180),
    };

    private static SolidColorBrush MkBrush(byte r, byte g, byte b, byte a)
    {
        var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        br.Freeze(); return br;
    }

    public ChannelBandCanvas() { _visuals = new VisualCollection(this); }

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];
    protected override void OnRenderSizeChanged(SizeChangedInfo info) => Rebuild();

    private void Rebuild()
    {
        _visuals.Clear();
        var dv = new DrawingVisual();
        _visuals.Add(dv);

        double w = ActualWidth, h = ActualHeight;
        if (w < 10 || h < 10) return;

        var nets = Networks?.Where(n => n.Band == BandFilter).ToList()
                   ?? new List<WifiNetwork>();

        using var dc = dv.RenderOpen();
        var (chMin, chMax, step) = BandFilter switch
        {
            WifiBand.Band5GHz => (36, 177, 4),
            WifiBand.Band6GHz => (1,  233, 4),
            _                 => (1,   14, 1),   // 2.4GHz
        };

        DrawGrid(dc, w, h, chMin, chMax, step);
        for (int i = 0; i < nets.Count; i++)
            DrawBell(dc, w, h, nets[i], i, chMin, chMax);

        DrawLegend(dc, h, nets);
    }

    private static void DrawGrid(DrawingContext dc,
        double w, double h, int chMin, int chMax, int step)
    {
        var gp  = new Pen(new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)), 0.5); gp.Freeze();
        var ap  = new Pen(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 1);  ap.Freeze();
        var tf  = new Typeface("Segoe UI");
        var lbr = new SolidColorBrush(Color.FromArgb(100, 180, 180, 180)); lbr.Freeze();

        double baseY = h - 18;
        for (int ch = chMin; ch <= chMax; ch += step)
        {
            double x = ChX(ch, w, chMin, chMax);
            dc.DrawLine(gp, new Point(x, 0), new Point(x, baseY));
            if ((ch - chMin) % (step * 3) == 0)
            {
                var ft = Fmt(ch.ToString(), 8, lbr, tf);
                dc.DrawText(ft, new Point(x - ft.Width / 2, baseY + 2));
            }
        }
        dc.DrawLine(ap, new Point(0, baseY), new Point(w, baseY));
    }

    private static void DrawBell(DrawingContext dc,
        double w, double h, WifiNetwork n, int idx, int chMin, int chMax)
    {
        if (n.Channel <= 0) return;
        var brush = Palette[idx % Palette.Length];
        var fillColor = ((SolidColorBrush)brush).Color;
        var fill = new SolidColorBrush(Color.FromArgb(55, fillColor.R, fillColor.G, fillColor.B));
        fill.Freeze();

        double baseY = h - 18;
        double peak  = n.SignalQuality / 100.0 * (baseY - 4);
        double xc    = ChX(n.Channel, w, chMin, chMax);
        double wide  = n.ChannelWidth switch { 40 => 4, 80 => 8, 160 => 16, 320 => 32, _ => 2 };
        double sigma = Math.Max(ChX(n.Channel + wide / 2, w, chMin, chMax) - xc, 12);

        // ガウス曲線
        const int pts = 50;
        var path = new StreamGeometry();
        using (var ctx = path.Open())
        {
            ctx.BeginFigure(new Point(xc - sigma * 2.8, baseY), true, true);
            for (int s = 0; s <= pts; s++)
            {
                double dx = (s / (double)pts - 0.5) * sigma * 5.6;
                double y  = peak * Math.Exp(-0.5 * (dx / sigma) * (dx / sigma));
                ctx.LineTo(new Point(xc + dx, baseY - y), s > 0, false);
            }
            ctx.LineTo(new Point(xc + sigma * 2.8, baseY), false, false);
        }
        path.Freeze();
        dc.DrawGeometry(fill, new Pen(brush, 1.5), path);

        // SSID ラベル
        var tf = new Typeface("Segoe UI");
        var lbl = n.Ssid.Length > 11 ? n.Ssid[..11] + "…" : n.Ssid;
        var ft  = Fmt(lbl, 9, brush, tf);
        dc.DrawText(ft, new Point(
            Math.Clamp(xc - ft.Width / 2, 2, w - ft.Width - 2),
            baseY - peak - ft.Height - 3));
    }

    private static void DrawLegend(DrawingContext dc, double h, List<WifiNetwork> nets)
    {
        var tf = new Typeface("Segoe UI");
        var fg = new SolidColorBrush(Color.FromArgb(200, 220, 222, 225)); fg.Freeze();
        for (int i = 0; i < Math.Min(nets.Count, 8); i++)
        {
            var br = Palette[i % Palette.Length];
            dc.DrawRectangle(br, null, new Rect(6, 4 + i * 13, 7, 7));
            var ft = Fmt(nets[i].Ssid.Length > 16 ? nets[i].Ssid[..16] + "…" : nets[i].Ssid, 9, fg, tf);
            dc.DrawText(ft, new Point(16, 3 + i * 13));
        }
    }

    private static double ChX(int ch, double w, int min, int max)
    {
        const double m = 18;
        return max == min ? w / 2 : m + (ch - min) / (double)(max - min) * (w - m * 2);
    }

    private static FormattedText Fmt(string s, double size, Brush brush, Typeface tf) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, brush, 96);
}
