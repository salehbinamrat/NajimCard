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

    private async Task LoadProfilesAsync()
    {
        var requestVersion=++_profileRequestVersion;
        var service=SelectedService;
        var previousProfile=_profile.SelectedItem?.ToString();
        var previousServer=_server.SelectedItem?.ToString();
        var previousOwner=_customer.SelectedItem?.ToString();
        bool IsCurrent()=>requestVersion==_profileRequestVersion && SelectedService==service;
        _profile.IsEnabled=_server.IsEnabled=_customer.IsEnabled=false;
        _catalogStatus.Text="جار تحميل الباقات من الراوتر...";

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
                var rows=await _gateway.ReadUserManagerV6CustomersAsync();
                customers=rows.Select(row=>row.Login).Where(name=>!string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                names=umProfiles.Select(row=>row.Name).ToArray();
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
                _umProfiles=umProfiles;
                _server.ItemsSource=servers;
                _server.SelectedItem=previousServer??"all";
                _customer.ItemsSource=customers;
                _customer.SelectedItem=previousOwner??customers.FirstOrDefault();
                _profile.ItemsSource=names;
                _profile.SelectedItem=previousProfile is null?names.FirstOrDefault():previousProfile;
                _catalogStatus.Text=names.Length==0?"لا توجد باقات لهذه الخدمة.":"عدد الباقات: "+names.Length;
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
            if(IsCurrent())_catalogStatus.Text="تعذرت قراءة الباقات: "+ex.Message;
        }
        finally
        {
            _profileReadGate.Release();
            if(IsCurrent())
            {
                _profile.IsEnabled=true;
                _server.IsEnabled=service=="hotspot";
                _customer.IsEnabled=service=="userman";
            }
        }
    }
}
