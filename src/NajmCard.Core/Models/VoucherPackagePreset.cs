using NajmCard.Core.Services;

namespace NajmCard.Core.Models;

/// <summary>
/// Complete, isolated and durable snapshot for a specific MikroTik + service
/// + profile + User Manager customer. It contains no login credentials.
/// </summary>
public sealed record VoucherPackagePreset(
    string RouterKey, string Service, string Profile, string Customer,
    int Quantity, string Prefix, int UsernameLength, string UsernameCharacters,
    bool SamePassword, int PasswordLength, string PasswordCharacters,
    bool LengthIncludesPrefix, CardPrintOptions Print, VoucherCardAppearance Appearance,
    bool UsernameOnly=false)
{
    public VoucherPackagePreset Validate()
    {
        if(string.IsNullOrWhiteSpace(RouterKey) || RouterKey.Length>253 ||
            RouterKey.Any(char.IsControl) || RouterKey != RouterKey.Trim())
            throw new ArgumentException("عنوان الراوتر لإعدادات الباقة غير صالح.");
        if(Service is not ("hotspot" or "userman" or "pppoe"))
            throw new ArgumentException("خدمة الباقة غير صالحة.");
        if(string.IsNullOrWhiteSpace(Profile) || Profile.Length>120 ||
            Profile.Any(char.IsControl) || Profile != Profile.Trim())
            throw new ArgumentException("اسم الباقة غير صالح.");
        if(Customer is null || Customer.Length>100 ||
            Customer.Any(char.IsControl) ||
            (Service=="userman" && string.IsNullOrWhiteSpace(Customer)))
            throw new ArgumentException("حساب مدير User Manager غير صالح.");
        if(Quantity is <1 or >10000 || UsernameLength is <4 or >32 ||
            PasswordLength is <1 or >64)
            throw new ArgumentException("عدد الكروت أو طول المستخدم أو كلمة المرور خارج الحدود.");
        if(Prefix is null || Prefix.Length>24 ||
            Prefix.Any(c=>!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
            throw new ArgumentException("بادئة رقم الكرت غير صالحة.");
        if(LengthIncludesPrefix && Prefix.Length>=UsernameLength)
            throw new ArgumentException("بادئة الرقم يجب أن تكون أقصر من الطول الكلي للكرت.");
        static bool ValidAlphabet(string? value,int minCount)
            => value is not null &&
               value.Distinct().Count()>=minCount &&
               value.All(char.IsAsciiLetterOrDigit);
        if(!ValidAlphabet(UsernameCharacters,2) ||
           (!SamePassword && !ValidAlphabet(PasswordCharacters,1)))
            throw new ArgumentException("أحرف الرقم أو كلمة المرور غير صالحة.");
        ArgumentNullException.ThrowIfNull(Print);
        ArgumentNullException.ThrowIfNull(Appearance);
        Appearance.Validate();
        CardPrintLayoutPlanner.Calculate(Print,Quantity);
        return this;
    }
}
