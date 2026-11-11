using System.IO;
using System.Windows.Documents;
using Microsoft.Win32;
using NajmCard.Core.Models;

namespace NajmCard.Desktop.Views;

/// <summary>
/// A saved print preview is only a snapshot. Re-check the selected source and
/// durable SENT state immediately before the Windows print spooler accepts it.
/// Preview PDFs are temporary and are not promoted to issued card archives.
/// </summary>
public partial class PrintingView
{
    private static bool VerifyPreparedBatchSnapshot(
        FixedDocument? document,IReadOnlyList<Voucher> prepared,
        IReadOnlyList<Voucher> latest,VoucherCardAppearance? previewStyle,
        VoucherCardAppearance currentStyle)
    {
        if(document is null || prepared.Count==0 || previewStyle is null)
            return false;
        var confirmed=latest.Where(v=>
            v.DeliveryStatus=="SENT"&&v.SentToRouter).ToArray();
        return confirmed.Length==prepared.Count &&
            confirmed.SequenceEqual(prepared) &&
            Equals(previewStyle,currentStyle.Validate());
    }

    public string? ChoosePdfDestination()
    {
        var dlg=new SaveFileDialog
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
