using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        _catalogStatus.Foreground=PageUi.Muted;
        _catalogStatus.Text="جار تحميل الباقات من الراوتر...";
        var failures=new List<string>();
        async Task<IReadOnlyList<T>> Read<T>(string label,Func<Task<IReadOnlyList<T>>> read)
        {
            try { return await read(); }
            catch(Exception ex)
            {
                failures.Add(label+": "+ex.Message);
                return [];
            }
        }

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
                var profiles=await Read("الباقات",()=>_gateway.ReadProfilesAsync());
                if(!IsCurrent())return;
                var rows=await Read("السيرفرات",()=>_gateway.ReadServersAsync());
                servers=servers.Concat(rows.Select(row=>row.Name))
                    .Where(name=>!string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                names=profiles.Select(row=>row.Name).ToArray();
            }
            else if(service=="userman")
            {
                umProfiles=await Read("الباقات",()=>_gateway.ReadUserManagerV6ProfilesAsync());
                if(!IsCurrent())return;
                var rows=await Read("حساب المدير",()=>_gateway.ReadUserManagerV6CustomersAsync());
                customers=rows.Select(row=>row.Login).Where(name=>!string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                names=umProfiles.Select(row=>row.Name).ToArray();
            }
            else
            {
                var profiles=await Read("الباقات",()=>_gateway.ReadPppProfilesAsync());
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
                _profile.SelectedItem=previousProfile??names.FirstOrDefault();
                _catalogStatus.Text=names.Length==0?"لا توجد باقات لهذه الخدمة.":"عدد الباقات: "+names.Length;
                if(service=="userman" && customers.Length==0)
                    _catalogStatus.Text+=" | لا توجد حسابات مدير متاحة.";
                ApplyPendingPackageSelection(service);
                if(previousOwner is null)SelectMatchingOwner();
                if(failures.Count>0)
                {
                    _catalogStatus.Foreground=new SolidColorBrush(Color.FromRgb(135, 23, 47));
                    _catalogStatus.Text="تعذر التحميل من الراوتر. "+string.Join(" | ",failures);
                }
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
                _catalogStatus.Foreground=new SolidColorBrush(Color.FromRgb(135, 23, 47));
                _catalogStatus.Text="تعذرت قراءة الباقات: "+ex.Message;
            }
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
