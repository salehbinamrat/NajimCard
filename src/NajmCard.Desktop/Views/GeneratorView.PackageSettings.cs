using System.Globalization;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// One per-router, per-service, per-profile configuration, following the
/// Hadramout package snapshot contract without touching router profiles.
/// </summary>
public partial class GeneratorView
{
    public bool SaveCurrentPackage()
    {
        try
        {
            if(_sending||_preparing||_restoringPreset)
                throw new InvalidOperationException("لا يمكن حفظ إعدادات الباقة أثناء إرسال الكروت أو استعادة الإعدادات.");
            var profile=_profile.SelectedItem?.ToString()??"";
            RequireRouteSelection();
            if(SelectedService=="hotspot" && _server.SelectedItem?.ToString()!="all")
                throw new InvalidOperationException(
                    "السيرفر المختار ظاهر للفحص، لكن إرسال كروت لسيرفر محدد غير متاح بعد. "+
                    "اختر all حتى لا توزع الكروت على السيرفر الخطأ.");
            int length=int.Parse(_length.Text,CultureInfo.InvariantCulture);
            bool usernameOnly=_credentialMode.SelectedIndex==0;
            bool same=_samePassword.IsChecked==true;
            var saved=new VoucherPackagePreset(_routerKey,SelectedService,profile,
                SelectedService=="userman"?_customer.SelectedItem?.ToString()??"":"",
                int.Parse(_count.Text,CultureInfo.InvariantCulture),_prefix.Text.Trim(),
                length,_usernameCharacters.Text.Trim(),same,
                usernameOnly||same?length:int.Parse(_passwordLength.Text,CultureInfo.InvariantCulture),
                _passwordCharacters.Text.Trim(),_lengthWithPrefix.IsChecked==true,
                PrintOptionsProvider?.Invoke()??new CardPrintOptions(),ReadCurrentAppearance(),
                usernameOnly,
                OutputPreferencesProvider?.Invoke()??new VoucherOutputPreferences()).Validate();
            _store.SavePackagePreset(saved);
            // Adopt the local copy made by the store immediately; a removed
            // external image must not break print preview in this session.
            var persisted=_store.LoadPackagePreset(_routerKey,SelectedService,
                profile,SelectedService=="userman"
                    ?_customer.SelectedItem?.ToString()??"":"");
            ApplyCardAppearance(persisted?.Appearance??saved.Appearance);
            _status.Text="حُفظت إعدادات الباقة "+profile+" حتى بعد إغلاق البرنامج.";
            return true;
        }
        catch(Exception ex){_status.Text="تعذر حفظ إعدادات الباقة: "+ex.Message;return false;}
    }
    // Restore current package preferences without changing navigation or router data.
    public void RestoreSavedPackageSettings() => RestorePackagePreset();
    private void RestorePackagePreset()
    {
        if(_restoringPreset)return;
        string profile=_profile.SelectedItem?.ToString()??"";
        string owner=SelectedService=="userman"?_customer.SelectedItem?.ToString()??"":"";
        if(profile.Length==0||(SelectedService=="userman"&&owner.Length==0))return;
        try
        {
            var v=_store.LoadPackagePreset(_routerKey,SelectedService,profile,owner);
            _restoringPreset=true;
            try
            {
                _count.Text=(v?.Quantity??60).ToString(CultureInfo.InvariantCulture);
                _prefix.Text=v?.Prefix??"";
                _length.Text=(v?.UsernameLength??10).ToString(CultureInfo.InvariantCulture);
                _usernameCharacters.Text=v?.UsernameCharacters??VoucherGenerator.LowercaseMixed;
                _passwordCharacters.Text=v?.PasswordCharacters??VoucherGenerator.LowercaseMixed;
                static int AlphabetIndex(string value)=>value switch
                {
                    VoucherGenerator.Digits=>0,
                    VoucherGenerator.LowercaseLetters=>1,
                    VoucherGenerator.LowercaseMixed=>2,
                    VoucherGenerator.UppercaseLetters=>3,
                    VoucherGenerator.Mixed=>4,
                    _=>5
                };
                _usernameAlphabetType.SelectedIndex=AlphabetIndex(_usernameCharacters.Text);
                _passwordAlphabetType.SelectedIndex=AlphabetIndex(_passwordCharacters.Text);
                _passwordLength.Text=(v?.PasswordLength??6).ToString(CultureInfo.InvariantCulture);
                _samePassword.IsChecked=v?.SamePassword??true;
                _credentialsSame.IsChecked=v?.SamePassword??true;
                _credentialsDifferent.IsChecked=!(v?.SamePassword??true);
                // A never-saved package follows Hadramout's username-only default.
                _credentialMode.SelectedIndex=v?.UsernameOnly==false?1:0;
                _lengthWithPrefix.IsChecked=v?.LengthIncludesPrefix??true;
                var d=v?.Appearance??new VoucherCardAppearance(NetworkTitle:"نجم نت");
                LoadCardAppearanceFields(d);
                PackagePrintSettingsRestored?.Invoke(v?.Print??new CardPrintOptions());
                ApplyOutputPreferences?.Invoke(v?.Output??new VoucherOutputPreferences());
                _status.Text=v is null?"لا يوجد قالب محفوظ لهذه الباقة.":
                    "استُعيدت إعدادات التوليد والتصميم والطباعة المحفوظة لهذه الباقة.";
            }
            finally {_restoringPreset=false;UpdateLiveCardPreview();}
        }
        catch(Exception ex){_status.Text="تعذر استعادة الباقة؛ لم يتغير السجل: "+ex.Message;}
    }
}
