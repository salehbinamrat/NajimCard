using System.Windows;
using System.Windows.Controls;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

internal sealed class VoucherDeliveryHistoryWindow : Window
{
    public VoucherDeliveryHistoryWindow(VoucherStore store)
    {
        Title = "نجم كارد | سجل الدفعات";
        Width = 660;
        Height = 475;
        MinWidth = 460;
        MinHeight = 330;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;

        var root = new DockPanel();
        var header = PageUi.Column();
        header.Children.Add(PageUi.Title("سجل الدفعات"));
        var batches = new ComboBox
        {
            DisplayMemberPath = nameof(VoucherDeliveryBatch.DisplayName),
            MinHeight = 32,
            Style = (Style)Application.Current.FindResource("DarkComboBox")
        };
        header.Children.Add(batches);
        var status = PageUi.Hint("جار تحميل الدفعات المحفوظة…");
        status.Margin = new Thickness(0, 8, 0, 8);
        header.Children.Add(status);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var close = PageUi.Button("إغلاق", false);
        close.IsCancel = true;
        close.Click += (_, _) => Close();
        close.HorizontalAlignment = HorizontalAlignment.Left;
        close.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        var cards = PageUi.Table(("رقم الكرت", nameof(VoucherDeliveryEntry.Number)),
            ("حالة الإرسال", nameof(VoucherDeliveryEntry.Status)));
        cards.MinHeight = 0;
        cards.IsReadOnly = true;
        cards.ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader;
        root.Children.Add(cards);
        Content = PageUi.Surface(root);

        batches.SelectionChanged += async (_, _) =>
        {
            cards.ItemsSource = null;
            if (batches.SelectedItem is not VoucherDeliveryBatch batch) return;
            status.Text = "جار قراءة حالة الكروت…";
            try
            {
                var entries = await Task.Run(() => store.ReadDeliveryEntries(batch.Service, batch.BatchId));
                if (!IsVisible || !ReferenceEquals(batches.SelectedItem, batch)) return;
                cards.ItemsSource = entries;
                status.Text = $"العدد {batch.Total} · مؤكد {batch.Sent} · لم يرسل {batch.Pending}" +
                    $" · فشل {batch.Failed} · يحتاج مراجعة {batch.NeedsReview}";
            }
            catch (Exception ex)
            {
                if (IsVisible && ReferenceEquals(batches.SelectedItem, batch))
                    status.Text = "تعذر قراءة حالة الدفعة: " + ex.Message;
            }
        };

        Loaded += async (_, _) =>
        {
            try
            {
                var history = await Task.Run(store.ListDeliveryBatches);
                if (!IsVisible) return;
                batches.ItemsSource = history;
                if (history.Count == 0)
                    status.Text = "لا توجد دفعات محفوظة بعد.";
                else
                    batches.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                if (IsVisible) status.Text = "تعذر تحميل سجل الدفعات: " + ex.Message;
            }
        };
    }
}
