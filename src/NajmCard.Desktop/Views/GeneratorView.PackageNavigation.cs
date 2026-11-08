namespace NajmCard.Desktop.Views;

/// <summary>
/// Carries an explicit selected router package across WPF navigation and
/// asynchronous service-specific package reads. Never falls back to the
/// first profile when an explicitly requested profile is missing.
/// </summary>
public partial class GeneratorView
{
    private string? _pendingProfileName;
    private string? _pendingProfileService;

    public void SelectPackageOnLoad(string? service,string? profile)
    {
        if(string.IsNullOrWhiteSpace(profile)||string.IsNullOrWhiteSpace(service))
            return;
        if(service is not ("hotspot" or "userman" or "pppoe"))
            throw new ArgumentException("خدمة الباقة المحددة غير معروفة.",nameof(service));
        _pendingProfileName=profile;
        _pendingProfileService=service;
        SelectService(service);
    }

    private void ApplyPendingPackageSelection(string service)
    {
        if(_pendingProfileName is null||_pendingProfileService!=service)return;
        string requested=_pendingProfileName;
        _pendingProfileName=null;
        _pendingProfileService=null;
        var found=_profile.Items.Cast<object>()
            .Select(x=>x?.ToString()??"")
            .FirstOrDefault(x=>string.Equals(x,requested,StringComparison.Ordinal));
        if(found is null)
        {
            // A missing/deleted package must never silently switch to another one.
            _profile.SelectedIndex=-1;
            _status.Text="الباقة المحددة «"+requested+
                "» غير موجودة الآن على الراوتر. حدّث الباقات وتحقق قبل الإنشاء.";
            return;
        }
        _profile.SelectedItem=found;
        _status.Text="تم تحديد الباقة «"+found+"» من الراوتر.";
    }
}
