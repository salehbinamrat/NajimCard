using System.Printing;
using System.Windows.Controls;
using NajmCard.Core.Models;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// Lazy, read-only selector loading. No RouterOS reads or password decryption
/// are performed when the user opens the print batch/printer dropdowns.
/// </summary>
public partial class PrintingView
{
    private (ComboBox BatchSelector,ComboBox PrinterSelector) CreatePrintSelectors(
        VoucherStore store,Func<IReadOnlyList<Voucher>>? selectedBatch)
    {
        var batchSelector=new ComboBox{MinWidth=0,ToolTip=
            "الدفعة الحالية أو دفعة سابقة مؤكدة الإرسال، دون طباعة حالات فاشلة."};
        _batchSelector=batchSelector;
        batchSelector.Items.Add(new ComboBoxItem
        {
            Content=selectedBatch is null?"الدفعة الأخيرة":"الدفعة الحالية",Tag=null
        });
        batchSelector.SelectedIndex=0;
        bool loadingBatches=false;
        batchSelector.DropDownOpened+=async (_,_) =>
        {
            if(loadingBatches)return;
            loadingBatches=true;
            try
            {
                var history=await Task.Run(()=>store.ListPrintableBatches());
                var previous=(batchSelector.SelectedItem as ComboBoxItem)?.Tag
                    as PrintableBatchSummary;
                batchSelector.Items.Clear();
                batchSelector.Items.Add(new ComboBoxItem
                {
                    Content=selectedBatch is null?"الدفعة الأخيرة":"الدفعة الحالية",
                    Tag=null
                });
                foreach(var info in history)
                    batchSelector.Items.Add(new ComboBoxItem
                    {
                        Content=info.DisplayName,ToolTip=info.DisplayName,Tag=info
                    });
                var selected=batchSelector.Items.OfType<ComboBoxItem>().FirstOrDefault(x=>
                    x.Tag is PrintableBatchSummary item && previous is not null &&
                    item.BatchId==previous.BatchId && item.Service==previous.Service);
                batchSelector.SelectedItem=selected??batchSelector.Items[0];
            }
            catch(Exception ex)
            {
                _status.Text="تعذر قراءة محفوظات الدفعات: "+ex.Message;
            }
            finally{loadingBatches=false;}
        };
        var selectedPrinter=new ComboBox
        {
            MinWidth=0,ToolTip="اختر طابعة مثبتة على Windows، أو اترك الافتراضية."
        };
        selectedPrinter.Items.Add("الافتراضية");
        selectedPrinter.SelectedIndex=0;
        bool loadingPrinters=false;
        selectedPrinter.DropDownOpened+=async (_,_) =>
        {
            if(loadingPrinters)return;
            loadingPrinters=true;
            try
            {
                var printers=await Task.Run(()=>
                {
                    using var server=new LocalPrintServer();
                    return server.GetPrintQueues().Select(q=>q.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x=>x,StringComparer.CurrentCultureIgnoreCase).ToArray();
                });
                var selected=selectedPrinter.SelectedItem?.ToString();
                selectedPrinter.Items.Clear();
                selectedPrinter.Items.Add("الافتراضية");
                foreach(var name in printers)selectedPrinter.Items.Add(name);
                selectedPrinter.SelectedItem=selected is not null &&
                    selectedPrinter.Items.Contains(selected)?selected: selectedPrinter.Items[0];
            }
            catch(Exception ex)
            {
                _status.Text="تعذر جلب قائمة الطابعات؛ ستبقى الطابعة الافتراضية: "+ex.Message;
            }
            finally{loadingPrinters=false;}
        };
        return (batchSelector,selectedPrinter);
    }
}
