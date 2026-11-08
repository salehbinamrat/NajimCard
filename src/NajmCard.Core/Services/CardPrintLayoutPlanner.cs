namespace NajmCard.Core.Services;

/// <summary>Hadramout A4/A3 card print geometry, separate from printer hardware.</summary>
public sealed record CardPrintOptions(
    int Rows = 15, int Columns = 4,
    double CardWidthMm = 50, double CardHeightMm = 19,
    double GapHorizontalMm = 0.5, double GapVerticalMm = 0.5,
    double MarginMm = 0.5,
    string Paper = "A4", string Orientation = "portrait",
    string Order = "left_to_right", string Duplex = "single");

public sealed record CardPrintSlot(int VoucherIndex, int PageIndex, bool Back,
    int Row, int Column, double LeftMm, double TopMm);

public sealed record CardPrintPlan(double PageWidthMm, double PageHeightMm,
    int CapacityPerSheet, int PhysicalSheets, int PdfPages,
    IReadOnlyList<CardPrintSlot> Slots);

/// <summary>
/// Pure, deterministic physical layout shared by screen preview and print.
/// Same default 50 x 19 mm, A4, 15 x 4 and slot order as Hadramout printing.py.
/// The duplex geometry is calculable; the WPF UI deliberately prints single-sided
/// until a real duplex/backside-template acceptance test is available.
/// </summary>
public static class CardPrintLayoutPlanner
{
    public static CardPrintPlan Calculate(CardPrintOptions options, int quantity)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (quantity < 0 || quantity > 10000)
            throw new ArgumentOutOfRangeException(nameof(quantity),
                "عدد الكروت للطباعة يجب أن يكون من 0 إلى 10000.");
        if (options.Rows is < 1 or > 1000 || options.Columns is < 1 or > 1000)
            throw new ArgumentException("عدد الصفوف والأعمدة يجب أن يكون بين 1 و1000.");
        Check(options.CardWidthMm, "عرض الكرت", 1, 300);
        Check(options.CardHeightMm, "ارتفاع الكرت", 1, 300);
        Check(options.GapHorizontalMm, "الفاصل الأفقي", 0, 300);
        Check(options.GapVerticalMm, "الفاصل الرأسي", 0, 300);
        Check(options.MarginMm, "الهامش", 0, 300);
        if (options.Paper is not ("A4" or "A3"))
            throw new ArgumentException("حجم الورق غير مدعوم؛ اختر A4 أو A3.");
        if (options.Orientation is not ("portrait" or "landscape"))
            throw new ArgumentException("اتجاه الورقة غير صالح.");
        if (options.Order is not ("left_to_right" or "right_to_left" or
            "snake" or "column_left" or "column_right"))
            throw new ArgumentException("ترتيب الكروت غير صالح.");
        if (options.Duplex is not ("single" or "long-edge" or "short-edge"))
            throw new ArgumentException("وضع الطباعة على الوجهين غير صالح.");

        double width=options.Paper=="A3"?297:210;
        double height=options.Paper=="A3"?420:297;
        if (options.Orientation=="landscape")
            (width,height)=(height,width);
        var gridWidth=options.Columns*options.CardWidthMm+
            (options.Columns-1)*options.GapHorizontalMm;
        var gridHeight=options.Rows*options.CardHeightMm+
            (options.Rows-1)*options.GapVerticalMm;
        if (gridWidth > width-2*options.MarginMm+0.00001 ||
            gridHeight > height-2*options.MarginMm+0.00001)
            throw new ArgumentException(
                "تخطيط الكروت أكبر من مساحة الورقة؛ قلل الصفوف أو الأعمدة أو الأبعاد أو الفواصل.");
        var startLeft=(width-gridWidth)/2;
        var startTop=(height-gridHeight)/2;
        var capacity=checked(options.Rows*options.Columns);
        var sheets=quantity==0?0:(quantity+capacity-1)/capacity;
        var sides=options.Duplex=="single"?1:2;
        var slots=new List<CardPrintSlot>(quantity*sides);

        for (var sheet=0;sheet<sheets;sheet++)
        {
            var cardsOnSheet=Math.Min(capacity,quantity-sheet*capacity);
            for (var side=0;side<sides;side++)
            {
                for (var i=0;i<cardsOnSheet;i++)
                {
                    int row,column;
                    if (options.Order is "column_left" or "column_right")
                    {
                        row=i%options.Rows;
                        column=i/options.Rows;
                        if(options.Order=="column_right")
                            column=options.Columns-1-column;
                    }
                    else
                    {
                        row=i/options.Columns;
                        column=i%options.Columns;
                        if(options.Order=="right_to_left" ||
                            options.Order=="snake" && row%2==1)
                            column=options.Columns-1-column;
                    }
                    var left=startLeft+column*(options.CardWidthMm+options.GapHorizontalMm);
                    var top=startTop+row*(options.CardHeightMm+options.GapVerticalMm);
                    if(side==1)
                    {
                        var mirrorHorizontal=(options.Duplex=="long-edge") ==
                            (options.Orientation=="portrait");
                        if(mirrorHorizontal)left=width-left-options.CardWidthMm;
                        else top=height-top-options.CardHeightMm;
                    }
                    slots.Add(new CardPrintSlot(sheet*capacity+i,sheet*sides+side,
                        side==1,row,column,left,top));
                }
            }
        }
        return new CardPrintPlan(width,height,capacity,sheets,sheets*sides,slots);
    }

    private static void Check(double number,string label,double minimum,double maximum)
    {
        if (!double.IsFinite(number) || number<minimum || number>maximum)
            throw new ArgumentException("قيمة غير صالحة: "+label);
    }
}
