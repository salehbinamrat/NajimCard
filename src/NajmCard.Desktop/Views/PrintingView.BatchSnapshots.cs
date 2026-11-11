using System.IO;
using System.Windows.Controls;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

public partial class PrintingView
{
    private VoucherBatchPrintSnapshot? _selectedArchiveTemplate;
    private bool _archiveTemplateReadFailed;
    private bool _viewingArchive;
    private CardPrintOptions? _currentEditorPrint;
    private VoucherOutputPreferences? _currentEditorOutput;

    private void RestoreArchivePrintTemplate(PrintableBatchSummary? selected,
        VoucherStore store,Action<CardPrintOptions> applyPrint,
        Func<CardPrintOptions> readCurrentPrint)
    {
        _selectedArchiveTemplate=null;
        _archiveTemplateReadFailed=false;
        if(selected is null)
        {
            if(_viewingArchive)
            {
                // Switching back must not leave an archived package's
                // physical paper or printer attached to the active editor.
                if(_currentEditorPrint is not null)applyPrint(_currentEditorPrint);
                if(_currentEditorOutput is not null)
                    ApplyOutputPreferences(_currentEditorOutput);
            }
            _viewingArchive=false;
            _currentEditorPrint=null;
            _currentEditorOutput=null;
            return;
        }
        if(!_viewingArchive)
        {
            try {_currentEditorPrint=readCurrentPrint();}
            catch(ArgumentException) {_currentEditorPrint=null;}
            _currentEditorOutput=GetOutputPreferences();
        }
        _viewingArchive=true;
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
            _archiveTemplateReadFailed=true;
            _status.Text="تعذر استعادة قالب الدفعة السابقة: "+ex.Message+
                "؛ لا تطبع قبل مراجعة القالب.";
        }
    }

    private (Voucher[] Cards,VoucherCardAppearance Style,CardPrintOptions Options)?
        ConfirmPrintJob(VoucherStore store,ComboBox batchSelector,
        Func<IReadOnlyList<Voucher>>? currentBatch,
        Func<VoucherCardAppearance>? currentAppearance,
        Func<CardPrintOptions> currentOptions,int maxCount)
    {
        var archive=(batchSelector.SelectedItem as ComboBoxItem)?.Tag
            as PrintableBatchSummary;
        IReadOnlyList<Voucher> requested;
        if(archive is not null)
            requested=store.GetBatchVouchers(archive.Service,archive.BatchId);
        else if(currentBatch is not null)
            requested=currentBatch();
        else
        {
            // A "latest" print must mean ONE batch; the old global query
            // mixed credentials from unrelated services and packages.
            var latest=store.ListPrintableBatches(1).FirstOrDefault();
            requested=latest is null?[]:
                store.GetBatchVouchers(latest.Service,latest.BatchId)
                    .Take(maxCount).ToArray();
        }

        var cards=requested.Where(card=>card.SentToRouter &&
            card.DeliveryStatus=="SENT").ToArray();
        if(cards.Length==0)
        {
            _status.Text="لا توجد كروت مؤكدة الإرسال لهذه الدفعة. "+
                "لا تُطبع الحالات CREATED/UNKNOWN/FAILED.";
            return null;
        }
        if(cards.Select(card=>card.Username)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count()!=cards.Length)
            throw new InvalidOperationException("أرقام كروت مكررة في الدفعة؛ أُوقفت الطباعة.");

        if(archive is not null && _archiveTemplateReadFailed)
            throw new InvalidDataException(
                "قالب الدفعة المحفوظ غير قابل للقراءة. يجب إصلاح بياناته قبل الطباعة.");
        if(archive is not null && _selectedArchiveTemplate is null &&
            !PageUi.Confirm(
                "هذه دفعة قديمة بلا قالب طباعة محفوظ. هل تريد استخدام "+
                "الإعدادات الحالية بعد مراجعة أبعاد الورق والتصميم؟"))
            return null;

        var style=(_selectedArchiveTemplate?.Appearance ??
            currentAppearance?.Invoke()??store.LoadCardAppearance()).Validate();
        var options=currentOptions();
        // Also prevents printing two different profiles under one shared layout.
        _=SnapshotForPrint(cards,options,style,GetOutputPreferences());
        if(!PageUi.Confirm("هل تريد معاينة "+cards.Length+
            " كرتا مؤكدا إرساله، "+
            (style.ShowPassword?"مع كلمات المرور؟":"دون كلمات المرور؟")))
            return null;
        return (cards,style,options);
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
    private static bool VerifyPreparedBatchSnapshot(
        System.Windows.Documents.FixedDocument? preparedDocument,
        IReadOnlyList<Voucher> preparedCards,IReadOnlyList<Voucher> latestCards,
        VoucherCardAppearance? preparedAppearance,VoucherCardAppearance latestAppearance)
    {
        if(preparedDocument is null || preparedCards.Count==0 ||
           preparedAppearance is null)return false;
        var confirmed=latestCards.Where(card=>card.SentToRouter &&
            card.DeliveryStatus=="SENT").ToArray();
        return confirmed.Length==preparedCards.Count &&
            confirmed.SequenceEqual(preparedCards) &&
            Equals(preparedAppearance,latestAppearance.Validate());
    }

    public string? ChoosePdfDestination()
    {
        var dlg=new Microsoft.Win32.SaveFileDialog
        {
            Title="حفظ PDF للدفعة",
            Filter="PDF (*.pdf)|*.pdf",
            DefaultExt=".pdf",AddExtension=true,
            FileName="نجم_كارد_"+DateTime.Now.ToString("yyyyMMdd_HHmm")+".pdf",
            OverwritePrompt=true
        };
        return dlg.ShowDialog()==true?dlg.FileName:null;
    }

    private static string NewPreviewPdfPath()
    {
        var directory=Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),"NajmCard","Preview");
        Directory.CreateDirectory(directory);
        foreach(var file in Directory.EnumerateFiles(directory,"Najm_*.pdf"))
        {
            try
            {
                if(File.GetLastWriteTimeUtc(file)<DateTime.UtcNow.AddDays(-2))
                    File.Delete(file);
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }
        return Path.Combine(directory,"Najm_"+Guid.NewGuid().ToString("N")+".pdf");
    }


}
