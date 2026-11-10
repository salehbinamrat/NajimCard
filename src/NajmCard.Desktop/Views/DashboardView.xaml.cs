using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

public partial class DashboardView : UserControl
{
    private readonly RouterGateway _gateway;
    private readonly Action<string> _navigate;
    private readonly VoucherStore _store;
    private UserManagerV6Inventory? _lastLifecycle;
    private bool _lifecycleReading;
    private bool _refreshing;
    private IReadOnlyList<VoucherActivityRow>? _displayedActivities;
    private CancellationTokenSource? _refreshToken;
    private CancellationTokenSource? _initialLifecycleToken;
    private CancellationTokenSource? _lifecycleToken;
    private readonly DispatcherTimer _telemetryTimer=new() {Interval=TimeSpan.FromSeconds(1)};
    private bool _initialSnapshotLoaded;
    private bool _initialLifecycleAttempted;
    private bool _liveTelemetryRunning;
    private CancellationTokenSource? _telemetryToken;

    public DashboardView(RouterGateway gateway, VoucherStore store, Action<string> navigate, string routerAddress = "")
    {
        _gateway = gateway;
        _store = store;
        _navigate = navigate;
        InitializeComponent();
        RouterEndpointInfo.Text = "عنوان الراوتر: " + (string.IsNullOrWhiteSpace(routerAddress) ? "غير محدد" : routerAddress);
        // One heavy inventory per router login; retaining the view preserves the snapshot
        // across sidebar navigation. Only online and router telemetry repeat.
        _telemetryTimer.Tick+=async (_,_)=>await RefreshLiveTelemetryAsync();
        Loaded += (_, _) =>
        {
            _telemetryTimer.Start();
            if(!_initialSnapshotLoaded)
            {
                _initialSnapshotLoaded=true;
                _=RefreshAsync();
            }
            if(!_initialLifecycleAttempted)
                _=ScheduleInitialLifecycleAsync();
        };
        Unloaded += (_, _) =>
        {
            _telemetryTimer.Stop();
            _telemetryToken?.Cancel();
            _refreshToken?.Cancel();
            _initialLifecycleToken?.Cancel();
            _lifecycleToken?.Cancel();
        };
    }

    private void NewCards_Click(object sender, RoutedEventArgs e) => _navigate("generator");
    private void Maintenance_Click(object sender, RoutedEventArgs e)
    {
        // Maintenance mutations require explicit selection/confirmation on
        // their owning page: never disable services from a dashboard shortcut.
        _navigate("tools");
    }
    private void ManageCards_Click(object sender, RoutedEventArgs e) => _navigate("cards");
    private void Reports_Click(object sender, RoutedEventArgs e) => _navigate("reports");
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    // RouterOS count-only connected clients and resource telemetry at a 1-second
    // display cadence. Never run overlapping requests or download UM user lists.
    private async Task RefreshLiveTelemetryAsync()
    {
        if(!IsLoaded || _liveTelemetryRunning || _refreshing)return;
        _liveTelemetryRunning=true;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));
        _telemetryToken=timeout;
        try
        {
            var resource=await _gateway.ReadResourceAsync(timeout.Token);
            if(!IsLoaded || timeout.IsCancellationRequested)return;
            Cpu.Text=resource.CpuLoad+"%";
            CpuProgress.Value=double.TryParse(resource.CpuLoad,NumberStyles.Float,
                CultureInfo.InvariantCulture,out var cpu)?Math.Clamp(cpu,0,100):0;
            Uptime.Text=resource.Uptime; // RouterOS reports uptime with second resolution.
            MemoryFree.Text=FormatBytes(resource.FreeMemory);
            if(long.TryParse(resource.TotalMemory,out var total) &&
               long.TryParse(resource.FreeMemory,out var free) && total>0)
            {
                var usage=Math.Clamp((total-free)*100d/total,0,100);
                MemoryProgress.Value=usage;
                MemoryUsePercent.Text=usage.ToString("F0",CultureInfo.InvariantCulture)+"%";
            }
            var live=await _gateway.ReadDashboardOnlineOnlyAsync(timeout.Token);
            if(!IsLoaded || timeout.IsCancellationRequested)return;
            Online.Text=live.OnlineTotal?.ToString("N0",CultureInfo.InvariantCulture)??"—";
            OnlineBreakdown.Text="Hotspot "+(live.HotspotOnline?.ToString("N0",CultureInfo.InvariantCulture)??"—")+
                " + PPPoE "+(live.PppOnline?.ToString("N0",CultureInfo.InvariantCulture)??"—")+
                (live.IsPartial?" | قراءة جزئية":"");
            RefreshHistory.Text=DateTime.Now.ToString("HH:mm:ss")+" · المتصلون وأداء الراوتر (مباشر)";
        }
        catch(OperationCanceledException) { /* Next timer tick retries; no zero substitutes. */ }
        catch(Exception ex) {if(IsLoaded)ErrorStatus.Text="تعذر تحديث الاتصال المباشر: "+ex.Message;}
        finally
        {
            if(ReferenceEquals(_telemetryToken,timeout))_telemetryToken=null;
            _liveTelemetryRunning=false;
        }
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || !IsLoaded) return;
        _refreshing=true;
        RefreshButton.IsEnabled=false;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(100));
        _refreshToken=timeout;
        ErrorStatus.Text="جارٍ تحديث العدادات المباشرة من الراوتر...";
        var refreshActivity=RefreshActivityAsync(timeout.Token);
        try
        {
            // Do not load tens of thousands of User Manager vouchers every time
            // the dashboard opens. These are 3 count-only requests; lifecycle
            // is a separate explicit heavyweight read with a 5-minute cache.
            var resource=await _gateway.ReadResourceAsync(timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            if(!IsLoaded)return;
            RouterName.Text=string.IsNullOrWhiteSpace(resource.Identity)?"MikroTik":resource.Identity;
            RouterVersion.Text=resource.Version;
            RouterBoard.Text=resource.Board;
            Uptime.Text=resource.Uptime;
            Cpu.Text=resource.CpuLoad+"%";
            CpuProgress.Value=double.TryParse(resource.CpuLoad,NumberStyles.Float,
                CultureInfo.InvariantCulture,out double cpu)?Math.Clamp(cpu,0,100):0;
            MemoryFree.Text=FormatBytes(resource.FreeMemory);
            if(long.TryParse(resource.TotalMemory,out long total)&&
               long.TryParse(resource.FreeMemory,out long free)&&total>0)
            {
                var use=Math.Clamp((total-free)*100d/total,0,100);
                MemoryProgress.Value=use;
                MemoryUsePercent.Text=use.ToString("F0",CultureInfo.InvariantCulture)+"%";
            }
            else {MemoryProgress.Value=0;MemoryUsePercent.Text="—";}
            var live=await _gateway.ReadDashboardLiveCountsAsync(timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            if(!IsLoaded)return;
            AllUsers.Text=live.UserManagerTotal?.ToString("N0",CultureInfo.InvariantCulture)??"—";
            Online.Text=live.OnlineTotal?.ToString("N0",CultureInfo.InvariantCulture)??"—";
            OnlineBreakdown.Text="Hotspot "+(live.HotspotOnline?.ToString("N0",CultureInfo.InvariantCulture)??"—")+
                " + PPPoE "+(live.PppOnline?.ToString("N0",CultureInfo.InvariantCulture)??"—")+
                (live.IsPartial?" | قراءة جزئية":"");
            var stamp=DateTime.Now.ToString("HH:mm:ss");
            RefreshHistory.Text=stamp+" · تحديث مباشر للعدادات، دون جلب قوائم مستخدمين كاملة";
            ErrorStatus.Text=(live.IsPartial?"توجد عدادات متصلين غير متاحة. ":"")+
                (live.UserManagerTotal is null?"إجمالي User Manager غير متاح؛ لا يمكن اعتماد الصفر. ":"")+
                "حالة النشطة والمنتهية تُقرأ مستقلة بحسب استخدام وحدود User Manager v6.";
        }
        catch(OperationCanceledException)
        {
            if(IsLoaded)ErrorStatus.Text="أُلغيت القراءة أو انتهت المهلة.";
        }
        catch(Exception ex)
        {
            if(IsLoaded)ErrorStatus.Text="تعذر التحديث؛ لم تُستبدل الأعداد ببيانات مزيفة: "+ex.Message;
        }
        finally
        {
            await refreshActivity;
            if(ReferenceEquals(_refreshToken,timeout))_refreshToken=null;
            _refreshing=false;
            RefreshButton.IsEnabled=true;
        }
    }

    private async Task RefreshActivityAsync(CancellationToken cancellationToken)
    {
        try
        {
            var activities=await Task.Run(()=>_store.ReadRecentActivities(6),cancellationToken);
            if(!IsLoaded || cancellationToken.IsCancellationRequested)return;
            if(_displayedActivities is null || !_displayedActivities.SequenceEqual(activities))
            {
                RenderActivity(activities);
                _displayedActivities=activities;
            }
            ActivityRows.ClearValue(ToolTipProperty);
        }
        catch(OperationCanceledException) { }
        catch(Exception ex)
        {
            if(IsLoaded)ActivityRows.ToolTip="تعذر تحديث سجل العمليات: "+ex.Message;
        }
    }

    private async void Lifecycle_Click(object sender,RoutedEventArgs e)
        => await RefreshLifecycleAsync();

    private async Task ScheduleInitialLifecycleAsync()
    {
        if(_initialLifecycleToken is not null)return;
        using var pending=new CancellationTokenSource();
        _initialLifecycleToken=pending;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5),pending.Token);
            if(IsLoaded && !pending.IsCancellationRequested)
            {
                _initialLifecycleAttempted=true;
                await RefreshLifecycleAsync();
            }
        }
        catch(OperationCanceledException) { }
        finally
        {
            if(ReferenceEquals(_initialLifecycleToken,pending))
                _initialLifecycleToken=null;
        }
    }

    private async Task RefreshLifecycleAsync()
    {
        if(_lifecycleReading || !IsLoaded)return;
        _lifecycleReading=true;
        LifecycleButton.IsEnabled=false;
        LifecycleStatus.Text="جارٍ قراءة مستخدمي User Manager v6 وحالتهم وحدود Transfer Limit؛ قد تستغرق القوائم الكبيرة وقتا...";
        using var cts=new CancellationTokenSource(TimeSpan.FromMinutes(11));
        _lifecycleToken=cts;
        try
        {
            var state=await _gateway.ReadCachedUserManagerV6InventoryAsync(false,cts.Token);
            if(!IsLoaded)return;
            if(!state.IsComplete)
            {
                _lastLifecycle=null;
                Enabled.Text="—";
                Expired.Text="—";
                ActiveLegend.Text=ExpiredLegend.Text=DisabledLegend.Text="—";
                ActivePercent.Text=ExpiredPercent.Text=ValidPercent.Text="—";

                ProfileBars.Children.Clear();
                LifecycleStatus.Text="القراءة غير مكتملة: "+state.Diagnostic+
                    " | لم تعرض أعدادا صفرية مضللة.";
                return;
            }
            _lastLifecycle=state;
            Enabled.Text=state.Active.ToString("N0",CultureInfo.InvariantCulture);
            Expired.Text=state.Expired.ToString("N0",CultureInfo.InvariantCulture);
            ActiveLegend.Text=state.Active.ToString("N0",CultureInfo.InvariantCulture);
            ExpiredLegend.Text=state.Expired.ToString("N0",CultureInfo.InvariantCulture);
            DisabledLegend.Text=state.Valid.ToString("N0",CultureInfo.InvariantCulture);

            static string Pct(int value,int total)=>
                total<=0?"0%":((double)value*100/total).ToString("0.0",
                    CultureInfo.InvariantCulture)+"%";
            ActivePercent.Text=Pct(state.Active,state.Total);
            ExpiredPercent.Text=Pct(state.Expired,state.Total);
            ValidPercent.Text=Pct(state.Valid,state.Total);

            var profiles=await Task.Run(()=>state.Users
                .Where(u=>!string.IsNullOrWhiteSpace(u.Profile))
                .GroupBy(u=>u.Profile)
                .Select(g=>new ProfileCount(g.Key,g.Count()))
                .OrderByDescending(g=>g.Count).ThenBy(g=>g.Name,StringComparer.OrdinalIgnoreCase)
                .ToArray());
            if(IsLoaded)RenderProfiles(profiles);
            LifecycleStatus.Text="آخر قراءة موثوقة: "+DateTime.Now.ToString("HH:mm:ss")+
                " | نشطة "+state.Active+" | منتهية "+state.Expired+
                " | صالحة غير مستخدمة "+state.Valid+
                (state.Unclassified>0?" | غير مصنفة "+state.Unclassified:"")+
                " | الكروت المنتهية: استنفاد Transfer Limit الموجب فقط.";
        }
        catch(OperationCanceledException)
        {
            if(IsLoaded)LifecycleStatus.Text="أُلغيت قراءة حالة الكروت أو انتهت المهلة؛ لا تعتمد أعدادا جديدة.";
        }
        catch(Exception ex)
        {
            if(IsLoaded)LifecycleStatus.Text="تعذرت قراءة حالة User Manager: "+ex.Message;
        }
        finally
        {
            if(ReferenceEquals(_lifecycleToken,cts))_lifecycleToken=null;
            _lifecycleReading=false;
            LifecycleButton.IsEnabled=true;
        }
    }

    private void OpenLifecycleDetails_Click(object sender,RoutedEventArgs e)
    {
        var kind=(sender as Button)?.Tag as string;
        if(kind is not ("active" or "expired"))return;
        if(_lastLifecycle is not {IsComplete:true} state)
        {
            LifecycleStatus.Text="اقرأ حالة الكروت من User Manager v6 أولا.";
            return;
        }
        UserManagerV6Voucher[] matches=state.Users.Where(x=>x.Status==kind).ToArray();
        var dialog=new Window
        {
            Title=kind=="active"?"الكروت النشطة":"الكروت المنتهية",
            Width=910,Height=650,MinWidth=680,MinHeight=460,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,
            Owner=Window.GetWindow(this),FlowDirection=FlowDirection.RightToLeft
        };
        var content=new DockPanel{Margin=new Thickness(12)};
        var header=new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text="نتيجة User Manager v6 موثوقة · الكروت "+matches.Length+
                " · البحث محلي دون الاتصال بالراوتر",
            Margin=new Thickness(0,0,0,7),TextWrapping=TextWrapping.Wrap
        });
        var filter=new TextBox{Margin=new Thickness(0,0,0,7)};
        header.Children.Add(filter);
        DockPanel.SetDock(header,Dock.Top);
        content.Children.Add(header);
        var grid=new DataGrid
        {
            AutoGenerateColumns=false,IsReadOnly=true,
            CanUserAddRows=false,CanUserDeleteRows=false,
            EnableRowVirtualization=true,EnableColumnVirtualization=true
        };
        void Column(string label,string property)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header=label,
                Binding=new System.Windows.Data.Binding(property)
                {
                    ConverterCulture=CultureInfo.GetCultureInfo("en-US"),
                    Converter=new AsciiDigitDisplayConverter()
                }
            });
        }
        Column("اسم المستخدم","Username");
        Column("الباقة","Profile");
        Column("الحالة","Status");
        Column("سبب التصنيف","Reason");
        Column("المستهلك (بايت)","ConsumedBytes");
        Column("تنزيل (بايت)","DownloadBytes");
        Column("رفع (بايت)","UploadBytes");
        Column("المتبقي (بايت)","RemainingLabel");
        Column("Transfer Limit","TransferLimit");
        Column("وقت الاستخدام","UptimeUsed");
        Column("آخر ظهور","LastSeen");
        Column("جلسات","ActiveSessions");
        int currentPage=0;
        const int pageSize=250;
        var footer=new DockPanel{Margin=new Thickness(0,7,0,2)};
        var previous=PageUi.Button("السابق",false);
        var next=PageUi.Button("التالي",false);
        var pageLabel=PageUi.Status();
        footer.Children.Add(previous);
        footer.Children.Add(next);
        footer.Children.Add(pageLabel);
        var deleteExpired=PageUi.Button("حذف الكروت المنتهية",false);
        deleteExpired.MinWidth=180;
        deleteExpired.Foreground=Brushes.White;
        deleteExpired.Background=new SolidColorBrush(Color.FromRgb(145,43,66));
        deleteExpired.Visibility=kind=="expired"?Visibility.Visible:Visibility.Collapsed;
        footer.Children.Add(deleteExpired);
        DockPanel.SetDock(footer,Dock.Bottom);
        content.Children.Add(footer);
        void UpdatePage()
        {
            var term=filter.Text.Trim();
            var found=matches.Where(x=>term.Length==0 ||
                x.Username.Contains(term,StringComparison.OrdinalIgnoreCase) ||
                x.Profile.Contains(term,StringComparison.OrdinalIgnoreCase)).ToArray();
            int pages=Math.Max(1,(found.Length+pageSize-1)/pageSize);
            currentPage=Math.Clamp(currentPage,0,pages-1);
            grid.ItemsSource=found.Skip(currentPage*pageSize).Take(pageSize).ToArray();
            pageLabel.Text="صفحة "+(currentPage+1)+" من "+pages+" | "+found.Length+" مطابق";
            previous.IsEnabled=currentPage>0;
            next.IsEnabled=currentPage+1<pages;
        }
        previous.Click+=(_,_)=>{currentPage--;UpdatePage();};
        next.Click+=(_,_)=>{currentPage++;UpdatePage();};
        filter.TextChanged+=(_,_)=>{currentPage=0;UpdatePage();};
        deleteExpired.Click+=async (_,_) =>
        {
            var eligible=matches.Where(x=>x.Status=="expired"
                && x.ExpirySource=="transfer-limit" && x.Started
                && !x.Disabled && !x.Incomplete && x.TransferLimit>0
                && x.ConsumedBytes>=x.TransferLimit).ToArray();
            if(eligible.Length==0)
            {
                pageLabel.Text="لا توجد كروت منتهية مؤكدة بحسب Transfer Limit.";
                return;
            }
            if(!PageUi.Confirm("حذف "+eligible.Length+
                " كرتا مستنفدا Transfer Limit بشكل نهائي؟"+
                " تُفحص كل الحالات مرة واحدة من الراوتر قبل الحذف،"+
                " وتُتخطى الكروت التي تجددت أو تغيرت باقتها. لا يمكن التراجع."))
                return;
            deleteExpired.IsEnabled=false;
            filter.IsEnabled=false;
            previous.IsEnabled=next.IsEnabled=false;
            var progress=new Progress<ExpiredTransferDeletionProgress>(p=>
            {
                pageLabel.Text="حذف آمن: "+p.Processed+" / "+p.Total+
                    " | نجح "+p.Deleted+" | تخطّي "+p.Skipped+
                    " | فشل "+p.Failed;
            });
            try
            {
                var result=await _gateway.DeleteExpiredUserManagerV6Async(
                    eligible,progress:progress);
                var auditWarning="";
                try{_store.RecordExpiredCleanup("userman",result);}
                catch {auditWarning=" | تعذر حفظ سجل الحذف محليا؛ لا تكرر أمر الراوتر."; }
                var message="نتيجة حذف المنتهية: "+result.Deleted+" من "+
                    result.Requested+" | تخطي "+result.Skipped+
                    " | فشل "+result.Failed.Count+auditWarning;
                if(result.UncertainOutcome || result.RemainingUserManager is null)
                {
                    pageLabel.Text=message+" | نتيجة اتصال غير مؤكدة."+
                        " لا تعاود العملية قبل التحديث من الراوتر.";
                    LifecycleStatus.Text=pageLabel.Text;
                    _lastLifecycle=null;
                }
                else
                {
                    var updated=new UserManagerV6Inventory(
                        result.RemainingUserManager,true,
                        "بيانات من فحص RouterOS السابق مباشرة للحذف؛ "+
                        "تحديث يدوي مطلوب للتغييرات اللاحقة.");
                    _lastLifecycle=updated;
                    matches=updated.Users.Where(x=>x.Status==kind).ToArray();
                    Enabled.Text=ActiveLegend.Text=updated.Active.ToString("N0",CultureInfo.InvariantCulture);
                    Expired.Text=ExpiredLegend.Text=updated.Expired.ToString("N0",CultureInfo.InvariantCulture);
                    DisabledLegend.Text=updated.Valid.ToString("N0",CultureInfo.InvariantCulture);

                    static string Pct(int value,int total)=>total==0?"0%":
                        (100d*value/total).ToString("0.0",CultureInfo.InvariantCulture)+"%";
                    ActivePercent.Text=Pct(updated.Active,updated.Total);
                    ExpiredPercent.Text=Pct(updated.Expired,updated.Total);
                    ValidPercent.Text=Pct(updated.Valid,updated.Total);

                    var ranked=updated.Users.Where(x=>!string.IsNullOrWhiteSpace(x.Profile))
                        .GroupBy(x=>x.Profile)
                        .Select(g=>new ProfileCount(g.Key,g.Count()))
                        .OrderByDescending(x=>x.Count)
                        .ThenBy(x=>x.Name,StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    RenderProfiles(ranked);
                    currentPage=0;UpdatePage();
                    LifecycleStatus.Text=message+
                        " | أُعيد عرض البيانات من فحص الحذف دون قراءة ثانية مكلفة.";
                }
            }
            catch(Exception ex)
            {
                pageLabel.Text="توقف الحذف الآمن: "+ex.Message+
                    " | تحقق من الراوتر قبل أي إعادة.";
                _lastLifecycle=null;
                LifecycleStatus.Text=pageLabel.Text;
            }
            finally{filter.IsEnabled=true;deleteExpired.IsEnabled=false;}
        };
        UpdatePage();
        content.Children.Add(grid);
        dialog.Content=content;
        dialog.ShowDialog();
    }

    private void RenderActivity(IReadOnlyList<VoucherActivityRow> activities)
    {
        ActivityRows.Children.Clear();
        var heading=new Grid{Background=(Brush)FindResource("SurfaceRaisedBrush"),Height=29};
        heading.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        heading.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0.85,GridUnitType.Star)});
        heading.Children.Add(new TextBlock{
            Text="العملية",Foreground=Brushes.White,FontSize=12,
            FontWeight=FontWeights.ExtraBold,VerticalAlignment=VerticalAlignment.Center,
            Margin=new Thickness(7,0,7,0)});
        var timeTitle=new TextBlock{
            Text="الوقت / التفاصيل",Foreground=Brushes.White,FontSize=12,
            FontWeight=FontWeights.ExtraBold,VerticalAlignment=VerticalAlignment.Center,
            Margin=new Thickness(6,0,6,0)};
        Grid.SetColumn(timeTitle,1);heading.Children.Add(timeTitle);
        ActivityRows.Children.Add(heading);
        if(activities.Count==0)
        {
            ActivityRows.Children.Add(new TextBlock{
                Text="لا توجد عمليات مسجلة بعد.",Foreground=Brushes.LightGray,
                Margin=new Thickness(8,12,8,10)});
            return;
        }
        foreach(var item in activities.Take(6))
        {
            var line=new Grid{
                Background=(Brush)FindResource("CanvasBrush"),
                Height=25,Margin=new Thickness(0,0,0,1)
            };
            line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0.85,GridUnitType.Star)});
            var color=item.Status switch{
                "SUCCESS"=>Brushes.LightGreen,
                "ERROR"=>Brushes.LightCoral,
                "PARTIAL"=>Brushes.Khaki,
                _=>Brushes.LightSkyBlue};
            line.Children.Add(new TextBlock{
                Text="● "+item.Action,Foreground=color,FontSize=12,
                FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,
                TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(7,0,7,0)
            });
            var stamp=DateTimeOffset.TryParse(item.Timestamp,out var t)
                ?t.ToLocalTime().ToString("HH:mm",CultureInfo.InvariantCulture):"—";
            var detail=new TextBlock{
                Text=stamp+"  "+item.Detail,ToolTip=item.Detail,Foreground=Brushes.White,FontSize=11,
                VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,
                Margin=new Thickness(6,0,6,0)};
            Grid.SetColumn(detail,1);line.Children.Add(detail);
            ActivityRows.Children.Add(line);
        }
    }

    private static string FormatBytes(string raw) => long.TryParse(raw, out long bytes)
        ? (bytes / (1024d * 1024d)).ToString("N1", CultureInfo.InvariantCulture) + " MB" : raw;

    private void RenderProfiles(IReadOnlyList<ProfileCount> profiles)
    {
        ProfileBars.Children.Clear();
        var groups = profiles.Take(4).ToList();
        if (groups.Count == 0)
        {
            ProfileBars.Children.Add(new TextBlock { Text = "لا توجد كروت على الراوتر", Foreground = new SolidColorBrush(Color.FromRgb(75,27,50)) });
            return;
        }
        var palette=new Brush[]
        {
            new SolidColorBrush(Color.FromRgb(31,126,102)),
            new SolidColorBrush(Color.FromRgb(195,69,104)),
            new SolidColorBrush(Color.FromRgb(57,104,185)),
            new SolidColorBrush(Color.FromRgb(168,100,31))
        };
        int max = groups[0].Count;
        for (int i = 0; i < groups.Count; i++)
        {
            var barBrush=palette[i%palette.Length];
            var stack = new StackPanel { Margin = new Thickness(0, 3, 0, 11) };
            var label=new Grid{FlowDirection=FlowDirection.RightToLeft};
            label.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            label.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(90)});
            label.Children.Add(new TextBlock {
                Text=groups[i].Name,FontSize=12,FontWeight=FontWeights.Bold,
                Margin=new Thickness(4,0,10,0),
                Foreground=(Brush)new BrushConverter().ConvertFrom("#471A30")!,TextTrimming=TextTrimming.CharacterEllipsis,
                VerticalAlignment=VerticalAlignment.Center});
            var count=new TextBlock{
                Text=groups[i].Count.ToString("N0",CultureInfo.InvariantCulture),
                FontSize=12,FontWeight=FontWeights.ExtraBold,
                Foreground=barBrush,
                TextAlignment=TextAlignment.Center
            };
            Grid.SetColumn(count,1);label.Children.Add(count);
            stack.Children.Add(label);
            var barTrack = new Grid { Background = (Brush)new BrushConverter().ConvertFrom("#E8D4C6")!, Height = 8, Margin = new Thickness(0, 4, 0, 0) };
            var bar = new Border { Background = barBrush,
                CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
            barTrack.Children.Add(bar);
            int value = groups[i].Count;
            barTrack.SizeChanged += (_, _) => bar.Width = Math.Max(0, barTrack.ActualWidth * value / Math.Max(1, max));
            stack.Children.Add(barTrack);
            ProfileBars.Children.Add(stack);
        }
    }
}
