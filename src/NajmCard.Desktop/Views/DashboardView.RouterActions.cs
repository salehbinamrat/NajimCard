using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NajmCard.Core.Models;

namespace NajmCard.Desktop.Views;

public partial class DashboardView
{
    private bool _maintenanceBusy;

    private async void RestoreHotspot_Click(object sender,RoutedEventArgs e) =>
        await SetHotspotStateFromDashboardAsync(false);

    private async void DisableHotspot_Click(object sender,RoutedEventArgs e) =>
        await SetHotspotStateFromDashboardAsync(true);

    private async Task SetHotspotStateFromDashboardAsync(bool disabled)
    {
        if(_maintenanceBusy)return;
        _maintenanceBusy=true;
        try
        {
            var candidates=(await _gateway.ReadHotspotServerStatusAsync())
                .Where(x=>x.Disabled==!disabled).ToArray();
            if(candidates.Length==0)
            {
                MessageBox.Show(disabled?"لا توجد سيرفرات Hotspot مفعلة لتعطيلها.":
                    "لا توجد سيرفرات Hotspot معطلة لاستعادتها.",
                    "نجم كارد | حالة الهوتسبوت",MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            if(candidates.Length>100)
                throw new InvalidOperationException(
                    "عدد السيرفرات كبير؛ حدد السيرفر من أدوات الصيانة.");
            var selector=new ComboBox
            {
                ItemsSource=candidates,DisplayMemberPath="Name",SelectedIndex=0,
                Margin=new Thickness(0,9,0,9),MinHeight=36
            };
            var panel=new StackPanel{Margin=new Thickness(16)};
            panel.Children.Add(new TextBlock
            {
                Text=disabled?"حدد سيرفر Hotspot واحدا لتعطيله:":
                    "حدد سيرفر Hotspot واحدا لإعادة تشغيل خدمته:",
                Foreground=Brushes.White,FontSize=14,FontWeight=FontWeights.Bold,
                TextWrapping=TextWrapping.Wrap
            });
            panel.Children.Add(selector);
            var approve=PageUi.Button("متابعة",true);
            approve.Background=(Brush)new BrushConverter().ConvertFrom(
                disabled?"#9C2947":"#197A54")!;
            approve.Foreground=Brushes.White;
            var cancel=PageUi.Button("إلغاء",false);
            var buttons=new UniformGrid{Columns=2};
            buttons.Children.Add(approve);buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            var dialog=new Window
            {
                Title=disabled?"تعطيل Hotspot":"استعادة Hotspot",
                Content=panel,Width=430,Height=200,
                ResizeMode=ResizeMode.NoResize,
                WindowStartupLocation=WindowStartupLocation.CenterOwner,
                Owner=Window.GetWindow(this),
                FlowDirection=FlowDirection.RightToLeft
            };
            approve.Click+=(_,_)=>dialog.DialogResult=true;
            cancel.Click+=(_,_)=>dialog.DialogResult=false;
            if(dialog.ShowDialog()!=true ||
                selector.SelectedItem is not RouterHotspotServerStatus selected)return;
            var label=disabled?"تعطيل":"استعادة";
            if(!PageUi.Confirm(label+" سيرفر "+selected.Name+" على الواجهة "+
                selected.Interface+"؟ قد يتأثر المستخدمون المتصلون."))return;
            var result=await _gateway.ChangeHotspotServerStateAsync(selected,disabled);
            var report=result.Diagnostic+
                (result.UncertainOutcome?" لا تكرر العملية قبل فحص الراوتر.":"");
            MessageBox.Show(report,"نجم كارد | "+label,
                MessageBoxButton.OK,result.Success?MessageBoxImage.Information:
                    MessageBoxImage.Warning);
        }
        catch(Exception ex)
        {
            MessageBox.Show("تعذر تحديث Hotspot: "+ex.Message,
                "نجم كارد | الصيانة",MessageBoxButton.OK,MessageBoxImage.Error);
        }
        finally{_maintenanceBusy=false;}
    }

    private async void RebootRouter_Click(object sender,RoutedEventArgs e)
    {
        if(_maintenanceBusy)return;
        if(!PageUi.Confirm("إعادة تشغيل الراوتر ستقطع الإنترنت عن جميع المستخدمين "+
            "مؤقتا. هل تريد المتابعة؟"))return;
        if(MessageBox.Show("تأكيد نهائي: إرسال أمر إعادة تشغيل MikroTik الآن؟",
            "نجم كارد | إعادة التشغيل",MessageBoxButton.YesNo,
            MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        _maintenanceBusy=true;
        try
        {
            var outcome=await _gateway.RequestRebootAsync();
            MessageBox.Show(outcome.Message,"نجم كارد | إعادة تشغيل الراوتر",
                MessageBoxButton.OK,
                outcome.Accepted?MessageBoxImage.Information:MessageBoxImage.Warning);
        }
        catch(Exception ex)
        {
            MessageBox.Show("فشل طلب إعادة التشغيل: "+ex.Message+
                " | تحقق من الراوتر ولا تعاود الطلب آليا.",
                "نجم كارد | الصيانة",MessageBoxButton.OK,MessageBoxImage.Error);
        }
        finally{_maintenanceBusy=false;}
    }
}
