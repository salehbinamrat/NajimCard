using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

public partial class GeneratorView
{
    private async Task UploadAsync()
    {
        if (_sending) return;
        if (_batch.Count == 0)
        {
            _status.Text = "أنشئ دفعة جديدة أولاً.";
            return;
        }
        var service = _batch[0].Service;
        var customer = _batch[0].Customer;
        var description = service == "userman"
            ? "User Manager v6، حساب المدير " + customer
            : service == "pppoe" ? "PPPoE" : "Hotspot";
        if (!PageUi.Confirm("سيتم إرسال " + _batch.Count + " كرتاً إلى خدمة " +
            description + " على الراوتر. هل تريد المتابعة؟"))
            return;

        _sending = true;
        _sendButton.IsEnabled = false;
        _service.IsEnabled = false;
        try
        {
            var current = _batch.ToArray();
            var result = await new VoucherBatchDispatcher(_gateway, _store).SendAsync(current);
            if (result.UncertainOutcome)
            {
                _batch = _store.GetBatchVouchers(service, current[0].BatchId).ToArray();
                BatchChanged?.Invoke();
                _table.ItemsSource = _batch.Take(500).ToArray();
                _status.Text = "الاتصال أو رد الراوتر غير مؤكد. يُمنع إعادة إرسال كروت " +
                    "UNKNOWN/SENDING أو طباعتها قبل التحقق من الراوتر.";
            }
            else
            {
                var persisted = _store.GetBatchVouchers(service, current[0].BatchId).ToArray();
                _batch = persisted;
                BatchChanged?.Invoke();
                _table.ItemsSource = _batch.Take(500).ToArray();
                _status.Text = "تم إرسال " + result.Succeeded +
                    " كرتاً، وفشل " + result.Failed.Count +
                    ". جميع الحالات محفوظة محلياً.";
            }
        }
        catch (Exception ex) { _status.Text = "فشل الإرسال: " + ex.Message; }
        finally
        {
            _sending = false;
            _sendButton.IsEnabled = _batch.Count > 0;
            _service.IsEnabled = true;
        }
    }
}
