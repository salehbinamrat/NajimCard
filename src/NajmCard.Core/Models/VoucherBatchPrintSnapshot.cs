using NajmCard.Core.Services;

namespace NajmCard.Core.Models;

/// <summary>
/// Operator-controlled local print/export preferences. Never includes any
/// MikroTik login credentials. Printer names are restored exactly, so an
/// unavailable printer cannot silently redirect a job to another printer.
/// </summary>
public sealed record VoucherOutputPreferences(
    string PrinterName="الافتراضية",
    bool AdditionalExport=false, bool ExportCsv=false, bool ExportExcel=false)
{
    public VoucherOutputPreferences Validate()
    {
        if (string.IsNullOrWhiteSpace(PrinterName) || PrinterName.Length>256 ||
            PrinterName.Any(char.IsControl))
            throw new ArgumentException("اسم الطابعة المحفوظ غير صالح.");
        return this;
    }
}

/// <summary>
/// Historical snapshot belongs to the issuing service + batch identifier,
/// not whichever package is currently selected in the editor.
/// </summary>
public sealed record VoucherBatchPrintSnapshot(
    string Service, string BatchId, string Profile, string Customer,
    CardPrintOptions Print, VoucherCardAppearance Appearance,
    VoucherOutputPreferences? Output=null)
{
    public VoucherBatchPrintSnapshot Validate()
    {
        if(Service is not ("hotspot" or "userman" or "pppoe") ||
           string.IsNullOrWhiteSpace(BatchId) || BatchId.Length>256 ||
           BatchId.Any(char.IsControl) ||
           string.IsNullOrWhiteSpace(Profile) || Profile.Length>120 ||
           Profile.Any(char.IsControl) || Customer is null || Customer.Length>100)
            throw new ArgumentException("معرّف دفعة الطباعة المحفوظة غير صالح.");
        if(Service=="userman" && string.IsNullOrWhiteSpace(Customer))
            throw new ArgumentException("حساب المدير مطلوب في دفعة User Manager.");
        ArgumentNullException.ThrowIfNull(Print);
        ArgumentNullException.ThrowIfNull(Appearance);
        Appearance.Validate();
        (Output??new VoucherOutputPreferences()).Validate();
        CardPrintLayoutPlanner.Calculate(Print,1);
        return this;
    }
}
