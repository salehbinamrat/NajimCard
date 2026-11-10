using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Views;

/// <summary>Hadramout-style four maintenance sections. Extends, never removes,
/// the established Hotspot FTP/template and read-only network tools.</summary>
public sealed class NetworkMaintenanceView : UserControl
{
    public NetworkMaintenanceView(RouterGateway gateway,string routerHost="")
    {
        var root=PageUi.Column();
        // The shared top bar provides the only primary page title.
        var tabs=new TabControl{Margin=new Thickness(0,5,0,8)};
        root.Children.Add(tabs);
        TabItem Add(string name,UIElement element)
        {
            var tab=new TabItem{Header=name,Content=new ScrollViewer
            {
                VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
                Content=element
            }};
            tabs.Items.Add(tab);return tab;
        }
        // Sessions: a single exact-ID read before any disconnect; never delete users.
        var sessionPanel=PageUi.Column();
        sessionPanel.Children.Add(PageUi.Title("الجلسات"));
        sessionPanel.Children.Add(PageUi.Hint(
            "قراءة جلسات Hotspot الفعلية؛ الفصل ينهي جلسة واحدة فقط، ولا يحذف الكرت."));
        var sessions=PageUi.Table(("المعرف","Id"),("المستخدم","User"),("IP","Address"),
            ("MAC","MacAddress"),("مدة الجلسة","Uptime"));
        sessions.IsReadOnly=true; sessions.EnableRowVirtualization=true;
        var sessionButtons=new WrapPanel();
        var readSessions=PageUi.Button("قراءة الجلسات الحالية");
        var disconnect=PageUi.Button("فصل الجلسة المحددة",false);
        sessionButtons.Children.Add(readSessions);sessionButtons.Children.Add(disconnect);
        var sessionStatus=PageUi.Status();
        sessionPanel.Children.Add(sessionButtons);
        sessionPanel.Children.Add(sessions);sessionPanel.Children.Add(sessionStatus);
        Add("الجلسات",PageUi.Surface(sessionPanel,"#568EA8"));
        bool readingSessions=false;
        async Task LoadSessions()
        {
            if(readingSessions)return;
            readingSessions=true;readSessions.IsEnabled=false;
            try
            {
                var items=await gateway.ReadSessionsAsync();
                sessions.ItemsSource=items;
                sessionStatus.Text="الجلسات الحقيقية: "+items.Count;
            }
            catch(Exception ex){sessions.ItemsSource=null;sessionStatus.Text="تعذرت القراءة: "+ex.Message;}
            finally{readingSessions=false;readSessions.IsEnabled=true;}
        }
        readSessions.Click+=async(_,_)=>await LoadSessions();
        disconnect.Click+=async(_,_)=>
        {
            if(sessions.SelectedItem is not HotspotSession selected)
            {sessionStatus.Text="حدد جلسة واحدة.";return;}
            if(!PageUi.Confirm("فصل جلسة "+selected.User+" على "+selected.Address+
                "؟ لن يتم حذف كرت المستخدم."))return;
            disconnect.IsEnabled=false;
            try
            {
                var result=await gateway.DisconnectHotspotSessionAsync(selected);
                sessionStatus.Text=result.Diagnostic;
                if(result.Success)await LoadSessions();
            }
            catch(Exception ex){sessionStatus.Text="تعذر الفصل: "+ex.Message;}
            finally{disconnect.IsEnabled=true;}
        };

        // Neighbor discovery uses count-only preflight; filter and copy are local.
        var devicePanel=PageUi.Column();
        devicePanel.Children.Add(PageUi.Title("الأجهزة المتصلة والمكتشفة"));
        devicePanel.Children.Add(PageUi.Hint(
            "الأجهزة المجاورة كما يعيدها MikroTik Neighbors؛ هذه ليست عملية مسح IP للشبكة."));
        var deviceSearch=PageUi.Input();
        var deviceGrid=PageUi.Table(("الواجهة","Interface"),("IP","PreferredAddress"),
            ("MAC","MacAddress"),("اسم الجهاز","Identity"),
            ("المنصة","Platform"),("الإصدار","Version"),("اللوحة","Board"));
        deviceGrid.IsReadOnly=true;deviceGrid.EnableRowVirtualization=true;
        var deviceButtons=new WrapPanel();
        var readDevices=PageUi.Button("تحديث الأجهزة");
        var copy=PageUi.Button("نسخ IP/MAC المحدد",false);
        deviceButtons.Children.Add(readDevices);deviceButtons.Children.Add(copy);
        devicePanel.Children.Add(deviceButtons);
        devicePanel.Children.Add(PageUi.Label("بحث بالاسم أو IP أو MAC"));
        devicePanel.Children.Add(deviceSearch);
        devicePanel.Children.Add(deviceGrid);
        var deviceStatus=PageUi.Status();devicePanel.Children.Add(deviceStatus);
        Add("الأجهزة المتصلة",PageUi.Surface(devicePanel,"#5D8E82"));
        IReadOnlyList<RouterNeighborDetail> neighbors=[];
        void FilterDevices()
        {
            var text=deviceSearch.Text.Trim();
            var result=neighbors.Where(x=>text.Length==0
                || x.Identity.Contains(text,StringComparison.OrdinalIgnoreCase)
                || x.PreferredAddress.Contains(text,StringComparison.OrdinalIgnoreCase)
                || x.MacAddress.Contains(text,StringComparison.OrdinalIgnoreCase)
                || x.Interface.Contains(text,StringComparison.OrdinalIgnoreCase)).ToArray();
            deviceGrid.ItemsSource=result;
            deviceStatus.Text="عرض "+result.Length+" من "+neighbors.Count+" جهاز";
        }
        deviceSearch.TextChanged+=(_,_)=>FilterDevices();
        readDevices.Click+=async(_,_)=>
        {
            readDevices.IsEnabled=false;
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try{neighbors=await gateway.ReadNearbyDevicesAsync(timeout.Token);FilterDevices();}
            catch(Exception ex)
            {neighbors=[];FilterDevices();deviceStatus.Text="تعذرت القراءة: "+ex.Message;}
            finally{readDevices.IsEnabled=true;}
        };
        copy.Click+=(_,_)=>
        {
            if(deviceGrid.SelectedItem is not RouterNeighborDetail d)return;
            Clipboard.SetText(d.PreferredAddress+" / "+d.MacAddress);
        };

        var dbPanel=PageUi.Column();
        dbPanel.Children.Add(PageUi.Title("قاعدة البيانات والراوتر"));
        dbPanel.Children.Add(PageUi.Hint(
            "قراءة حالة User Manager v6 من الراوتر. تحسين قاعدة RouterOS 7 وإعادة التشغيل "+
            "وإعادة ضبط البيانات ليست أوامر قراءة ولا تُنفذ تلقائيا."));
        var readDb=PageUi.Button("قراءة حالة قاعدة البيانات");
        var dbStatus=PageUi.Status();
        dbPanel.Children.Add(readDb);dbPanel.Children.Add(dbStatus);
        Add("قاعدة البيانات والراوتر",PageUi.Surface(dbPanel,"#B58B5B"));
        readDb.Click+=async(_,_)=>
        {
            readDb.IsEnabled=false;
            try
            {
                var value=await gateway.ReadUserManagerV6DatabaseStatusAsync();
                dbStatus.Text=value.Diagnostic;
            }
            catch(Exception ex){dbStatus.Text="تعذرت قراءة القاعدة: "+ex.Message;}
            finally{readDb.IsEnabled=true;}
        };

        var serversPanel=PageUi.Column();
        serversPanel.Children.Add(PageUi.Title("سيرفرات الهوتسبوت"));
        serversPanel.Children.Add(PageUi.Hint(
            "التفعيل والتعطيل لسيرفر واحد محدد بعد قراءة هوية وحالة السيرفر مباشرة من الراوتر."));
        var serverGrid=PageUi.Table(("اختيار","Id"),("اسم السيرفر","Name"),
            ("الواجهة","Interface"),("البروفايل","Profile"),("الحالة","StateLabel"));
        serverGrid.IsReadOnly=true;serverGrid.EnableRowVirtualization=true;
        var serverButtons=new WrapPanel();
        var readServers=PageUi.Button("تحديث السيرفرات");
        var enable=PageUi.Button("تفعيل المحدد",false);
        var disable=PageUi.Button("تعطيل المحدد",false);
        serverButtons.Children.Add(readServers);serverButtons.Children.Add(enable);
        serverButtons.Children.Add(disable);
        var serverStatus=PageUi.Status();
        serversPanel.Children.Add(serverButtons);
        serversPanel.Children.Add(serverGrid);
        serversPanel.Children.Add(serverStatus);
        Add("سيرفرات الهوتسبوت",PageUi.Surface(serversPanel,"#9D5F70"));
        bool serverBusy=false;
        async Task LoadServers()
        {
            if(serverBusy)return;
            serverBusy=true;readServers.IsEnabled=false;
            try
            {
                var list=await gateway.ReadHotspotServerStatusAsync();
                serverGrid.ItemsSource=list;
                serverStatus.Text="عدد السيرفرات من الراوتر: "+list.Count;
            }
            catch(Exception ex){serverGrid.ItemsSource=null;serverStatus.Text="فشلت القراءة: "+ex.Message;}
            finally{serverBusy=false;readServers.IsEnabled=true;}
        }
        readServers.Click+=async(_,_)=>await LoadServers();
        async Task ServerAction(bool disabled)
        {
            if(serverBusy || serverGrid.SelectedItem is not RouterHotspotServerStatus selected)
            {serverStatus.Text="حدد سيرفر Hotspot واحدا أولا.";return;}
            if(!PageUi.Confirm((disabled?"تعطيل":"تفعيل")+" السيرفر «"+
                selected.Name+"» على الراوتر؟ قد يؤثر ذلك على المشتركين."))return;
            serverBusy=true;enable.IsEnabled=disable.IsEnabled=false;
            try
            {
                var result=await gateway.ChangeHotspotServerStateAsync(selected,disabled);
                serverStatus.Text=result.Diagnostic;
                if(result.Success)
                {
                    var fresh=await gateway.ReadHotspotServerStatusAsync();
                    serverGrid.ItemsSource=fresh;
                }
            }
            catch(Exception ex){serverStatus.Text="توقف تعديل السيرفر: "+ex.Message;}
            finally{serverBusy=false;enable.IsEnabled=disable.IsEnabled=true;}
        }
        enable.Click+=async(_,_)=>await ServerAction(false);
        disable.Click+=async(_,_)=>await ServerAction(true);

        // Existing FTP, Hotspot template, full interface/inventory tools remain accessible.
        Add("أدوات إضافية",new ToolsView(gateway,routerHost));
        tabs.SelectedIndex=0;
        Content=PageUi.Wrap("التحكم بالشبكة والصيانة",
            "تبويبات مطابقة للمراجع مع الحفاظ على أدوات نجم كارد الموجودة.",
            PageUi.Surface(root,"#9C7859"));
    }
}
