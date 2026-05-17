using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Spectrvm.Models;

namespace Spectrvm.Converters;

public sealed class IsViewModeConverter(ViewMode target) : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is ViewMode m && m == target;

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();
}

public static class ViewModeConverters
{
    public static IValueConverter Interpreter { get; } = new IsViewModeConverter(ViewMode.Interpreter);
    public static IValueConverter Curl        { get; } = new IsViewModeConverter(ViewMode.Curl);
    public static IValueConverter Links       { get; } = new IsViewModeConverter(ViewMode.Links);
    public static IValueConverter History     { get; } = new IsViewModeConverter(ViewMode.History);
    public static IValueConverter Navigator   { get; } = new IsViewModeConverter(ViewMode.Navigator);
}