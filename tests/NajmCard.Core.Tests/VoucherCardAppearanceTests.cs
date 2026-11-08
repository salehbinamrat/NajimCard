using NajmCard.Core.Models;
using Xunit;
namespace NajmCard.Core.Tests;

public sealed class VoucherCardAppearanceTests
{
    [Fact]
    public void Default_Najm_Visual_Style_Is_Validated_And_Usable()
    {
        var style=new VoucherCardAppearance().Validate();
        Assert.Equal("#FFF6EB",style.BackgroundColor);
        Assert.Equal("Arial",style.FontFamily);
        Assert.True(style.ShowPassword);
    }

    [Theory]
    [InlineData("#12345")]
    [InlineData("red")]
    [InlineData("#GG00FF")]
    [InlineData("#12\\n3456")]
    public void Invalid_Color_Cannot_Be_Used_To_Render_Card(string color)
    {
        Assert.ThrowsAny<ArgumentException>(()=>
            (new VoucherCardAppearance(BackgroundColor:color)).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(80)]
    public void Invalid_Font_Size_Is_Rejected(double size)
    {
        Assert.ThrowsAny<ArgumentException>(()=>
            (new VoucherCardAppearance(UsernameFontSize:size)).Validate());
    }

    [Fact]
    public void Blank_Title_Unsupported_Font_And_Invalid_Border_Are_Rejected()
    {
        Assert.ThrowsAny<ArgumentException>(()=>
            new VoucherCardAppearance(NetworkTitle:" ").Validate());
        Assert.ThrowsAny<ArgumentException>(()=>
            new VoucherCardAppearance(FontFamily:"Unknown Font").Validate());
        Assert.ThrowsAny<ArgumentException>(()=>
            new VoucherCardAppearance(BorderWidth:double.NaN).Validate());
    }
}
