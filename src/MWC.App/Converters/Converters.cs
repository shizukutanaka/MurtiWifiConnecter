using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MWC.Core.Models;
using MWC.Core.Services;

namespace MWC.App.Converters;

// ── null → bool (IsEnabled等に使用) ──────────────────
[ValueConversion(typeof(object), typeof(bool))]
public sealed class NullToBoolConverter : IValueConverter
{
    public static readonly NullToBoolConverter Instance = new();
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is not null;
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

// ── bool → Visibility ────────────────────────────────
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance          = new() { Invert = false };
    public static readonly BoolToVisibilityConverter InvertedInstance  = new() { Invert = true };

    public bool Invert { get; set; }

    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        bool b = v is bool bl && bl;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => (v is Visibility vis && vis == Visibility.Visible) != Invert;
}

// ── int → bool (Count > 0) ───────────────────────────
[ValueConversion(typeof(int), typeof(bool))]
public sealed class CountToBoolConverter : IValueConverter
{
    public static readonly CountToBoolConverter Instance = new();
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is int i && i > 0;
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

// ── int → Visibility (Count == 0 → Visible: EmptyState用) ──
[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public static readonly ZeroToVisibleConverter Instance = new();
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is int i && i == 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

// ── null → Visibility ────────────────────────────────
[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <summary>null → Collapsed, not-null → Visible</summary>
    public static readonly NullToVisibilityConverter Instance  = new() { Invert = false };
    /// <summary>null → Visible, not-null → Collapsed</summary>
    public static readonly NullToVisibilityConverter Inverted  = new() { Invert = true };

    public bool Invert { get; set; }

    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        bool hasValue = v is not null
                     && (v is not string s || !string.IsNullOrEmpty(s));
        bool show = Invert ? !hasValue : hasValue;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

// ── bool → DisplayMode RadioButton helper ────────────
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class ExpertModeToBoolConverter : IValueConverter
{
    public static readonly ExpertModeToBoolConverter Instance = new();
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is bool b && b;
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => v is bool b && b;
}

// ── string? → Visibility (空/nullならCollapsed) ──────
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class StringToVisibilityConverter : IValueConverter
{
    public static readonly StringToVisibilityConverter Instance = new();
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is string s && !string.IsNullOrEmpty(s)
            ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}
