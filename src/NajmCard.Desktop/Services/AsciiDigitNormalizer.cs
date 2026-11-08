using System.Globalization;
using System.Windows.Data;

namespace NajmCard.Desktop.Services;

/// <summary>Only decimal digits change. Arabic text, identifiers and
/// non-decimal symbols keep their original values.</summary>
public static class AsciiDigitNormalizer
{
    public static string NormalizeAsciiDigits(string? value)
    {
        if(string.IsNullOrEmpty(value))return value??string.Empty;
        char[]? buffer=null;
        for(int i=0;i<value.Length;i++)
        {
            char c=value[i];
            if(c>='0'&&c<='9')continue;
            if(char.GetUnicodeCategory(c)!=UnicodeCategory.DecimalDigitNumber)continue;
            double n=char.GetNumericValue(c);
            if(n<0||n>9||n!=Math.Truncate(n))continue;
            buffer??=value.ToCharArray();
            buffer[i]=(char)('0'+(int)n);
        }
        return buffer is null?value:new string(buffer);
    }
}

/// <summary>UI-only WPF display conversion, never mutates account identifiers,
/// credentials, router inventory or database values.</summary>
public sealed class AsciiDigitDisplayConverter : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
        => value is string text ? AsciiDigitNormalizer.NormalizeAsciiDigits(text) : value;

    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)
        => Binding.DoNothing;
}
