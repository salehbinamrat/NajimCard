using NajmCard.Core.Models;
using Xunit;
namespace NajmCard.Core.Tests;
public sealed class VoucherTypographyContrastTests
{
    [Fact]
    public void Light_And_Dark_Card_Themes_Are_Compatible_With_Bold_Readable_Numbers()
    {
        Assert.NotNull(new VoucherCardAppearance().Validate());
        Assert.NotNull(new VoucherCardAppearance(BackgroundColor:"#42152F",
            UsernameColor:"#FFE2A3",UsernameFontSize:20).Validate());
    }
    [Fact]
    public void Nearly_Identical_Text_And_Background_Colors_Are_Rejected()
    {
        Assert.Throws<ArgumentException>(()=>
            new VoucherCardAppearance(BackgroundColor:"#42152F",
                UsernameColor:"#43162E").Validate());
    }
}
