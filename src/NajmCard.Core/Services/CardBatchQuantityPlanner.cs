namespace NajmCard.Core.Services;

/// <summary>
/// One physically validated source of truth for two-way quantity/sheet controls.
/// Full-sheets intent changes quantity; quantity intent rounds up sheet count.
/// This pure planner performs no RouterOS or database mutations.
/// </summary>
public static class CardBatchQuantityPlanner
{
    public static int SheetsForCards(CardPrintOptions options,int quantity)
    {
        if(quantity is <1 or >10000)
            throw new ArgumentOutOfRangeException(nameof(quantity),
                "عدد الكروت يجب أن يكون من 1 إلى 10000.");
        int capacity=CardPrintLayoutPlanner.Calculate(options,1).CapacityPerSheet;
        return (quantity+capacity-1)/capacity;
    }

    public static int CardsForSheets(CardPrintOptions options,int sheets)
    {
        if(sheets<1)
            throw new ArgumentOutOfRangeException(nameof(sheets),
                "عدد الصفحات يجب أن يكون أكبر من صفر.");
        int capacity=CardPrintLayoutPlanner.Calculate(options,1).CapacityPerSheet;
        long cards=(long)sheets*capacity;
        if(cards>10000)
            throw new ArgumentOutOfRangeException(nameof(sheets),
                "هذا العدد من الصفحات يتجاوز الحد الأقصى 10000 كرت.");
        return (int)cards;
    }
}
