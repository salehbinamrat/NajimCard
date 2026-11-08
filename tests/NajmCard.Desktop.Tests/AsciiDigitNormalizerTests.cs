using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

public sealed class AsciiDigitNormalizerTests
{
    [Fact]
    public void Grid_Display_Converts_Source_Text_Without_Changing_Bound_Value()
    {
        var converter=new AsciiDigitDisplayConverter();
        Assert.Equal("بطاقة 123",converter.Convert("بطاقة ١٢٣",
            typeof(string),null!,System.Globalization.CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(42,converter.Convert(42,typeof(string),null!,
            System.Globalization.CultureInfo.GetCultureInfo("en-US")));
        Assert.Same(System.Windows.Data.Binding.DoNothing,
            converter.ConvertBack("١٢٣",typeof(string),null!,
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("الرقم: ١٢٣٤٥٦٧٨٩٠","الرقم: 1234567890")]
    [InlineData("کارت ۰۱۲۳۴۵۶۷۸۹","کارت 0123456789")]
    [InlineData("192.168.8.1 / ٨٧٢٨","192.168.8.1 / 8728")]
    [InlineData("شبكة ١٢٣ - ۲۳۴ X","شبكة 123 - 234 X")]
    [InlineData("كلمة عربية بلا أرقام","كلمة عربية بلا أرقام")]
    public void NormalizeAsciiDigits_Converts_Arabic_And_Persian_Decimal_Digits_Only(
        string input,string expected)
    {
        Assert.Equal(expected,AsciiDigitNormalizer.NormalizeAsciiDigits(input));
    }
}
