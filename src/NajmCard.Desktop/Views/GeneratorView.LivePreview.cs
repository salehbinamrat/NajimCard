using System.Globalization;
using System.Windows.Controls;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

public partial class GeneratorView
{
    // Preview and issuing share one source of generation options. Preview has
    // a local-only profile name before the router's real profiles are loaded.
    private VoucherGenerationOptions ReadGenerationOptions(bool forPreview)
    {
        if(!int.TryParse(_count.Text,NumberStyles.Integer,
               CultureInfo.InvariantCulture,out var count) ||
           !int.TryParse(_length.Text,NumberStyles.Integer,
               CultureInfo.InvariantCulture,out var length))
            throw new ArgumentException("حدد العدد وطول اسم المستخدم بشكل صحيح.");

        bool same=_credentialMode.SelectedIndex==0 || _samePassword.IsChecked==true;
        int passwordLength=0;
        if(!same && !int.TryParse(_passwordLength.Text,NumberStyles.Integer,
               CultureInfo.InvariantCulture,out passwordLength))
            throw new ArgumentException("أدخل طول كلمة المرور بشكل صحيح.");

        var profile=_profile.SelectedItem?.ToString()??"";
        if(forPreview && string.IsNullOrWhiteSpace(profile))
            profile="معاينة محلية فقط";
        return new VoucherGenerationOptions(
            count,_prefix.Text.Trim(),length,_usernameCharacters.Text.Trim(),
            same,profile,PasswordLength:passwordLength,
            PasswordCharacters:same?null:_passwordCharacters.Text.Trim(),
            LengthIncludesPrefix:_lengthWithPrefix.IsChecked==true);
    }

    // Like Hadramout's scheduled sample_card() repaint: no SaveBatch,
    // RouterGateway, PreparedBatch or UploadAsync is reachable from here.
    // The visible text is a non-issuable sample, not a stored voucher number.
    private bool ShowPreviewPassword => _credentialMode.SelectedIndex!=0 &&
        _designShowPassword.IsChecked==true;

    private void UpdateLiveCardPreview()
    {
        if(_restoringPreset || _preparing || _sending)return;
        try
        {
            var sample=VoucherGenerator.PreviewSample(ReadGenerationOptions(forPreview:true));
            _cardPreview.Text=sample.Username+
                (ShowPreviewPassword?"\n"+sample.Password:"");
            _cardPreview.ToolTip="معاينة متغيرة حسب الإعدادات؛ غير محفوظة وغير مرسلة.";
        }
        catch(ArgumentException ex)
        {
            _cardPreview.Text="راجع إعدادات رقم الكرت";
            _cardPreview.ToolTip="المعاينة: "+ex.Message;
        }
    }

    private void SubscribeLiveCardPreview()
    {
        foreach(TextBox field in new[]{
            _count,_prefix,_length,_passwordLength,
            _usernameCharacters,_passwordCharacters})
            field.TextChanged+=(_,_)=>UpdateLiveCardPreview();
        foreach(var combo in new[]{
            _credentialMode,_usernameAlphabetType,_passwordAlphabetType,_profile})
            combo.SelectionChanged+=(_,_)=>UpdateLiveCardPreview();
        _credentialsSame.Checked+=(_,_)=>UpdateLiveCardPreview();
        _credentialsDifferent.Checked+=(_,_)=>UpdateLiveCardPreview();
        _designShowPassword.Checked+=(_,_)=>UpdateLiveCardPreview();
        _designShowPassword.Unchecked+=(_,_)=>UpdateLiveCardPreview();
        _lengthWithPrefix.Checked+=(_,_)=>UpdateLiveCardPreview();
        _lengthWithPrefix.Unchecked+=(_,_)=>UpdateLiveCardPreview();
        UpdateLiveCardPreview();
    }
}
