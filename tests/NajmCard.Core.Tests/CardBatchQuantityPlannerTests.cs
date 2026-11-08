using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>Regression: editable count/sheets sync must honor the actual
/// physically validated A4/A3 card layout and the hard 10k batch limit.</summary>
public sealed class CardBatchQuantityPlannerTests
{
    [Fact]
    public void CardsToSheets_Uses_Validated_Physical_Capacity()
    {
        var options=new CardPrintOptions();
        Assert.Equal(1,CardBatchQuantityPlanner.SheetsForCards(options,60));
        Assert.Equal(2,CardBatchQuantityPlanner.SheetsForCards(options,61));
        Assert.Equal(3,CardBatchQuantityPlanner.SheetsForCards(options,121));
    }

    [Fact]
    public void Editing_Sheets_Multiplies_Full_Grid_Capacity()
    {
        var options=new CardPrintOptions();
        Assert.Equal(120,CardBatchQuantityPlanner.CardsForSheets(options,2));
        var small=new CardPrintOptions(Rows:2,Columns:3);
        Assert.Equal(18,CardBatchQuantityPlanner.CardsForSheets(small,3));
    }

    [Fact]
    public void Impossible_Or_Dangerous_Requests_Fail_Before_Generation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            CardBatchQuantityPlanner.CardsForSheets(new CardPrintOptions(),0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            CardBatchQuantityPlanner.CardsForSheets(new CardPrintOptions(),168));
        Assert.Throws<ArgumentOutOfRangeException>(()=>
            CardBatchQuantityPlanner.SheetsForCards(new CardPrintOptions(),10001));
        Assert.Throws<ArgumentException>(()=>
            CardBatchQuantityPlanner.CardsForSheets(new CardPrintOptions(Rows:100),2));
    }
}
