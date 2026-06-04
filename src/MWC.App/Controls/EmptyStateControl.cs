using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MWC.App.Controls;

/// <summary>
/// Apple HIG "Deference" + "Helpful Empty States":
///   空のリスト、エラー状態を親切に説明し、次のアクションを提示する。
///   「ネットワークが見つかりません」で終わらない。
/// </summary>
public sealed class EmptyStateControl : Control
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(EmptyStateControl),
            new PropertyMetadata("📡"));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyStateControl),
            new PropertyMetadata(MWC.App.Resources.L.LabelNetworksNotFound));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(EmptyStateControl),
            new PropertyMetadata(MWC.App.Resources.L.LabelRetryHint));

    public static readonly DependencyProperty ActionLabelProperty =
        DependencyProperty.Register(nameof(ActionLabel), typeof(string), typeof(EmptyStateControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ActionCommandProperty =
        DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(EmptyStateControl),
            new PropertyMetadata(null));

    public string Icon     { get => (string)GetValue(IconProperty);     set => SetValue(IconProperty,     value); }
    public string Title    { get => (string)GetValue(TitleProperty);    set => SetValue(TitleProperty,    value); }
    public string Message  { get => (string)GetValue(MessageProperty);  set => SetValue(MessageProperty,  value); }
    public string? ActionLabel  { get => (string?)GetValue(ActionLabelProperty); set => SetValue(ActionLabelProperty, value); }
    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }

    static EmptyStateControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(EmptyStateControl),
            new FrameworkPropertyMetadata(typeof(EmptyStateControl)));
    }
}
