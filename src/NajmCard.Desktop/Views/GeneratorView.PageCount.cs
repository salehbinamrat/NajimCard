using System.Globalization;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

public partial class GeneratorView
{
    private bool _syncingPageCount;
    private bool _quantityIsDerivedFromSheets;

    /// <summary>
    /// Full bidirectional contract of Hadramout: editing cards shows the minimum
    /// physical sheets, editing sheets requests full 15x4 (or customized) grids.
    /// The same validated planner owns both directions. Never mutate the router.
    /// </summary>
    public void RefreshPageCount()
    {
        if(_syncingPageCount)return;
        _syncingPageCount=true;
        try
        {
            var options=PrintOptionsProvider?.Invoke()??new CardPrintOptions();
            if(_quantityIsDerivedFromSheets)
            {
                if(!int.TryParse(_pagesEstimate.Text,NumberStyles.Integer,
                    CultureInfo.InvariantCulture,out var sheets)||sheets<1)
                {
                    _count.Text="—";
                    return;
                }
                _count.Text=CardBatchQuantityPlanner.CardsForSheets(options,sheets)
                    .ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                if(!int.TryParse(_count.Text,NumberStyles.Integer,
                    CultureInfo.InvariantCulture,out var count)||count<1)
                {
                    _pagesEstimate.Text="—";
                    return;
                }
                _pagesEstimate.Text=CardBatchQuantityPlanner.SheetsForCards(options,count)
                    .ToString(CultureInfo.InvariantCulture);
            }
        }
        catch(ArgumentException ex)
        {
            // Invalidate only the dependent field; never silently generate
            // the former card count on an impossible print layout.
            if(_quantityIsDerivedFromSheets)_count.Text="—";
            else _pagesEstimate.Text="—";
            _status.Text="تحقق من حساب الصفحات: "+ex.Message;
        }
        catch(OverflowException)
        {
            if(_quantityIsDerivedFromSheets)_count.Text="—";
            else _pagesEstimate.Text="—";
            _status.Text="عدد الصفحات أو الكروت كبير جدا.";
        }
        finally {_syncingPageCount=false;}
    }

}
