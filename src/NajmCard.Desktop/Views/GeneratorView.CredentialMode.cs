using NajmCard.Core.Models;

namespace NajmCard.Desktop.Views;

public partial class GeneratorView
{
    // All progress notifications originate from the actual issue/save stages.
    // Only the UI workspace subscribes; no status text includes card passwords.
    public event Action<string>? OperationProgressChanged;
    private void ReportOperationProgress(string stage) =>
        OperationProgressChanged?.Invoke(stage);

    // A saved batch is immutable. Changing the selected credential mode must
    // never send or print the previous batch as though it used the new choice.
    private void InvalidatePendingBatchForCredentialChange()
    {
        if (_restoringPreset || _preparing || _sending || _batch.Count == 0) return;
        _batch = [];
        _table.ItemsSource = null;
        _sendButton.IsEnabled = false;
        BatchChanged?.Invoke();
        _status.Text = "تغيرت إعدادات إنشاء الكروت. الدفعة القديمة لا تطابق الخيارات الحالية؛ "
            + "أنشئ دفعة جديدة قبل الإرسال أو الطباعة.";
    }

    private void SetCredentialControlsBusy(bool busy)
    {
        _credentialMode.IsEnabled = !busy;
        _credentialsSame.IsEnabled = !busy;
        _credentialsDifferent.IsEnabled = !busy;
        // Freeze every input affecting the issued credentials while generation
        // and RouterOS delivery run asynchronously.
        foreach (var input in new[]
        {
            _count, _pagesEstimate, _prefix, _length, _usernameCharacters,
            _passwordLength, _passwordCharacters
        }) input.IsEnabled = !busy;
        foreach (var selector in new[]
        {
            _usernameAlphabetType, _passwordAlphabetType, _profile,
            _customer, _server
        }) selector.IsEnabled = !busy;
        _lengthWithPrefix.IsEnabled = !busy;
    }

    // A preview/edit to the generation form cannot silently reuse a saved
    // batch created from earlier settings. Service/profile/customer changes
    // are already handled by their own navigation handlers.
    private void SubscribeGenerationInputChanges()
    {
        foreach (var input in new[]
        {
            _count, _pagesEstimate, _prefix, _length, _usernameCharacters,
            _passwordLength, _passwordCharacters
        })
            input.TextChanged += (_, _) => InvalidatePendingBatchForCredentialChange();

        foreach (var selector in new[]
        {
            _usernameAlphabetType, _passwordAlphabetType
        })
            selector.SelectionChanged += (_, _) =>
                InvalidatePendingBatchForCredentialChange();

        _lengthWithPrefix.Checked += (_, _) => InvalidatePendingBatchForCredentialChange();
        _lengthWithPrefix.Unchecked += (_, _) => InvalidatePendingBatchForCredentialChange();
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
