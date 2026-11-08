using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// Physical layout regression against Hadramout core/printing.py grid,
/// slot orders and duplex mirroring; printer I/O is not mocked here.
/// </summary>
public sealed class CardPrintLayoutPlannerTests
{
    [Fact]
    public void Original_A4_50x19mm_Default_Fits_Sixty_Cards_Per_Sheet()
    {
        var plan=CardPrintLayoutPlanner.Calculate(new CardPrintOptions(),61);
        Assert.Equal(210d,plan.PageWidthMm);
        Assert.Equal(297d,plan.PageHeightMm);
        Assert.Equal(60,plan.CapacityPerSheet);
        Assert.Equal(2,plan.PhysicalSheets);
        Assert.Equal(2,plan.PdfPages);
        Assert.Equal(61,plan.Slots.Count);
        Assert.Equal(4.25d,plan.Slots[0].LeftMm,3);
        Assert.Equal(2.5d,plan.Slots[0].TopMm,3);
        Assert.Equal(1,plan.Slots[60].PageIndex);
        Assert.Equal(0,plan.Slots[60].Row);
        Assert.Equal(0,plan.Slots[60].Column);
    }

    [Fact]
    public void RightToLeft_And_Snake_Use_Actual_Column_Slots()
    {
        var rtl=CardPrintLayoutPlanner.Calculate(
            new CardPrintOptions(Rows:2,Columns:3,Order:"right_to_left"),4);
        Assert.Equal(2,rtl.Slots[0].Column);
        Assert.Equal(1,rtl.Slots[1].Column);
        Assert.Equal(0,rtl.Slots[2].Column);
        var snake=CardPrintLayoutPlanner.Calculate(
            new CardPrintOptions(Rows:2,Columns:3,Order:"snake"),6);
        Assert.Equal(0,snake.Slots[0].Column);
        Assert.Equal(2,snake.Slots[3].Column);
        Assert.Equal(0,snake.Slots[5].Column);
    }

    [Fact]
    public void ColumnOrder_Uses_Rows_Before_Advancing_To_Next_Column()
    {
        var left=CardPrintLayoutPlanner.Calculate(
            new CardPrintOptions(Rows:2,Columns:3,Order:"column_left"),6);
        Assert.Equal((0,0),(left.Slots[0].Row,left.Slots[0].Column));
        Assert.Equal((1,0),(left.Slots[1].Row,left.Slots[1].Column));
        Assert.Equal((0,1),(left.Slots[2].Row,left.Slots[2].Column));
        var right=CardPrintLayoutPlanner.Calculate(
            new CardPrintOptions(Rows:2,Columns:3,Order:"column_right"),6);
        Assert.Equal((0,2),(right.Slots[0].Row,right.Slots[0].Column));
        Assert.Equal((1,1),(right.Slots[3].Row,right.Slots[3].Column));
    }

    [Fact]
    public void A3_Landscape_Swaps_Physical_Paper_Dimensions()
    {
        var plan=CardPrintLayoutPlanner.Calculate(
            new CardPrintOptions(Rows:2,Columns:4,Paper:"A3",Orientation:"landscape"),2);
        Assert.Equal(420d,plan.PageWidthMm);
        Assert.Equal(297d,plan.PageHeightMm);
        Assert.Equal(8,plan.CapacityPerSheet);
        Assert.Equal(1,plan.PhysicalSheets);
    }

    [Fact]
    public void Duplex_Mirrors_Long_And_Short_Edge_For_Portrait()
    {
        var a=CardPrintLayoutPlanner.Calculate(
            new CardPrintOptions(Duplex:"long-edge"),1);
        Assert.Equal(1,a.PhysicalSheets);
        Assert.Equal(2,a.PdfPages);
        Assert.Equal(2,a.Slots.Count);
        Assert.False(a.Slots[0].Back);
        Assert.True(a.Slots[1].Back);
        Assert.Equal(210-50-a.Slots[0].LeftMm,a.Slots[1].LeftMm,3);
        Assert.Equal(a.Slots[0].TopMm,a.Slots[1].TopMm,3);
        var b=CardPrintLayoutPlanner.Calculate(
            new CardPrintOptions(Duplex:"short-edge"),1);
        Assert.Equal(a.Slots[0].LeftMm,b.Slots[1].LeftMm,3);
        Assert.Equal(297-19-b.Slots[0].TopMm,b.Slots[1].TopMm,3);
    }

    [Fact]
    public void Oversized_Layout_Or_Invalid_Input_Fails_Without_Creating_Pages()
    {
        Assert.Throws<ArgumentException>(()=>CardPrintLayoutPlanner.Calculate(
            new CardPrintOptions(Rows:100),1));
        Assert.Throws<ArgumentException>(()=>CardPrintLayoutPlanner.Calculate(
            new CardPrintOptions(GapHorizontalMm:double.NaN),1));
        Assert.Throws<ArgumentException>(()=>CardPrintLayoutPlanner.Calculate(
            new CardPrintOptions(CardWidthMm:0),1));
        Assert.Throws<ArgumentException>(()=>CardPrintLayoutPlanner.Calculate(
            new CardPrintOptions(Paper:"Letter"),1));
        Assert.Throws<ArgumentException>(()=>CardPrintLayoutPlanner.Calculate(
            new CardPrintOptions(Order:"unknown"),1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>CardPrintLayoutPlanner.Calculate(
            new CardPrintOptions(),10001));
    }

    [Fact]
    public void Empty_Selection_Has_Zero_Sheets_And_Zero_Slots()
    {
        var plan=CardPrintLayoutPlanner.Calculate(new CardPrintOptions(),0);
        Assert.Equal(0,plan.PhysicalSheets);
        Assert.Equal(0,plan.PdfPages);
        Assert.Empty(plan.Slots);
    }
}
