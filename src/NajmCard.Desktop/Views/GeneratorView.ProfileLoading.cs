using System.IO;
using System.Windows;
using System.Windows.Controls;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>RouterOS profile/server discovery is separate from the visual
/// editor to keep GUI changes isolated and reviewable.</summary>
public partial class GeneratorView
{
    private readonly SemaphoreSlim _profileReadGate=new(1,1);
    private readonly TextBlock _catalogStatus=PageUi.Status();
    private bool _applyingProfileCatalog;

    private void RequireRouteSelection()
    {
        if(!_profile.IsEnabled || _profile.SelectedIndex<0)
            throw new ArgumentException("اختر باقة محملة من الراوتر.");
        if(SelectedService=="userman" && (!_customer.IsEnabled || _customer.SelectedIndex<0))
            throw new ArgumentException("اختر حساب مدير User Manager.");
    }

    private async Task LoadProfilesAsync()
    {
        var requestVersion=++_profileRequestVersion;
        var service=SelectedService;
        var previousProfile=_profile.SelectedItem?.ToString();
        var previousServer=_server.SelectedItem?.ToString();
        var previousOwner=_customer.SelectedItem?.ToString();
        bool IsCurrent()=>requestVersion==_profileRequestVersion && SelectedService==service;
        _profile.IsEnabled=_server.IsEnabled=_customer.IsEnabled=false;
        bool hasProfiles=false, hasCustomers=false;
        _catalogStatus.Text="جار تحميل الباقات من الراوتر...";
        _applyingProfileCatalog=true;
        try
        {
            // Never leave an empty, unlabelled dropdown during slow API loads.
            _profile.ItemsSource=new[]{"جار تحميل الباقات…"};
            _profile.SelectedIndex=0;
            _customer.ItemsSource=new[]{"جار تحميل الحسابات…"};
            _customer.SelectedIndex=0;
        }
        finally{_applyingProfileCatalog=false;}

        // RouterOS commands must drain their replies. Superseded reads are
        // skipped between commands, without aborting an active API session.
        await _profileReadGate.WaitAsync();
        try
        {
            if(!IsCurrent())return;
            IReadOnlyList<UserManagerV6Profile> umProfiles=[];
            string[] servers=["all"];
            string[] customers=[];
            string[] names;
            if(service=="hotspot")
            {
                var profiles=await _gateway.ReadProfilesAsync();
                if(!IsCurrent())return;
                var rows=await _gateway.ReadServersAsync();
                servers=servers.Concat(rows.Select(row=>row.Name))
                    .Where(name=>!string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                names=profiles.Select(row=>row.Name).ToArray();
            }
            else if(service=="userman")
            {
                umProfiles=await _gateway.ReadUserManagerV6ProfilesAsync();
                if(!IsCurrent())return;
                names=umProfiles.Select(row=>row.Name).Where(name=>
                    !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                // A customer-list failure must not erase successfully read
                // package definitions. Owners on the real profiles are usable.
                try
                {
                    var rows=await _gateway.ReadUserManagerV6CustomersAsync();
                    customers=rows.Select(row=>row.Login)
                        .Concat(umProfiles.Select(row=>row.Owner))
                        .Where(name=>!string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                }
                catch(Exception ex) when(ex is IOException or InvalidDataException
                    or InvalidOperationException or ObjectDisposedException)
                {
                    customers=umProfiles.Select(row=>row.Owner)
                        .Where(name=>!string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if(IsCurrent())_catalogStatus.Text=
                        "الباقات جاهزة؛ تعذرت قراءة الحسابات. "+ex.Message;
                }
            }
            else
            {
                var profiles=await _gateway.ReadPppProfilesAsync();
                names=profiles.Select(row=>row.Name).ToArray();
            }
            if(!IsCurrent())return;

            _applyingProfileCatalog=true;
            try
            {
                hasProfiles=names.Length>0;
                hasCustomers=customers.Length>0;
                _umProfiles=umProfiles;
                _server.ItemsSource=servers;
                _server.SelectedItem=servers.Contains(previousServer) ? previousServer : "all";
                _customer.ItemsSource=customers.Length>0?customers:
                    new[]{"لا توجد حسابات للمدير · تحديث"};
                _customer.SelectedItem=customers.Contains(previousOwner)?
                    previousOwner:_customer.Items[0];
                _profile.ItemsSource=names.Length>0?names:
                    new[]{"لا توجد باقات · تحديث"};
                _profile.SelectedItem=names.Contains(previousProfile)?
                    previousProfile:_profile.Items[0];
                _catalogStatus.Text=names.Length==0?"لا توجد باقات لهذه الخدمة؛ اضغط تحديث الباقات.":
                    "تم تحميل "+names.Length+" باقة "+
                    (service=="userman"?" · حسابات المدير: "+customers.Length:"");
                ApplyPendingPackageSelection(service);
                if(previousOwner is null)SelectMatchingOwner();
            }
            finally{_applyingProfileCatalog=false;}

            if(!string.Equals(previousProfile,_profile.SelectedItem?.ToString(),StringComparison.Ordinal) ||
                !string.Equals(previousOwner,_customer.SelectedItem?.ToString(),StringComparison.Ordinal))
            {
                _batch=[];
                BatchChanged?.Invoke();
                RestorePackagePreset();
            }
            UpdateLiveCardPreview();
        }
        catch(Exception ex)
        {
            if(IsCurrent())
            {
                _applyingProfileCatalog=true;
                try
                {
                    _profile.ItemsSource=new[]{"تعذر الجلب · تحديث الباقات"};
                    _profile.SelectedIndex=0;
                    if(service=="userman")
                    {
                        _customer.ItemsSource=new[]{"تعذر جلب الحسابات · تحديث"};
                        _customer.SelectedIndex=0;
                    }
                    _catalogStatus.Text="تعذرت قراءة الباقات: "+ex.Message+
                        " | اضغط تحديث الباقات للمحاولة مجددا.";
                }
                finally{_applyingProfileCatalog=false;}
            }
        }
        finally
        {
            _profileReadGate.Release();
            if(IsCurrent())
            {
                _profile.IsEnabled=hasProfiles;
                _server.IsEnabled=service=="hotspot";
                _customer.IsEnabled=service=="userman" && hasCustomers;
            }
        }
    }
}
