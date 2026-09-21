using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace SteamLoginLite;

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => value is Visibility.Visible;
}

public sealed class StatusForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value?.ToString() ?? "";
        if (status == "正常") return new SolidColorBrush(ColorHelper.FromArgb(255, 15, 157, 105));
        if (status.Contains("封禁")) return new SolidColorBrush(ColorHelper.FromArgb(255, 220, 53, 69));
        return new SolidColorBrush(ColorHelper.FromArgb(255, 102, 116, 136));
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class StatusBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value?.ToString() ?? "";
        if (status == "正常") return new SolidColorBrush(ColorHelper.FromArgb(255, 237, 250, 244));
        if (status.Contains("封禁")) return new SolidColorBrush(ColorHelper.FromArgb(255, 255, 241, 243));
        return new SolidColorBrush(ColorHelper.FromArgb(255, 242, 245, 249));
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class CurrentRowBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        new SolidColorBrush(value is true ? ColorHelper.FromArgb(255, 248, 252, 243) : Colors.White);
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
