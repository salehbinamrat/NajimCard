using System.Windows.Controls.Primitives;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

public class ServersView : UserControl
{
    public ServersView(RouterGateway gateway)
    {
        var grid=PageUi.Table(
            ("السيرفر","Name"),("الواجهة","Interface"),
            ("البروفايل","Profile"),("الحالة","StateLabel"),
            ("معطل","Disabled"),("غير صالح","Invalid"),("قيد التشغيل","Running"));
        grid.IsReadOnly=true;
        grid.CanUserAddRows=false;
        grid.CanUserDeleteRows=false;
        var status=PageUi.Status();
        var stack=PageUi.Column();
        var refresh=PageUi.Button("تحديث حالة سيرفرات Hotspot");
        stack.Children.Add(PageUi.Hint(
            "تُقرأ حالة السيرفرات الحقيقية من /ip/hotspot/print وليست باقات المستخدمين. " +
            "هذه الصفحة للقراءة فقط، ولا تعطّل السيرفرات أو تغير إعدادات الراوتر."));
        stack.Children.Add(refresh);
        stack.Children.Add(grid);
        stack.Children.Add(status);
        Content=PageUi.Wrap("سيرفرات Hotspot",
            "الحالة الفعلية: السيرفر، الواجهة، البروفايل، التشغيل والتعطيل.",
            PageUi.Surface(stack));

        bool busy=false;
        async Task Load()
        {
            if(busy)return;
            busy=true;
            refresh.IsEnabled=false;
            status.Text="جارٍ قراءة الحالة الفعلية من RouterOS...";
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try
            {
                var items=await gateway.ReadHotspotServerStatusAsync(timeout.Token);
                grid.ItemsSource=items;
                status.Text="عدد السيرفرات: "+items.Count+
                    " | القراءة فقط | تحديث "+DateTime.Now.ToString("HH:mm:ss");
            }
            catch(Exception ex)
            {
                grid.ItemsSource=null;
                status.Text="فشلت قراءة حالة السيرفرات: "+ex.Message+
                    " | لم تُعرض بيانات قديمة على أنها حديثة.";
            }
            finally {busy=false;refresh.IsEnabled=true;}
        }
        refresh.Click+=async (_,_)=>await Load();
    }
}
