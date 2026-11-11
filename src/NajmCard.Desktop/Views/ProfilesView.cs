using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

/// <summary>Service-specific router package catalog with detailed read-only fields.
/// Creation and deletion of remote RouterOS profiles require separate tested contracts.</summary>
public sealed class ProfilesView : UserControl
{
    private sealed record PackageRow(string Name,string Speed,string Capacity,
        string Validity,string Uptime,string Shared,string Price,string Owner,
        RouterCardProfile? Hotspot=null,PppProfile? Ppp=null);
    private static string Bytes(long value)
    {
        if(value<=0)return "غير محدد";
        var units=new[]{"B","KB","MB","GB","TB"};
        double size=value;int i=0;
        while(size>=1024&&i<4){size/=1024;i++;}
        return size.ToString("0.##",CultureInfo.InvariantCulture)+" "+units[i];
    }
    public ProfilesView(RouterGateway gateway,Action<string,string>? selectPackage=null,
        VoucherStore? audit=null)
    {
        // The application header already labels this page; service tabs provide context.
        var tabs=new TabControl{Margin=new Thickness(0,4,0,9)};
        tabs.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        tabs.VerticalContentAlignment=VerticalAlignment.Stretch;
        var sources=new[]{
            ("hotspot","هوتسبوت","#8F5BB9"),
            ("userman","مدير المستخدمين","#168D92"),
            ("pppoe","برودباند PPPoE","#B38B57")
        };
        foreach(var (service,title,accent) in sources)
        {
            var panel=PageUi.Column();

            var search=PageUi.SearchInput();
            search.Margin=new Thickness(0,6,0,6);

            var grid=PageUi.Table(("اسم الباقة","Name"),("السرعة","Speed"),
                ("السعة","Capacity"),("الصلاحية","Validity"),
                ("المدة","Uptime"),("المشتركون","Shared"),("السعر","Price"));
            grid.IsReadOnly=true;grid.CanUserAddRows=false;grid.CanUserDeleteRows=false;
            grid.EnableRowVirtualization=true;grid.MinHeight=300;
            panel.Children.Add(grid);
            var details=PageUi.Column();
            details.Children.Add(PageUi.Title("بيانات الباقة على الراوتر"));
            var fields=new Dictionary<string,TextBox>(StringComparer.Ordinal);
            var detailsGrid=new UniformGrid{Columns=4};
            foreach(var (key,label) in new[]{
                ("Name","اسم الباقة"),("Speed","رفع / تنزيل"),
                ("Capacity","السعة / Transfer Limit"),("Validity","الصلاحية"),
                ("Uptime","الوقت / الجلسة"),("Shared","المشتركون"),
                ("Price","السعر"),("Owner","مدير المستخدمين / عنوان PPP")
            })
            {
                var box=PageUi.Input();
                box.IsReadOnly=!(service=="hotspot"&&
                    key is ("Name" or "Speed" or "Uptime" or "Shared"))
                    && !(service=="pppoe"&&key=="Speed");
                box.Margin=new Thickness(4,0,4,7);
                fields[key]=box;
                var cell=PageUi.Column();
                cell.Children.Add(PageUi.Label(label));
                cell.Children.Add(box);
                detailsGrid.Children.Add(cell);
            }
            details.Children.Add(detailsGrid);
            panel.Children.Add(PageUi.Surface(details,"#9B6D55"));
            panel.Children.Add(PageUi.Title("إجراءات الباقة"));
            var actions=new WrapPanel{Margin=new Thickness(0,2,0,10)};
            var refresh=PageUi.Button("تحديث من الراوتر");
            var clear=PageUi.Button("جديد",false);
            var save=PageUi.Button(
                service=="pppoe"?"حفظ سرعة الباقة المحددة":"إضافة / حفظ على الراوتر",false);
            var remove=PageUi.Button("حذف الباقة المحددة",false);
            var create=PageUi.Button("إنشاء كروت الباقة",false);
            create.IsEnabled=false;
            if(service=="userman")save.IsEnabled=remove.IsEnabled=false;
            if(service=="pppoe")remove.IsEnabled=false;
            actions.Children.Add(save);
            actions.Children.Add(clear);actions.Children.Add(create);
            actions.Children.Add(remove);
            panel.Children.Add(actions);
            panel.Children.Add(PageUi.Hint(service=="hotspot"
                ? "تعريف باقة Hotspot فقط: إضافة وحفظ وحذف بإذن صريح، والتحقق من المعرف والحالة قبل وبعد التعديل. لن تُمس سكربتات الباقة أو إعداداتها غير المطلوبة."
                : service=="pppoe"
                ? "تغيير حد سرعة باقة PPP المحددة فقط، بعد إعادة التحقق من بياناتها. إضافة وحذف PPP لم يُفعّلا بعد."
                : "مدير المستخدمين RouterOS 6: قراءة الباقات والمحددات فقط. ربط المحددات وإنشاء/حذف الباقات يحتاجان اختبارا مستقلا."));
            var limitGrid=PageUi.Table(("باقة مدير المستخدمين","ProfileName"),
                ("Transfer Limit موجب (بايت)","EffectiveTransferLimitBytes"),
                ("عدد الروابط","LinkedLimitationCount"),("المحددات","LimitationNames"));
            limitGrid.IsReadOnly=true;
            if(service=="userman")
            {
                panel.Children.Add(PageUi.Label("محددات الباقات وروابطها"));
                panel.Children.Add(limitGrid);
            }
            var status=PageUi.Status();
            status.MaxHeight=56;
            status.SetBinding(ToolTipProperty,new System.Windows.Data.Binding(nameof(TextBlock.Text)){Source=status});
            var toolbar=PageUi.Column();
            toolbar.Children.Add(PageUi.Title("الباقات: "+title));
            var filters=new WrapPanel();
            filters.Children.Add(PageUi.Label("البحث باسم الباقة"));
            filters.Children.Add(search);
            refresh.VerticalAlignment=VerticalAlignment.Center;
            filters.Children.Add(refresh);
            toolbar.Children.Add(filters);
            toolbar.Children.Add(status);
            var pinned=PageUi.Surface(toolbar,accent);
            var body=new DockPanel{LastChildFill=true};
            DockPanel.SetDock(pinned,Dock.Top);
            body.Children.Add(pinned);
            var pageScroll=new ScrollViewer
            {
                Content=PageUi.Surface(panel,accent),
                VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled
            };
            body.Children.Add(pageScroll);
            foreach(var table in new[]{grid,limitGrid})
            {
                table.PreviewMouseWheel+=(_,e)=>
                {
                    var inner=table.Template.FindName("DG_ScrollViewer",table) as ScrollViewer;
                    bool atEdge=inner is null || (e.Delta<0
                        ?inner.VerticalOffset>=inner.ScrollableHeight
                        :inner.VerticalOffset<=0);
                    if(!atEdge)return;
                    pageScroll.ScrollToVerticalOffset(pageScroll.VerticalOffset-e.Delta/3d);
                    e.Handled=true;
                };
            }
            var tab=new TabItem{Header=title,Content=body};
            tabs.Items.Add(tab);
            string Audit(string action,string profile,string outcome)
            {
                if(audit is null)return "";
                try
                {
                    audit.RecordPackageOperation(service,action,profile,outcome);
                    return "";
                }
                catch
                {
                    return " | تنبيه: تعذر حفظ العملية في سجل النشاط المحلي؛ لا تكرر أمر الراوتر.";
                }
            }
            IReadOnlyList<PackageRow> all=[];
            bool loaded=false;
            bool writing=false;
            bool busy=false;
            void Filter()
            {
                var term=search.Text.Trim();
                grid.ItemsSource=all.Where(x=>term.Length==0 ||
                    x.Name.Contains(term,StringComparison.OrdinalIgnoreCase)).ToArray();
            }
            search.TextChanged+=(_,_)=>Filter();
            void Details()
            {
                var item=grid.SelectedItem as PackageRow;
                fields["Name"].Text=item?.Name??"";
                fields["Speed"].Text=item?.Speed??"";
                fields["Capacity"].Text=item?.Capacity??"";
                fields["Validity"].Text=item?.Validity??"";
                fields["Uptime"].Text=item?.Uptime??(service=="hotspot"?"0s":"");
                fields["Shared"].Text=item?.Shared??(service=="hotspot"?"1":"");
                fields["Price"].Text=item?.Price??"";
                fields["Owner"].Text=item?.Owner??"";
                create.IsEnabled=item is not null&&selectPackage is not null;
                save.IsEnabled=loaded && !writing &&
                    (service=="hotspot" || service=="pppoe" && item?.Ppp is not null);
                remove.IsEnabled=loaded && !writing && service=="hotspot"
                    && item?.Hotspot is not null;
            }
            grid.SelectionChanged+=(_,_)=>Details();
            clear.Click+=(_,_)=>{grid.SelectedItem=null;Details();};
            create.Click+=(_,_)=>
            {
                if(grid.SelectedItem is not PackageRow)return;
                var item=(PackageRow)grid.SelectedItem;
                status.Text="جارٍ فتح الإنشاء للباقة «"+item.Name+"» في خدمة "+title;
                selectPackage?.Invoke(service,item.Name);
            };
            async Task SaveAsync()
            {
                if(writing)return;
                if(!loaded)
                {status.Text="يجب قراءة الباقات من الراوتر بنجاح قبل حفظ أي تغيير.";return;}
                var selected=grid.SelectedItem as PackageRow;
                if(service=="userman")
                {status.Text="تعديل باقات User Manager غير مفعل في هذه المرحلة.";return;}
                if(service=="pppoe" && selected?.Ppp is null)
                {status.Text="حدد باقة برودباند أولا لتغيير سرعتها.";return;}
                if(service=="hotspot" && selected is null &&
                    string.IsNullOrWhiteSpace(fields["Name"].Text))
                {status.Text="اكتب اسم باقة Hotspot الجديدة.";return;}
                if(!PageUi.Confirm((selected is null?"إضافة":"تعديل")+
                    " باقة "+title+" «"+fields["Name"].Text.Trim()+
                    "» على الراوتر المتصل؟"))return;
                writing=true;save.IsEnabled=remove.IsEnabled=false;
                try
                {
                    if(service=="hotspot")
                    {
                        var draft=new HotspotProfileDraft(
                            fields["Name"].Text.Trim(),fields["Speed"].Text.Trim(),
                            fields["Uptime"].Text.Trim(),fields["Shared"].Text.Trim());
                        var saved=await gateway.SaveHotspotProfileAsync(draft,selected?.Hotspot);
                        var auditNote=Audit(selected is null?"create":"edit",draft.Name,
                            saved.UncertainOutcome?"UNKNOWN":saved.Success?"SUCCESS":"ERROR");
                        if(saved.UncertainOutcome)loaded=false;
                        if(saved.Success)await ReadAsync();
                        status.Text=saved.Diagnostic+auditNote+
                            (saved.UncertainOutcome?" | لا تكرر الحفظ قبل مراجعة الراوتر.":"");
                    }
                    else if(selected?.Ppp is not null)
                    {
                        var changed=await gateway.SetPppProfileRateLimitAsync(
                            selected.Ppp,fields["Speed"].Text.Trim());
                        var auditNote=Audit("rate",selected.Ppp.Name,
                            changed.UncertainOutcome?"UNKNOWN":changed.Success?"SUCCESS":"ERROR");
                        if(changed.UncertainOutcome)loaded=false;
                        if(changed.Success)await ReadAsync();
                        status.Text=changed.Diagnostic+auditNote+
                            (changed.UncertainOutcome?" | تحقق من الراوتر قبل المحاولة مجددا.":"");
                    }
                }
                catch(Exception ex)
                {status.Text="توقف حفظ الباقة دون إرسال لاحق: "+ex.Message;}
                finally
                {
                    writing=false;Details();
                }
            }
            async Task DeleteAsync()
            {
                if(writing || !loaded || service!="hotspot")return;
                var selected=grid.SelectedItem as PackageRow;
                if(selected?.Hotspot is null)
                {status.Text="حدد باقة Hotspot من القائمة قبل الحذف.";return;}
                if(!PageUi.Confirm("حذف باقة Hotspot «"+selected.Name+
                    "» نهائيا من الراوتر؟ قد تؤثر على الكروت المرتبطة بها. لا يمكن التراجع عن الحذف."))return;
                writing=true;save.IsEnabled=remove.IsEnabled=false;
                try
                {
                    var deleted=await gateway.DeleteHotspotProfileAsync(selected.Hotspot);
                    var auditNote=Audit("remove",selected.Name,
                        deleted.UncertainOutcome?"UNKNOWN":deleted.Success?"SUCCESS":"ERROR");
                    if(deleted.UncertainOutcome)loaded=false;
                    if(deleted.Success)await ReadAsync();
                    status.Text=deleted.Diagnostic+auditNote+
                        (deleted.UncertainOutcome?" | لا تكرر الحذف قبل فحص الراوتر.":"");
                }
                catch(Exception ex){status.Text="أوقف حذف الباقة: "+ex.Message;}
                finally{writing=false;Details();}
            }
            save.Click+=async(_,_)=>await SaveAsync();
            remove.Click+=async(_,_)=>await DeleteAsync();
            async Task ReadAsync()
            {
                if(busy)return;
                busy=true;loaded=false;refresh.IsEnabled=save.IsEnabled=remove.IsEnabled=false;
                status.Text="جارٍ قراءة باقات "+title+" من RouterOS...";
                try
                {
                    string? limitError=null;
                    if(service=="hotspot")
                    {
                        var rows=await gateway.ReadProfilesAsync();
                        all=rows.Select(x=>new PackageRow(x.Name,x.RateLimit,
                            "غير محدد","—",x.SessionTimeout,x.SharedUsers,"—","—",
                            Hotspot:x)).ToArray();
                    }
                    else if(service=="pppoe")
                    {
                        var report=await gateway.ReadPppInventoryAsync();
                        all=report.Profiles.Select(x=>new PackageRow(x.Name,x.RateLimit,
                            "غير محدد","—","—","—","—",
                            "محلي "+x.LocalAddress+" | بعيد "+x.RemoteAddress,
                            Ppp:x)).ToArray();
                    }
                    else
                    {
                        var profiles=await gateway.ReadUserManagerV6ProfilesAsync();
                        PackageRow ToRow(UserManagerV6Profile profile,string capacity)=>new(
                            profile.Name,"—",capacity,profile.Validity,"—","—",
                            profile.Price,profile.Owner+" | بدء "+profile.StartsAt);
                        all=profiles.Select(x=>ToRow(x,"غير معروف")).ToArray();
                        Filter();
                        status.Text="الباقات: "+all.Count+" | جارٍ قراءة المحددات…";
                        limitGrid.ItemsSource=null;
                        if(profiles.Count>0)
                        {
                            try
                            {
                                var limits=await gateway.ReadUserManagerV6ProfileLimitSummaryAsync();
                                limitGrid.ItemsSource=limits;
                                var byId=limits.ToLookup(x=>x.ProfileId,StringComparer.OrdinalIgnoreCase);
                                var names=profiles.ToLookup(x=>x.Name,StringComparer.OrdinalIgnoreCase);
                                all=profiles.Select(x=>ToRow(x,
                                    names[x.Name].Count()==1 && byId[x.Id].FirstOrDefault() is { } data
                                        ?Bytes(data.EffectiveTransferLimitBytes):"غير معروف")).ToArray();
                            }
                            catch(Exception ex)
                            {
                                limitError=" | تعذرت قراءة المحددات: "+ex.Message;
                            }
                        }
                    }
                    loaded=true;
                    Filter();Details();
                    status.Text="عدد الباقات الحقيقية: "+all.Count+
                        " | آخر قراءة "+DateTime.Now.ToString("HH:mm:ss")+limitError;
                }
                catch(Exception ex)
                {
                    loaded=false;all=[];grid.ItemsSource=null;limitGrid.ItemsSource=null;
                    status.Text="تعذر قراءة الباقات بصورة موثوقة: "+ex.Message;
                }
                finally{busy=false;refresh.IsEnabled=true;Details();}
            }
            refresh.Click+=async(_,_)=>await ReadAsync();
            tabs.Loaded+=async(_,_) =>
            {
                if(ReferenceEquals(tabs.SelectedItem,tab) && !loaded)await ReadAsync();
            };
            tabs.SelectionChanged+=async(_,e)=>
            {
                if(ReferenceEquals(e.Source,tabs) && ReferenceEquals(tabs.SelectedItem,tab) && !loaded)
                    await ReadAsync();
            };
        }
        tabs.SelectedIndex=0;
        FlowDirection=FlowDirection.RightToLeft;
        Content=tabs;
    }
}
