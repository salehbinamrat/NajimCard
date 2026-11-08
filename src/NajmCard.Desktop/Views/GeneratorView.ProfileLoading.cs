using System.Windows;
using System.Windows.Controls;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>RouterOS profile/server discovery is separate from the visual
/// editor to keep GUI changes isolated and reviewable.</summary>
public partial class GeneratorView
{
    private async Task LoadProfilesAsync()
    {
        var requestVersion = ++_profileRequestVersion;
        var service = SelectedService;
        _profile.IsEnabled = false;
        _status.Text = "جارٍ تحميل الباقات من الراوتر...";
        try
        {
            if (service == "hotspot")
            {
                var profiles = await _gateway.ReadProfilesAsync();
                var servers = await _gateway.ReadServersAsync();
                if (SelectedService != service || requestVersion != _profileRequestVersion) return;
                _server.ItemsSource=new[]{"all"}.Concat(servers
                    .Select(x=>x.Name)
                    .Where(x=>!string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)).ToArray();
                _server.SelectedIndex=0;
                _umProfiles = [];
                _profile.ItemsSource = profiles.Select(x => x.Name).ToArray();
                if (_profile.Items.Count > 0) _profile.SelectedIndex = 0;
            }
            else if (service == "userman")
            {
                _server.ItemsSource=new[]{"all"};
                _server.SelectedIndex=0;
                var profiles = await _gateway.ReadUserManagerV6ProfilesAsync();
                var customers = await _gateway.ReadUserManagerV6CustomersAsync();
                if (SelectedService != service || requestVersion != _profileRequestVersion) return;
                _umProfiles = profiles;
                _customer.ItemsSource = customers.Select(x => x.Login)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                _profile.ItemsSource = profiles.Select(x => x.Name).ToArray();
                if (_profile.Items.Count > 0) _profile.SelectedIndex = 0;
                if (_customer.Items.Count > 0 && _customer.SelectedIndex < 0)
                    _customer.SelectedIndex = 0;
                SelectMatchingOwner();
            }
            else if (service == "pppoe")
            {
                _server.ItemsSource=new[]{"all"};
                _server.SelectedIndex=0;
                var inventory = await _gateway.ReadPppInventoryAsync();
                if (SelectedService != service || requestVersion != _profileRequestVersion) return;
                _umProfiles = [];
                _profile.ItemsSource = inventory.Profiles.Select(x => x.Name).ToArray();
                if (_profile.Items.Count > 0) _profile.SelectedIndex = 0;
            }
            _status.Text = "عدد باقات " +
                (service == "userman" ? "User Manager v6" :
                    service == "pppoe" ? "PPPoE" : "Hotspot") +
                ": " + _profile.Items.Count;
            ApplyPendingPackageSelection(service);
        }
        catch (Exception ex)
        {
            if (SelectedService == service && requestVersion == _profileRequestVersion)
            {
                _profile.ItemsSource = null;
                _customer.ItemsSource = null;
                _status.Text = "تعذرت قراءة باقات الخدمة: " + ex.Message;
            }
        }
        finally { if(requestVersion == _profileRequestVersion) _profile.IsEnabled = true; }
    }


}
