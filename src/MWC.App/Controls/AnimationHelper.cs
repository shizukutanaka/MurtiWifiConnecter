using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace MWC.App.Controls;

/// <summary>
/// Apple HIG "Motion":
///   アニメーションは意味を持つ。装飾ではなく情報を伝える。
///   - 要素の追加 → フェードイン(新しいものが現れた)
///   - 接続成功  → スケールアップ+フェード(達成感)
///   - エラー    → 左右シェイク(注意を引く)
///   - パネル切替 → スライド(奥行きの演出)
///
/// WPF Storyboard で実装。GPU合成で60fps。
/// </summary>
public static class AnimationHelper
{
    // ── フェードイン ──────────────────────────────
    public static void FadeIn(UIElement el, double durationMs = 200)
    {
        el.Opacity = 0;
        el.Visibility = Visibility.Visible;
        var anim = new DoubleAnimation(0, 1,
            TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        el.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    public static void FadeOut(UIElement el, double durationMs = 150, Action? onComplete = null)
    {
        var anim = new DoubleAnimation(1, 0,
            TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        if (onComplete is not null)
            anim.Completed += (_, _) => { el.Visibility = Visibility.Collapsed; onComplete(); };
        el.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    // ── 接続成功パルス ───────────────────────────
    public static async Task PulseSuccessAsync(UIElement el)
    {
        var tg = new System.Windows.Media.ScaleTransform(1, 1);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = tg;

        var scaleUp = new DoubleAnimation(1.0, 1.06,
            TimeSpan.FromMilliseconds(120))
        { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } };

        var scaleDown = new DoubleAnimation(1.06, 1.0,
            TimeSpan.FromMilliseconds(180))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

        var tcs = new TaskCompletionSource<bool>();
        scaleDown.Completed += (_, _) => tcs.SetResult(true);

        tg.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleUp);
        tg.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleUp);
        await Task.Delay(120);
        tg.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleDown);
        tg.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleDown);
        await tcs.Task;
    }

    // ── エラーシェイク ────────────────────────────
    public static async Task ShakeAsync(UIElement el)
    {
        var tt = new System.Windows.Media.TranslateTransform();
        el.RenderTransform = tt;

        double[] offsets = { 0, -8, 8, -6, 6, -4, 4, 0 };
        foreach (var offset in offsets)
        {
            var anim = new DoubleAnimation(offset,
                TimeSpan.FromMilliseconds(40));
            tt.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
            await Task.Delay(40);
        }
        el.RenderTransform = null;
    }

    // ── スライドイン(右パネルタブ切替) ──────────────
    public static void SlideIn(UIElement el, SlideDirection dir = SlideDirection.FromBottom,
        double durationMs = 250)
    {
        var tt = new System.Windows.Media.TranslateTransform(
            dir == SlideDirection.FromRight ? 30 : 0,
            dir == SlideDirection.FromBottom ? 16 : 0);

        el.RenderTransform = tt;
        el.Opacity = 0;
        el.Visibility = Visibility.Visible;

        var opAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        el.BeginAnimation(UIElement.OpacityProperty, opAnim);

        var prop = dir == SlideDirection.FromRight
            ? System.Windows.Media.TranslateTransform.XProperty
            : System.Windows.Media.TranslateTransform.YProperty;
        var tAnim = new DoubleAnimation(
            dir == SlideDirection.FromRight ? 30 : 16, 0,
            TimeSpan.FromMilliseconds(durationMs))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        tt.BeginAnimation(prop, tAnim);
    }

    // ── スケルトンローディング(Shimmer効果) ──────────
    public static void StartShimmer(System.Windows.Shapes.Rectangle rect)
    {
        var from = new System.Windows.Media.LinearGradientBrush();
        from.StartPoint = new Point(0, 0);
        from.EndPoint   = new Point(1, 0);
        from.GradientStops.Add(new System.Windows.Media.GradientStop(
            System.Windows.Media.Color.FromArgb(255, 30, 30, 40), 0));
        from.GradientStops.Add(new System.Windows.Media.GradientStop(
            System.Windows.Media.Color.FromArgb(255, 50, 54, 68), 0.5));
        from.GradientStops.Add(new System.Windows.Media.GradientStop(
            System.Windows.Media.Color.FromArgb(255, 30, 30, 40), 1));

        rect.Fill = from;

        var anim = new DoubleAnimation(0, 1,
            TimeSpan.FromMilliseconds(1200))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            AutoReverse    = true
        };
        from.GradientStops[1].BeginAnimation(
            System.Windows.Media.GradientStop.OffsetProperty, anim);
    }
}

public enum SlideDirection { FromRight, FromBottom }
