using System.Windows.Controls;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

public partial class PrintingView
{
    private VoucherBatchPrintSnapshot? _selectedArchiveTemplate;

    private void RestoreArchivePrintTemplate(PrintableBatchSummary? selected,
        VoucherStore store,Action<CardPrintOptions> applyPrint)
    {
        _selectedArchiveTemplate=null;
        if(selected is null)return;
        try
        {
            var snapshot=store.LoadBatchPrintSnapshot(selected.Service,selected.BatchId);
            if(snapshot is null)
            {
                _status.Text="هذه دفعة قديمة دون قالب محفوظ. تحقق من التصميم "+
                    "وإعدادات الورق الحالية قبل الطباعة؛ لن يتم استخدام قالب باقة أخرى بصمت.";
                return;
            }
            _selectedArchiveTemplate=snapshot;
            applyPrint(snapshot.Print);
            ApplyOutputPreferences(snapshot.Output??new VoucherOutputPreferences());
            _status.Text="استُعيد قالب الطباعة التاريخي لدفعة "+
                selected.BatchId+" دون تغيير إعدادات باقة أخرى.";
        }
        catch(Exception ex)
        {
            _selectedArchiveTemplate=null;
            _status.Text="تعذر استعادة قالب الدفعة السابقة: "+ex.Message+
                "؛ لا تطبع قبل مراجعة القالب.";
        }
    }

    private static VoucherBatchPrintSnapshot SnapshotForPrint(
        IReadOnlyList<Voucher> cards,CardPrintOptions options,
        VoucherCardAppearance appearance,VoucherOutputPreferences output)
    {
        if(cards.Count==0)
            throw new ArgumentException("لا توجد كروت محفوظة لهذه الدفعة.");
        var first=cards[0];
        if(cards.Any(v=>v.Service!=first.Service || v.BatchId!=first.BatchId ||
            v.Profile!=first.Profile || v.Customer!=first.Customer))
            throw new InvalidOperationException(
                "لا يمكن جمع كروت باقات أو دفعات أو حسابات مدير مختلفة في مهمة طباعة واحدة.");
        return new VoucherBatchPrintSnapshot(
            first.Service,first.BatchId,first.Profile,first.Customer,
            options,appearance,output).Validate();
    }
}
