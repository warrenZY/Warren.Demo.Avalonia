using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Bookmark.Converters;

public class CountToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count > 0;
        }

        if (value is System.Collections.ICollection collection)
        {
            return collection.Count > 0;
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}