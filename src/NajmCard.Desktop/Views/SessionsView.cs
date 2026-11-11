using System.Windows.Controls;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

public class SessionsView : UserControl
{
    public SessionsView(RouterGateway gateway)
    {
        var grid=PageUi.Table(
            ("الكرت","User"),("عنوان IP","Address"),
            ("MAC","MacAddress"),("المدة","Uptime"));
        grid.IsReadOnly=true;
        grid.CanUserAddRows=false;
        grid.CanUserDeleteRows=false;
        grid.EnableRowVirtualization=true;
        var status=PageUi.Status();
        var stack=PageUi.Column();
        var refresh=PageUi.Button("تحديث المتصلين");
        stack.Children.Add(refresh);
        stack.Children.Add(grid);
        stack.Children.Add(status);
        Content=PageUi.Wrap("المتصلون حالياً",
            "جلسات Hotspot الفعلية المتصلة بالراوتر، بلا بيانات تجريبية.",
            PageUi.Surface(stack,"#568E78"));

        bool busy=false;
        async Task Load()
        {
            if(busy)return; // Avoid overlapping RouterOS calls on repeated clicks.
            busy=true;
            refresh.IsEnabled=false;
            status.Text="جار تحميل الجلسات المتصلة من الراوتر...";
            try
            {
                var entries=await gateway.ReadSessionsAsync();
                if(!IsLoaded)return;
                grid.ItemsSource=entries;
                status.Text="جلسات متصلة: "+entries.Count;
            }
            catch(Exception ex)
            {
                if(!IsLoaded)return;
                // A failed refresh must not leave stale sessions marked as live.
                grid.ItemsSource=null;
                status.Text="تعذرت قراءة الجلسات المباشرة: "+ex.Message;
            }
            finally
            {
                busy=false;
                refresh.IsEnabled=true;
            }
        }
        refresh.Click+=async (_,_)=>await Load();
        Loaded+=async (_,_)=>await Load();
    }
}
