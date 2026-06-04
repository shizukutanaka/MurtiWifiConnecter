using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using MWC.Core.Services;

namespace MWC.App.Controls;

/// <summary>
/// SSID 単位の信号品質時系列グラフ。
/// NetSpot / inSSIDer が持つ「リアルタイム信号グラフ」に相当。
///
/// X軸: 時刻(右が最新)  Y軸: Signal Quality 0-100%
/// 描画: DrawingVisual で軽量レンダリング
/// </summary>
public sealed class SignalHistoryCanvas : FrameworkElement
{
    public static readonly DependencyProperty HistoryProperty =
        DependencyProperty.Register(nameof(History), typeof(IReadOnlyList<SignalSample>),
            typeof(SignalHistoryCanvas),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                (d, _) => ((SignalHistoryCanvas)d).Rebuild()));

    public static readonly DependencyProperty SsidProperty =
        DependencyProperty.Register(nameof(Ssid), typeof(string),
            typeof(SignalHistoryCanvas),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                (d, _) => ((SignalHistoryCanvas)d).Rebuild()));

    public IReadOnlyList<SignalSample>? History
    {
        get => (IReadOnlyList<SignalSample>?)GetValue(HistoryProperty);
        set => SetValue(HistoryProperty, value);
    }

    public string? Ssid
    {
        get => (string?)GetValue(SsidProperty);
        set => SetValue(SsidProperty, value);
    }

    private readonly VisualCollection _visuals;

    private static readonly Pen GridPen   = new(new SolidColorBrush(Color.FromArgb(50,  255,255,255)), 0.5);
    private static readonly Pen AxisPen   = new(new SolidColorBrush(Color.FromArgb(100, 255,255,255)), 1);
    private static readonly Pen LinePen   = new(new SolidColorBrush(Color.FromArgb(255, 0, 196, 204)), 1.5);
    private static readonly Brush FillBrush = new LinearGradientBrush(
        Color.FromArgb(80,  0, 196, 204),
        Color.FromArgb(5,   0, 196, 204),
        90);
    private static readonly Typeface Typeface = new("Segoe UI");

    static SignalHistoryCanvas()
    {
        GridPen.Freeze(); AxisPen.Freeze(); LinePen.Freeze(); FillBrush.Freeze();
    }

    public SignalHistoryCanvas() { _visuals = new VisualCollection(this); }

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];
    protected override void OnRenderSizeChanged(SizeChangedInfo info) => Rebuild();

    private void Rebuild()
    {
        _visuals.Clear();
        var dv = new DrawingVisual();
        _visuals.Add(dv);

        double w = ActualWidth, h = ActualHeight;
        if (w < 20 || h < 20) return;

        const double padL = 36, padR = 8, padT = 8, padB = 20;
        double pw = w - padL - padR, ph = h - padT - padB;

        var samples = History;
        using var dc = dv.RenderOpen();

        DrawGrid(dc, padL, padT, pw, ph);

        if (samples is null || samples.Count < 2)
        {
            DrawNoData(dc, w, h);
            dc.Close();
            return;
        }

        DrawLine(dc, samples, padL, padT, pw, ph);
        DrawAxes(dc, padL, padT, pw, ph);
        DrawLabels(dc, samples, padL, padT, pw, ph);
    }

    private static void DrawGrid(DrawingContext dc, double l, double t, double pw, double ph)
    {
        // Y軸グリッド 0/25/50/75/100%
        foreach (int pct in new[] { 0, 25, 50, 75, 100 })
        {
            double y = t + ph * (1 - pct / 100.0);
            dc.DrawLine(GridPen, new Point(l, y), new Point(l + pw, y));
            var ft = new FormattedText($"{pct}",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Typeface, 8, new SolidColorBrush(Color.FromArgb(100, 200, 200, 200)), 96);
            dc.DrawText(ft, new Point(l - ft.Width - 3, y - ft.Height / 2));
        }
    }

    private static void DrawLine(DrawingContext dc,
        IReadOnlyList<SignalSample> samples,
        double l, double t, double pw, double ph)
    {
        // 最新N点のみ描画(古い順に並べて右に最新)
        var ordered = samples.Reverse().Take(120).Reverse().ToList();
        int n = ordered.Count;
        if (n < 2) return;

        double dx = pw / (n - 1);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var pts = new Point[n];
            for (int i = 0; i < n; i++)
            {
                double x = l + i * dx;
                double y = t + ph * (1 - ordered[i].Quality / 100.0);
                pts[i] = new Point(x, y);
            }

            // 塗りつぶし領域(底辺閉じ)
            ctx.BeginFigure(new Point(l, t + ph), true, true);
            ctx.LineTo(pts[0], false, false);
            ctx.PolyLineTo(pts, true, false);
            ctx.LineTo(new Point(l + pw, t + ph), false, false);
        }
        geo.Freeze();
        dc.DrawGeometry(FillBrush, null, geo);

        // ライン
        var linePath = new StreamGeometry();
        using (var ctx = linePath.Open())
        {
            ctx.BeginFigure(new Point(l, t + ph * (1 - ordered[0].Quality / 100.0)), false, false);
            for (int i = 1; i < n; i++)
            {
                double x = l + i * dx;
                double y = t + ph * (1 - ordered[i].Quality / 100.0);
                ctx.LineTo(new Point(x, y), true, false);
            }
        }
        linePath.Freeze();
        dc.DrawGeometry(null, LinePen, linePath);
    }

    private static void DrawAxes(DrawingContext dc, double l, double t, double pw, double ph)
    {
        dc.DrawLine(AxisPen, new Point(l, t), new Point(l, t + ph));
        dc.DrawLine(AxisPen, new Point(l, t + ph), new Point(l + pw, t + ph));
    }

    private static void DrawLabels(DrawingContext dc,
        IReadOnlyList<SignalSample> samples,
        double l, double t, double pw, double ph)
    {
        if (samples.Count == 0) return;
        var latest   = samples[0];  // 降順なので [0] が最新
        var earliest = samples[^1];

        var brush = new SolidColorBrush(Color.FromArgb(140, 180, 180, 180));
        brush.Freeze();

        void DrawTime(DateTimeOffset at, double x)
        {
            var ft = new FormattedText(at.LocalDateTime.ToString("HH:mm",
                CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Typeface, 8, brush, 96);
            dc.DrawText(ft, new Point(Math.Clamp(x - ft.Width / 2, l, l + pw - ft.Width),
                t + ph + 3));
        }
        DrawTime(earliest.At, l);
        DrawTime(latest.At,   l + pw);

        // 現在値ラベル
        var valBrush = new SolidColorBrush(Color.FromArgb(220, 0, 196, 204));
        valBrush.Freeze();
        var valFt = new FormattedText($"{latest.Quality}%",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface, 9, valBrush, 96);
        double yVal = t + ph * (1 - latest.Quality / 100.0);
        dc.DrawText(valFt, new Point(l + pw - valFt.Width - 2, yVal - valFt.Height - 2));
    }

    private static void DrawNoData(DrawingContext dc, double w, double h)
    {
        var ft = new FormattedText(MWC.App.Resources.L.LabelNoData,
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface, 11,
            new SolidColorBrush(Color.FromArgb(80, 180, 180, 180)), 96);
        dc.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
    }
}
