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

public class CardsView : UserControl
{
    private readonly RouterGateway _gateway;
    private readonly VoucherStore? _audit;
    private string CleanupAudit(string service,ExpiredTransferDeletionResult outcome)
    {
        if(_audit is null)return "";
        try{_audit.RecordExpiredCleanup(service,outcome);return "";}
        catch{return " | تعذر حفظ سجل العملية؛ لا تكرر الحذف بسبب ذلك.";}
    }
    private readonly DataGrid _table = PageUi.Table(("الكرت", "Name"), ("الباقة", "Profile"), ("المستهلك", "ConsumedBytes"),
        ("Transfer Limit", "TransferLimit"), ("معطل", "Disabled"), ("منتهي", "IsExpiredByTransferLimit"));
    private readonly TextBlock _status = PageUi.Status();
    private readonly DataGrid _umGrid = PageUi.Table(
        ("الكرت", "Username"), ("الباقة الفعلية", "Profile"), ("الحالة", "Status"),
        ("المستهلك", "ConsumedBytes"), ("Transfer Limit", "TransferLimit"),
        ("السبب", "Reason"));
    private readonly TextBlock _umStatus = PageUi.Status();
    private readonly Button _deleteExpiredButton = PageUi.Button("حذف المنتهية بالاستهلاك", false);
    private bool _bulkDeleting;
    private IReadOnlyList<HotspotUser> _hotspotUsers=[];
    private IReadOnlyList<UserManagerV6Voucher> _usermanUsers=[];
    private Action? _filterHotspot;
    private Action? _filterUm;
    private Action? _syncHotspot;
    private Action? _syncUm;
    public CardsView(RouterGateway gateway,VoucherStore? audit=null)
    {
        _gateway = gateway;
        _audit=audit;
        var stack = PageUi.Column();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 15) };
        var refresh = PageUi.Button("تحديث", false); refresh.Click += async (_, _) => await RefreshAsync();buttons.Children.Add(refresh);
        foreach (var (name, state) in new[] { ("تعطيل الكرت", true), ("تفعيل الكرت", false) })
        {
            var btn = PageUi.Button(name, false); btn.Click += async (_, _) => await DisableAsync(state);buttons.Children.Add(btn);
        }
        var delete = PageUi.Button("حذف المحدد", false);delete.Click += async (_, _) => await DeleteAsync();buttons.Children.Add(delete);
        _deleteExpiredButton.Click += async (_, _) => await DeleteExpiredAsync();
        buttons.Children.Add(_deleteExpiredButton);
        stack.Children.Add(buttons);
        var search=PageUi.SearchInput();
        var profiles=new ComboBox{MinWidth=180};
        profiles.Items.Add("كل الباقات");profiles.SelectedIndex=0;
        var online=new CheckBox{Content="المتصلون الآن",Margin=new Thickness(10,12,6,0)};
        var filters=new WrapPanel();
        filters.Children.Add(PageUi.Label("بحث"));filters.Children.Add(search);
        filters.Children.Add(PageUi.Label("الباقة"));filters.Children.Add(profiles);
        filters.Children.Add(online);stack.Children.Add(filters);
        var showPasswords=new CheckBox{Content="إظهار كلمات المرور (Hotspot)",
            Margin=new Thickness(4,4,0,9)};
        var secretColumn=new DataGridTextColumn{Header="كلمة المرور",
            Binding=new System.Windows.Data.Binding("Password"),
            Visibility=Visibility.Collapsed,MinWidth=110};
        _table.Columns.Insert(1,secretColumn);
        showPasswords.Checked+=(_,_) =>
        {
            if(!PageUi.Confirm("عرض كلمات المرور الحقيقية على الشاشة؟"))
            {showPasswords.IsChecked=false;return;}
            secretColumn.Visibility=Visibility.Visible;
        };
        showPasswords.Unchecked+=(_,_)=>secretColumn.Visibility=Visibility.Collapsed;
        stack.Children.Add(showPasswords);
        var pager=new WrapPanel();
        var previous=PageUi.Button("السابق",false);
        var next=PageUi.Button("التالي",false);
        var pageStatus=PageUi.Status();
        pager.Children.Add(previous);pager.Children.Add(next);pager.Children.Add(pageStatus);
        int page=0;
        HashSet<string>? onlineNames=null;
        void FilterHotspot()
        {
            var term=search.Text.Trim();var profile=profiles.SelectedItem?.ToString()??"كل الباقات";
            var items=_hotspotUsers.Where(x=>
                (term.Length==0||x.Name.Contains(term,StringComparison.OrdinalIgnoreCase)
                    ||x.Profile.Contains(term,StringComparison.OrdinalIgnoreCase))
                &&(profile=="كل الباقات"||x.Profile==profile)
                &&(online.IsChecked!=true||onlineNames?.Contains(x.Name)==true)).ToArray();
            int pages=Math.Max(1,(items.Length+249)/250);
            page=Math.Clamp(page,0,pages-1);
            _table.ItemsSource=items.Skip(page*250).Take(250).ToArray();
            previous.IsEnabled=page>0;next.IsEnabled=page+1<pages;
            pageStatus.Text="صفحة "+(page+1)+" من "+pages+
                " | "+items.Length+" مطابق من "+_hotspotUsers.Count;
        }
        _filterHotspot=FilterHotspot;
        _syncHotspot=()=>
        {
            profiles.Items.Clear();profiles.Items.Add("كل الباقات");
            foreach(var name in _hotspotUsers.Select(x=>x.Profile).Distinct()
                .Where(x=>!string.IsNullOrWhiteSpace(x))
                .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
                profiles.Items.Add(name);
            profiles.SelectedIndex=0;page=0;FilterHotspot();
        };
        search.TextChanged+=(_,_)=>{page=0;FilterHotspot();};
        profiles.SelectionChanged+=(_,_)=>{page=0;FilterHotspot();};
        previous.Click+=(_,_)=>{page--;FilterHotspot();};
        next.Click+=(_,_)=>{page++;FilterHotspot();};
        online.Checked+=async(_,_) =>
        {
            online.IsEnabled=false;
            try
            {
                var sessions=await _gateway.ReadSessionsAsync();
                onlineNames=sessions.Select(x=>x.User).ToHashSet(StringComparer.OrdinalIgnoreCase);
                page=0;FilterHotspot();
            }
            catch(Exception ex)
            {
                onlineNames=null;online.IsChecked=false;
                _status.Text="تعذرت قراءة المتصلين: "+ex.Message;
            }
            finally{online.IsEnabled=true;}
        };
        online.Unchecked+=(_,_)=>{onlineNames=null;page=0;FilterHotspot();};
        stack.Children.Add(_table);stack.Children.Add(pager);stack.Children.Add(_status);
        var umSection = PageUi.Column();
        umSection.Children.Add(PageUi.Title("مستخدمي User Manager v6"));
        umSection.Children.Add(PageUi.Hint(
            "تُقرأ حالات مدير المستخدمين عند الطلب فقط. المنتهي يعني استنفاد Transfer Limit موجب؛ انتهاء الوقت وحده لا يكفي."));
        var loadUm = PageUi.Button("قراءة وتصنيف حسابات User Manager v6", false);
        umSection.Children.Add(loadUm);
        var umSearch=PageUi.SearchInput();
        var umProfiles=new ComboBox{MinWidth=180};
        umProfiles.Items.Add("كل الباقات");umProfiles.SelectedIndex=0;
        var umFilter=new WrapPanel();
        umFilter.Children.Add(PageUi.Label("بحث"));umFilter.Children.Add(umSearch);
        umFilter.Children.Add(PageUi.Label("الباقة"));umFilter.Children.Add(umProfiles);
        umSection.Children.Add(umFilter);
        umSection.Children.Add(_umGrid);
        var umPager=new WrapPanel();
        var umPrev=PageUi.Button("السابق",false);
        var umNext=PageUi.Button("التالي",false);
        var umPageStatus=PageUi.Status();
        umPager.Children.Add(umPrev);umPager.Children.Add(umNext);
        umPager.Children.Add(umPageStatus);umSection.Children.Add(umPager);
        int umPage=0;
        void FilterUm()
        {
            var term=umSearch.Text.Trim();var profile=umProfiles.SelectedItem?.ToString()??"كل الباقات";
            var items=_usermanUsers.Where(x=>
                (term.Length==0||x.Username.Contains(term,StringComparison.OrdinalIgnoreCase)
                    ||x.Profile.Contains(term,StringComparison.OrdinalIgnoreCase))
                &&(profile=="كل الباقات"||x.Profile==profile)).ToArray();
            int pages=Math.Max(1,(items.Length+249)/250);
            umPage=Math.Clamp(umPage,0,pages-1);
            _umGrid.ItemsSource=items.Skip(umPage*250).Take(250).ToArray();
            umPrev.IsEnabled=umPage>0;umNext.IsEnabled=umPage+1<pages;
            umPageStatus.Text="صفحة "+(umPage+1)+" من "+pages+
                " | "+items.Length+" مطابق من "+_usermanUsers.Count;
        }
        _filterUm=FilterUm;
        _syncUm=()=>
        {
            umProfiles.Items.Clear();umProfiles.Items.Add("كل الباقات");
            foreach(var name in _usermanUsers.Select(x=>x.Profile).Distinct()
                .Where(x=>!string.IsNullOrWhiteSpace(x))
                .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))umProfiles.Items.Add(name);
            umProfiles.SelectedIndex=0;umPage=0;FilterUm();
        };
        umSearch.TextChanged+=(_,_)=>{umPage=0;FilterUm();};
        umProfiles.SelectionChanged+=(_,_)=>{umPage=0;FilterUm();};
        umPrev.Click+=(_,_)=>{umPage--;FilterUm();};
        umNext.Click+=(_,_)=>{umPage++;FilterUm();};
        var umActions = new StackPanel { Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 5) };
        var umEnable = PageUi.Button("تفعيل الحساب المحدد", false);
        var umDisable = PageUi.Button("تعطيل الحساب المحدد", false);
        var umDelete = PageUi.Button("حذف الحساب المحدد", false);
        umActions.Children.Add(umEnable);
        umActions.Children.Add(umDisable);
        umActions.Children.Add(umDelete);
        var umDeleteExpired = PageUi.Button("حذف المنتهية بالاستهلاك", false);
        umActions.Children.Add(umDeleteExpired);
        umSection.Children.Add(umActions);
        umSection.Children.Add(_umStatus);
        bool umBusy = false;
        async Task ApplyUserManagerAction(string action)
        {
            if (umBusy) return;
            if (_umGrid.SelectedItem is not UserManagerV6Voucher selected)
            {
                _umStatus.Text = "اختر حساب User Manager من الجدول أولاً.";
                return;
            }
            var name = action=="disable" ? "تعطيل" : action=="enable" ? "تفعيل" : "حذف نهائي";
            if (!PageUi.Confirm(name + " حساب User Manager «" + selected.Username +
                "»؟ سيتم التحقق من معرف الحساب وباقته على الراوتر قبل التنفيذ.")) return;
            umBusy = true;
            umEnable.IsEnabled = umDisable.IsEnabled = umDelete.IsEnabled =
                umDeleteExpired.IsEnabled = false;
            try
            {
                var result = await _gateway.ChangeUserManagerV6UserAsync(selected,action);
                _umStatus.Text = result.Diagnostic +
                    (result.UncertainOutcome ? " لا تعاود التنفيذ دون مراجعة الراوتر." : "");
                if (result.Success)
                {
                    var refreshed = await _gateway.ReadUserManagerV6InventoryAsync();
                    _usermanUsers=refreshed.Users;_syncUm?.Invoke();
                }
            }
            catch (Exception ex)
            {
                _umStatus.Text = "أُوقفت العملية أو فشل التحقق: " + ex.Message;
            }
            finally
            {
                umBusy = false;
                umEnable.IsEnabled = umDisable.IsEnabled = umDelete.IsEnabled =
                    umDeleteExpired.IsEnabled = true;
            }
        }
        umEnable.Click += async (_, _) => await ApplyUserManagerAction("enable");
        umDisable.Click += async (_, _) => await ApplyUserManagerAction("disable");
        umDelete.Click += async (_, _) => await ApplyUserManagerAction("remove");
        umDeleteExpired.Click += async (_, _) =>
        {
            if (umBusy) return;
            var snapshot=_usermanUsers.ToArray();
            var eligible=snapshot.Where(x=>x.Status=="expired"
                && x.ExpirySource=="transfer-limit" && x.TransferLimit>0
                && x.ConsumedBytes>=x.TransferLimit && x.Started
                && !x.Disabled && !x.Incomplete).ToArray();
            if(eligible.Length==0)
            {
                _umStatus.Text="لا توجد كروت User Manager منتهية باستنفاد Transfer Limit في القائمة الحالية.";
                return;
            }
            if(!PageUi.Confirm("حذف " + eligible.Length +
                " كرت User Manager v6 منتهياً بالاستهلاك نهائياً؟ " +
                "ستُقرأ الحالات الحالية من الراوتر مجدداً، ويُستبعد من تغيّرت باقته أو رصيده."))
                return;
            umBusy=true;
            umEnable.IsEnabled=umDisable.IsEnabled=umDelete.IsEnabled=
                umDeleteExpired.IsEnabled=false;
            try
            {
                _umStatus.Text="جارٍ إعادة التحقق من Transfer Limit ثم حذف الكروت المؤكدة...";
                var result=await _gateway.DeleteExpiredUserManagerV6Async(eligible);
                var auditNote=CleanupAudit("userman",result);
                var summary="حُذف " + result.Deleted + " من أصل " + result.Requested +
                    "؛ تم تخطي " + result.Skipped + "، وفشل " + result.Failed.Count + "."+auditNote;
                if(result.UncertainOutcome)
                {
                    _umStatus.Text=summary +
                        " انقطع الاتصال أثناء الحذف؛ لا تعاود الأمر قبل مراجعة الراوتر.";
                }
                else
                {
                    // The gateway already verified each candidate from a fresh
                    // full snapshot. Reusing it saves an expensive second UM scan.
                    if(result.RemainingUserManager is { } remaining)
                    {
                        _usermanUsers=remaining;
                        _syncUm?.Invoke();
                    }
                    _umStatus.Text=summary+
                        " تحديث محلي من فحص الراوتر المؤكد؛ يمكنك التحديث يدويا لاحقا.";
                }
            }
            catch(Exception ex)
            {
                _umStatus.Text="أُوقف التنظيف دون متابعة الحذف: " + ex.Message;
            }
            finally
            {
                umBusy=false;
                umEnable.IsEnabled=umDisable.IsEnabled=umDelete.IsEnabled=
                    umDeleteExpired.IsEnabled=true;
            }
        };
        var pppSection = PageUi.Column();
        pppSection.Children.Add(PageUi.Title("حسابات وجلسات PPP / PPPoE"));
        pppSection.Children.Add(PageUi.Hint(
            "عرض الحسابات والباقات والجلسات من RouterOS 6؛ يتطلب الحذف أو الفصل تأكيداً مستقلاً وقراءة حية للتحقق من الهوية."));
        var loadPpp = PageUi.Button("تحديث حسابات وجلسات PPP", false);
        var pppAccounts = PageUi.Table(
            ("الاسم", "Name"), ("الباقة", "Profile"), ("الخدمة", "Service"),
            ("معطل", "Disabled"), ("متصل", "Online"), ("ملاحظات", "Comment"));
        var pppSessions = PageUi.Table(
            ("الحساب", "Name"), ("الخدمة", "Service"), ("العنوان", "Address"),
            ("معرف الاتصال", "CallerId"), ("مدة الجلسة", "Uptime"));
        var pppProfiles = PageUi.Table(
            ("الباقة", "Name"), ("حد السرعة", "RateLimit"),
            ("العنوان المحلي", "LocalAddress"), ("العنوان البعيد", "RemoteAddress"));
        var pppStatus = PageUi.Status();
        pppSection.Children.Add(loadPpp);
        var pppActions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 7)
        };
        var pppEnable = PageUi.Button("تفعيل حساب PPP المحدد", false);
        var pppDisable = PageUi.Button("تعطيل حساب PPP المحدد", false);
        var pppDelete = PageUi.Button("حذف حساب PPP المحدد", false);
        var pppDisconnect = PageUi.Button("فصل جلسة PPP المحددة", false);
        pppActions.Children.Add(pppEnable);
        pppActions.Children.Add(pppDisable);
        pppActions.Children.Add(pppDelete);
        pppActions.Children.Add(pppDisconnect);
        pppSection.Children.Add(pppActions);
        pppSection.Children.Add(PageUi.Label("الحسابات المسجلة"));
        pppSection.Children.Add(pppAccounts);
        pppSection.Children.Add(PageUi.Label("الجلسات المتصلة"));
        pppSection.Children.Add(pppSessions);
        pppSection.Children.Add(PageUi.Label("باقات PPP"));
        pppSection.Children.Add(pppProfiles);
        var pppRatePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 7)
        };
        var pppRateInput = PageUi.Input("4M/4M");
        pppRateInput.MinWidth = 150;
        pppRatePanel.Children.Add(PageUi.Label("حد السرعة الجديد (مثال 4M/4M)"));
        pppRatePanel.Children.Add(pppRateInput);
        var pppRateButton = PageUi.Button("حفظ سرعة باقة PPP المحددة", false);
        pppRatePanel.Children.Add(pppRateButton);
        pppSection.Children.Add(pppRatePanel);
        pppSection.Children.Add(pppStatus);
        pppProfiles.SelectionChanged += (_, _) =>
        {
            if (pppProfiles.SelectedItem is PppProfile profile)
                pppRateInput.Text = profile.RateLimit;
        };
        bool pppBusy = false;
        async Task RefreshPpp()
        {
            pppStatus.Text = "جارٍ قراءة حسابات PPP وجلساتها وباقاتها...";
            try
            {
                var report = await _gateway.ReadPppInventoryAsync();
                pppAccounts.ItemsSource = report.Accounts;
                pppSessions.ItemsSource = report.ActiveSessions;
                pppProfiles.ItemsSource = report.Profiles;
                pppStatus.Text = "الحسابات: " + report.Accounts.Count +
                    " | الجلسات الحالية: " + report.ActiveSessions.Count +
                    " | الباقات: " + report.Profiles.Count;
            }
            catch (Exception ex)
            {
                pppAccounts.ItemsSource = null;
                pppSessions.ItemsSource = null;
                pppProfiles.ItemsSource = null;
                pppStatus.Text = "تعذرت قراءة بيانات PPP؛ لا توجد نتائج مؤكدة: " + ex.Message;
            }
        }
        loadPpp.Click += async (_, _) =>
        {
            if (pppBusy) return;
            pppBusy = true;
            loadPpp.IsEnabled = pppEnable.IsEnabled = pppDisable.IsEnabled =
                    pppDelete.IsEnabled = pppDisconnect.IsEnabled =
                    pppRateButton.IsEnabled = pppRateInput.IsEnabled = false;
            try { await RefreshPpp(); }
            finally
            {
                pppBusy = false;
                loadPpp.IsEnabled = pppEnable.IsEnabled = pppDisable.IsEnabled =
                    pppDelete.IsEnabled = pppDisconnect.IsEnabled =
                    pppRateButton.IsEnabled = pppRateInput.IsEnabled = true;
            }
        };
        async Task SetPppDisabled(bool disabled)
        {
            if (pppBusy) return;
            if (pppAccounts.SelectedItem is not PppAccount selected)
            {
                pppStatus.Text = "اختر حساباً من جدول PPP أولاً.";
                return;
            }
            if (!PageUi.Confirm((disabled ? "تعطيل" : "تفعيل") +
                " حساب PPP «" + selected.Name +
                "»؟ سيتم التحقق من الهوية والباقة والحالة على الراوتر قبل الإجراء."))
                return;
            pppBusy = true;
            loadPpp.IsEnabled = pppEnable.IsEnabled = pppDisable.IsEnabled =
                    pppDelete.IsEnabled = pppDisconnect.IsEnabled =
                    pppRateButton.IsEnabled = pppRateInput.IsEnabled = false;
            try
            {
                var result = await _gateway.SetPppAccountDisabledAsync(selected, disabled);
                if (result.Success)
                {
                    await RefreshPpp();
                    pppStatus.Text = result.Diagnostic;
                }
                else
                {
                    pppStatus.Text = result.Diagnostic +
                        (result.UncertainOutcome ?
                        " لا تعاود العملية قبل التحقق من الراوتر." : "");
                }
            }
            catch (Exception ex)
            {
                pppStatus.Text = "أُوقف تعديل PPP دون تأكيد النجاح: " + ex.Message;
            }
            finally
            {
                pppBusy = false;
                loadPpp.IsEnabled = pppEnable.IsEnabled = pppDisable.IsEnabled =
                    pppDelete.IsEnabled = pppDisconnect.IsEnabled =
                    pppRateButton.IsEnabled = pppRateInput.IsEnabled = true;
            }
        }
        pppEnable.Click += async (_, _) => await SetPppDisabled(false);
        pppDisable.Click += async (_, _) => await SetPppDisabled(true);

        async Task RemoveOnePppRecord(bool disconnect)
        {
            if (pppBusy) return;
            var chosen = disconnect ? pppSessions.SelectedItem : pppAccounts.SelectedItem;
            if (disconnect && chosen is not PppActiveSession ||
                !disconnect && chosen is not PppAccount)
            {
                pppStatus.Text = disconnect
                    ? "اختر جلسة PPP محددة من الجدول." : "اختر حساب PPP محدداً من الجدول.";
                return;
            }
            string name = disconnect ? ((PppActiveSession)chosen!).Name : ((PppAccount)chosen!).Name;
            string verb = disconnect ? "فصل الجلسة" : "حذف الحساب نهائياً";
            if (!PageUi.Confirm(verb + " للحساب «" + name +
                "»؟ سيُفحص المعرّف وبيانات السجل على الراوتر قبل التنفيذ.")) return;

            pppBusy = true;
            loadPpp.IsEnabled = pppEnable.IsEnabled = pppDisable.IsEnabled =
                pppDelete.IsEnabled = pppDisconnect.IsEnabled =
                    pppRateButton.IsEnabled = pppRateInput.IsEnabled = false;
            try
            {
                var result = disconnect
                    ? await _gateway.DisconnectPppSessionAsync((PppActiveSession)chosen!)
                    : await _gateway.DeletePppAccountAsync((PppAccount)chosen!);
                if (result.Success)
                {
                    await RefreshPpp();
                    pppStatus.Text = result.Diagnostic;
                }
                else pppStatus.Text = result.Diagnostic +
                    (result.UncertainOutcome ? " لا تُعد العملية قبل مراجعة الراوتر." : "");
            }
            catch (Exception ex)
            {
                pppStatus.Text = "أُوقفت العملية دون تأكيد التنفيذ: " + ex.Message;
            }
            finally
            {
                pppBusy = false;
                loadPpp.IsEnabled = pppEnable.IsEnabled = pppDisable.IsEnabled =
                    pppDelete.IsEnabled = pppDisconnect.IsEnabled =
                    pppRateButton.IsEnabled = pppRateInput.IsEnabled = true;
            }
        }
        pppDelete.Click += async (_, _) => await RemoveOnePppRecord(false);
        pppDisconnect.Click += async (_, _) => await RemoveOnePppRecord(true);
        pppRateButton.Click += async (_, _) =>
        {
            if (pppBusy) return;
            if (pppProfiles.SelectedItem is not PppProfile selected)
            {
                pppStatus.Text = "حدد باقة PPP من جدول الباقات أولاً.";
                return;
            }
            var rate = pppRateInput.Text.Trim();
            if (!PageUi.Confirm("تغيير حد سرعة باقة «" + selected.Name +
                "» من «" + selected.RateLimit + "» إلى «" + rate +
                "»؟ سيُعاد التحقق من هوية الباقة وعناوينها قبل التعديل."))
                return;
            pppBusy = true;
            loadPpp.IsEnabled = pppEnable.IsEnabled = pppDisable.IsEnabled =
                pppDelete.IsEnabled = pppDisconnect.IsEnabled =
                pppRateButton.IsEnabled = pppRateInput.IsEnabled = false;
            try
            {
                var result = await _gateway.SetPppProfileRateLimitAsync(selected,rate);
                if (result.Success)
                {
                    await RefreshPpp();
                    pppStatus.Text = result.Diagnostic;
                }
                else
                    pppStatus.Text = result.Diagnostic +
                        (result.UncertainOutcome ? " لا تعاود التعديل قبل التحقق من الراوتر." : "");
            }
            catch (Exception ex)
            {
                pppStatus.Text = "أُوقف تعديل سرعة PPP: " + ex.Message;
            }
            finally
            {
                pppBusy = false;
                loadPpp.IsEnabled = pppEnable.IsEnabled = pppDisable.IsEnabled =
                    pppDelete.IsEnabled = pppDisconnect.IsEnabled =
                    pppRateButton.IsEnabled = pppRateInput.IsEnabled = true;
            }
        };
        var sections=PageUi.Column();
        var tabs=new TabControl{Margin=new Thickness(0,3,0,8)};
        tabs.Items.Add(new TabItem{Header="هوتسبوت",
            Content=PageUi.Surface(stack,"#866BAF")});
        tabs.Items.Add(new TabItem{Header="مدير المستخدمين",
            Content=PageUi.Surface(umSection,"#B0874F")});
        tabs.Items.Add(new TabItem{Header="برودباند PPPoE",
            Content=PageUi.Surface(pppSection,"#3D91A4")});
        tabs.SelectedIndex=0;
        sections.Children.Add(tabs);
        Content = PageUi.Wrap("إدارة كروت الشبكة",
            "Hotspot وUser Manager v6 وPPP في شاشة واحدة دون تكرار خيارات القائمة.",
            sections);
        loadUm.Click += async (_, _) =>
        {
            loadUm.IsEnabled = false;
            _umStatus.Text = "جارٍ فحص حسابات User Manager v6 من الراوتر...";
            try
            {
                var report = await _gateway.ReadUserManagerV6InventoryAsync();
                _usermanUsers=report.Users;_syncUm?.Invoke();
                _umStatus.Text = report.IsComplete
                    ? "الإجمالي: " + report.Total + " | صالحة: " + report.Valid +
                      " | نشطة: " + report.Active + " | منتهية بالاستهلاك: " + report.Expired +
                      " | معطلة: " + report.Disabled + " | غير مكتملة: " + report.Incomplete +
                      " | غير مصنفة: " + report.Unclassified +
                      (string.IsNullOrEmpty(report.Diagnostic) ? "" : " | " + report.Diagnostic)
                    : "تعذر التصنيف الموثوق: " + report.Diagnostic +
                      " | إجمالي الصفوف المقروءة: " + report.Total;
            }
            catch (Exception ex)
            {
                _usermanUsers=[];_syncUm?.Invoke();
                _umStatus.Text = "تعذرت قراءة User Manager: " + ex.Message;
            }
            finally { loadUm.IsEnabled = true; }
        };
        Loaded += (_,_)=>_status.Text="اضغط تحديث لجلب مستخدمي Hotspot من الراوتر؛ البحث محلي.";
    }
    private async Task RefreshAsync()
    {
        try { _status.Text = "جارٍ تحميل الكروت..."; var users = await _gateway.ReadUsersAsync();
            _hotspotUsers=users;_syncHotspot?.Invoke();
            _status.Text = "تم تحميل " + users.Count + " كرتا من الراوتر."; }
        catch (Exception ex) { _hotspotUsers=[];_syncHotspot?.Invoke();
            _status.Text = "فشل التحميل: " + ex.Message; }
    }
    private async Task DisableAsync(bool disabled)
    {
        if (_table.SelectedItem is not HotspotUser item) { _status.Text = "اختر كرتاً من الجدول."; return; }
        if (!PageUi.Confirm((disabled ? "تعطيل " : "تفعيل ") + item.Name + " على الراوتر؟")) return;
        try { await _gateway.SetUserDisabledAsync(item.Id, disabled); await RefreshAsync(); }
        catch (Exception ex) { _status.Text = "فشلت العملية: " + ex.Message; }
    }
    private async Task DeleteAsync()
    {
        if (_table.SelectedItem is not HotspotUser item) { _status.Text = "اختر كرتاً أولاً."; return; }
        if (!PageUi.Confirm("حذف الكرت «" + item.Name + "» من الراوتر نهائياً؟ لا يمكن التراجع.")) return;
        try { await _gateway.DeleteUserAsync(item.Id); await RefreshAsync(); }
        catch (Exception ex) { _status.Text = "فشل الحذف: " + ex.Message; }
    }
    private async Task DeleteExpiredAsync()
    {
        if (_bulkDeleting) return;
        var snapshot = _hotspotUsers.ToArray();
        var count = snapshot.Count(x => x.IsExpiredByTransferLimit);
        if (count == 0) { _status.Text = "لا توجد كروت منتهية بالاستهلاك في القائمة الحالية."; return; }
        if (!PageUi.Confirm("حذف " + count + " كرتاً منتهياً بحسب Transfer Limit فقط؟ " +
            "سيُعاد التحقق من حالة الكروت قبل الحذف، ولا يمكن التراجع عن العمليات الناجحة.")) return;
        _bulkDeleting = true;
        _deleteExpiredButton.IsEnabled = false;
        _status.Text = "جارٍ فحص حالة الكروت الحالية وحذف المنتهية المؤكدة...";
        try
        {
            var result = await _gateway.DeleteExpiredByTransferAsync(snapshot);
            var auditNote=CleanupAudit("hotspot",result);
            if(!result.UncertainOutcome && result.RemainingHotspot is { } remaining)
            {
                _hotspotUsers=remaining;
                _syncHotspot?.Invoke();
            }
            _status.Text = "حُذف " + result.Deleted + " من أصل " + result.Requested +
                " كرتاً؛ تغيّر/تُخطّي " + result.Skipped + "؛ فشل " + result.Failed.Count + "." +auditNote+
                (result.UncertainOutcome ? " انقطع الاتصال أثناء الحذف؛ راجع الراوتر قبل إعادة المحاولة." : "");
        }
        catch (Exception ex) { _status.Text = "تعذّر الحذف الجماعي: " + ex.Message; }
        finally { _bulkDeleting = false; _deleteExpiredButton.IsEnabled = true; }
    }
}

