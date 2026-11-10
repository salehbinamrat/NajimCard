using System.Text.RegularExpressions;

namespace NajmCard.Core.Models;

public sealed record CardElementPosition(double X, double Y);

/// <summary>Safe, reusable NajmCard card design settings (never a voucher password).</summary>
public sealed record VoucherCardAppearance(
    string NetworkTitle = "نجم كارد",
    string FontFamily = "Arial",
    double UsernameFontSize = 18,
    string BackgroundColor = "#FFF6EB",
    string UsernameColor = "#4C1730",
    string BorderColor = "#B89159",
    double BorderWidth = 1,
    bool ShowPassword = true,
    bool ShowProfile = true,
    string TemplateImagePath = "",
    CardElementPosition? TitlePosition = null,
    CardElementPosition? CredentialsPosition = null,
    CardElementPosition? ProfilePosition = null,
    CardElementPosition? PasswordPosition = null,
    double TitleScale = 1,
    double PasswordScale = 1,
    double ProfileScale = 1,
    bool ShowTitle = true,
    bool ShowCapacity = false,
    bool ShowPhone = false,
    bool ShowQr = false,
    bool ShowTime = false,
    bool ShowDate = true,
    bool ShowSequence = false,
    bool ShowRoute = false,
    string CapacityText = "",
    string PhoneText = "",
    double QrSize = 24,
    CardElementPosition? CapacityPosition = null,
    CardElementPosition? PhonePosition = null,
    CardElementPosition? QrPosition = null,
    CardElementPosition? TimePosition = null,
    CardElementPosition? DatePosition = null,
    CardElementPosition? SequencePosition = null,
    CardElementPosition? RoutePosition = null,
    string? ElementColor = null)
{
    // Presets saved before the colors were split used the same ink for every element.
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveElementColor => ElementColor ?? UsernameColor;

    private static readonly HashSet<string> AllowedFonts = new(
        ["Arial","Segoe UI","Tahoma","Calibri"],StringComparer.OrdinalIgnoreCase);

    public VoucherCardAppearance Validate()
    {
        if(string.IsNullOrWhiteSpace(NetworkTitle) || NetworkTitle.Length > 48 ||
            NetworkTitle.Any(char.IsControl))
            throw new ArgumentException("اسم الشبكة على الكرت يجب ألا يكون فارغا أو يتجاوز 48 حرفا.");
        if(!AllowedFonts.Contains(FontFamily))
            throw new ArgumentException("نوع الخط غير مدعوم؛ اختر Arial أو Tahoma أو Segoe UI أو Calibri.");
        if(!double.IsFinite(UsernameFontSize) || UsernameFontSize is < 8 or > 40)
            throw new ArgumentException("حجم رقم الكرت يجب أن يكون بين 8 و40.");
        if(!double.IsFinite(BorderWidth) || BorderWidth is < 0 or > 5)
            throw new ArgumentException("سمك حدود الكرت يجب أن يكون بين 0 و5.");
        if(TemplateImagePath is null || TemplateImagePath.Length>1024 ||
            (TemplateImagePath.Length>0 && !Path.IsPathFullyQualified(TemplateImagePath)))
            throw new ArgumentException("مسار صورة القالب المحلية غير صالح.");
        foreach(var hex in new[] {BackgroundColor,UsernameColor,BorderColor,EffectiveElementColor})
        {
            if(hex is null || !Regex.IsMatch(hex,@"^#[0-9a-fA-F]{6}$",
                RegexOptions.CultureInvariant))
                throw new ArgumentException("لون الكرت يجب أن يكون بصيغة #RRGGBB.");
        }
        foreach(var position in new[]{TitlePosition,CredentialsPosition,ProfilePosition,PasswordPosition,
            CapacityPosition,PhonePosition,QrPosition,TimePosition,DatePosition,SequencePosition,RoutePosition})
        {
            if(position is not null && (!double.IsFinite(position.X) || !double.IsFinite(position.Y)
                || position.X is < 0 or > 1 || position.Y is < 0 or > 1))
                throw new ArgumentException("موضع عنصر الكرت خارج حدود التصميم.");
        }
        foreach(var scale in new[]{TitleScale,PasswordScale,ProfileScale})
        {
            if(!double.IsFinite(scale) || scale is < 0.4 or > 4)
                throw new ArgumentException("حجم عنصر الكرت خارج النطاق المسموح.");
        }
        if(!double.IsFinite(QrSize) || QrSize is < 12 or > 72)
            throw new ArgumentException("حجم رمز QR يجب أن يكون بين 12 و72.");
        if(PhoneText is null || PhoneText.Length>48 || PhoneText.Any(char.IsControl) ||
           CapacityText is null || CapacityText.Length>48 || CapacityText.Any(char.IsControl))
            throw new ArgumentException("بيانات الهاتف أو السعة غير صالحة.");
        // WCAG relative luminance: keep voucher numbers legible regardless
        // of a custom dark burgundy or a bright paper background.
        if(ContrastRatio(BackgroundColor,UsernameColor)<4.5)
            throw new ArgumentException(
                "لون رقم الكرت قريب جدا من لون الخلفية؛ اختر لونا أوضح لتظهر الخطوط بارزة.");
        return this;
    }

    private static double ContrastRatio(string a,string b)
    {
        static double Luminance(string hex)
        {
            static double Channel(int c)
            {
                double v=c/255.0;
                return v<=0.04045 ? v/12.92 : Math.Pow((v+0.055)/1.055,2.4);
            }
            return 0.2126*Channel(Convert.ToInt32(hex.Substring(1,2),16))+
                   0.7152*Channel(Convert.ToInt32(hex.Substring(3,2),16))+
                   0.0722*Channel(Convert.ToInt32(hex.Substring(5,2),16));
        }
        var l1=Luminance(a);var l2=Luminance(b);
        return (Math.Max(l1,l2)+0.05)/(Math.Min(l1,l2)+0.05);
    }
}
