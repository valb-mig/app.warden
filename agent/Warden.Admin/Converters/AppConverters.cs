using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Warden.Admin.Converters;

public sealed class IsNotNullOrEmptyConverter : IValueConverter
{
    public static readonly IsNotNullOrEmptyConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrEmpty(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true → vermelho (erro), false → verde (sucesso) — usado no toast de baixo da janela.</summary>
public sealed class ToastColorConverter : IValueConverter
{
    public static readonly ToastColorConverter Instance = new();
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#C4362D"));
    private static readonly IBrush OkBrush = new SolidColorBrush(Color.Parse("#1F8A4C"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? ErrorBrush : OkBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class IsNotNullConverter : IValueConverter
{
    public static readonly IsNotNullConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true quando um `int?` é maior que zero — usado nos badges de ahead/behind do git.</summary>
public sealed class IntPositiveConverter : IValueConverter
{
    public static readonly IntPositiveConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && i > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool → verde/cinza — bolinha de "conectado" do terminal de logs.</summary>
public sealed class ConnectedDotConverter : IValueConverter
{
    public static readonly ConnectedDotConverter Instance = new();
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush Gray = new SolidColorBrush(Color.Parse("#52525B"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Green : Gray;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool (é linha de erro?) → vermelho/cinza-claro — cor da linha no terminal de logs.</summary>
public sealed class ErrorLineBrushConverter : IValueConverter
{
    public static readonly ErrorLineBrushConverter Instance = new();
    private static readonly IBrush ErrorRed = new SolidColorBrush(Color.Parse("#F87171"));
    private static readonly IBrush Normal = new SolidColorBrush(Color.Parse("#D4D4D8"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? ErrorRed : Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
