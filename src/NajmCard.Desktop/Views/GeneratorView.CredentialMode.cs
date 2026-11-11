using NajmCard.Core.Models;

namespace NajmCard.Desktop.Views;

public partial class GeneratorView
{
    // A saved batch is immutable. Changing the selected credential mode must
    // never send or print the previous batch as though it used the new choice.
    private void InvalidatePendingBatchForCredentialChange()
    {
        if (_restoringPreset || _preparing || _sending || _batch.Count == 0) return;
        _batch = [];
        _table.ItemsSource = null;
        _sendButton.IsEnabled = false;
        BatchChanged?.Invoke();
        _status.Text = "تغير نوع بيانات الدخول. الدفعة القديمة لا تطابق الخيار الحالي؛ "
            + "أنشئ دفعة جديدة قبل الإرسال أو الطباعة.";
    }

    private void SetCredentialControlsBusy(bool busy)
    {
        _credentialMode.IsEnabled = !busy;
        _credentialsSame.IsEnabled = !busy;
        _credentialsDifferent.IsEnabled = !busy;
    }

    private static string DescribeSavedCredentialMode(IReadOnlyList<Voucher> batch)
    {
        if (batch.Count == 0) return "";
        if (batch.All(v => v.Password.Length == 0))
            return "اسم مستخدم فقط (دون كلمة مرور)";
        if (batch.All(v => string.Equals(v.Username, v.Password, StringComparison.Ordinal)))
            return "اسم مستخدم وكلمة مرور متطابقين";
        if (batch.All(v => v.Password.Length > 0 &&
            !string.Equals(v.Username, v.Password, StringComparison.Ordinal)))
            return "اسم مستخدم وكلمة مرور مختلفين";
        return "";
    }

    private void SelectMatchingOwner()
    {
        if (SelectedService != "userman") return;
        var name = _profile.SelectedItem?.ToString();
        var owner = _umProfiles.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.Ordinal))?.Owner;
        if (!string.IsNullOrWhiteSpace(owner) && _customer.Items.Contains(owner))
            _customer.SelectedItem = owner;
    }

}
