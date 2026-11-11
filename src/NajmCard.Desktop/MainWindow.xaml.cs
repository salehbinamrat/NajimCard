using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using NajmCard.Desktop.Services.Licensing;
using System.Windows.Controls;
using System.Windows.Media;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;
using NajmCard.Desktop.Views;

namespace NajmCard.Desktop;

public sealed record NavEntry(string Key, Geometry Glyph, Brush Accent,
    Brush IconBackground, string Title, string Subtitle);

public partial class MainWindow : Window
{
    private ResilientRouterSession? _session;
    private bool _closed;
    private RouterGateway? _gateway;
    private readonly VoucherStore _store = new();
    private readonly HttpClient _licenseHttp = new();
    private readonly ProtectedPaidCodeStore _paidCodeStore=new();
    private PaidLicenseClient? _paidLicense;
    private LicenseGate? _licenseGate;
    private LocalTrialService? _localTrial;
    private DispatcherTimer? _licenseMonitor;
    private DispatcherTimer? _sidebarCountdown;
    private LicenseCheck? _lastLicenseStatus;
    private DashboardView? _dashboardPage;
    private string _storedPaidCode="";
    private string _activeLicenseMode="";
    private DateTimeOffset _nextPaidVerifyUtc=DateTimeOffset.MaxValue;
    private bool _licenseBusy;
    private bool _monitorBusy;
    private readonly SavedRouterConnectionStore _savedConnections = new();
    private bool _applyingSavedConnection;
    private string _activeMode = "local";
    private string _localHostDraft = "192.168.88.1";
    private string _remoteHostDraft = "";
    private bool _isConnecting;
    private string? _pendingVoucherService;
    private string? _pendingVoucherProfile;
    private readonly NavEntry[] _pages;

    private static NavEntry Entry(string key, string iconKey, string accent,
        string background, string title, string subtitle) => new(key,
        (Geometry)Application.Current.FindResource(iconKey),
        (Brush)new BrushConverter().ConvertFromInvariantString(accent)!,
        (Brush)new BrushConverter().ConvertFromInvariantString(background)!, title, subtitle);

    private static NavEntry[] BuildPages() =>
    [
        Entry("dashboard", "IconDashboard", "#EBC589", "#59412E", "الرئيسية", "بيانات الشبكة والرسوم البيانية"),
        Entry("generator", "IconPlus", "#A8E8CD", "#25584C", "إنشاء وطباعة الكروت", "إنشاء وإرسال وطباعة مع إعدادات كل باقة"),
        Entry("cards", "IconCard", "#B8C9FF", "#323E73", "إدارة الكروت", "عرض وإدارة Hotspot"),
        Entry("profiles", "IconPackages", "#DCC0FF", "#4C3469", "الباقات", "ملفات الباقات وسرعاتها"),
        Entry("sessions", "IconPeople", "#A2E6BB", "#305943", "المتصلون", "الجلسات الحية"),
        Entry("servers", "IconServer", "#FAC792", "#6A442F", "سيرفرات Hotspot", "حالة نقاط الاستضافة"),
        Entry("reports", "IconReport", "#9BDFF8", "#26566D", "التقارير", "تصدير وحفظ البيانات"),
        Entry("activity", "IconActivity", "#D6B3EA", "#584165", "النشاط", "سجل العمليات الفعلية"),
        Entry("tools", "IconTools", "#F5D890", "#5A4A2F", "التحكم بالشبكة والصيانة", "الجلسات والأجهزة والقاعدة والسيرفرات"),
        Entry("settings", "IconGear", "#C1BCFF", "#3A376A", "الإعدادات", "بيانات المشروع والنسخ")
    ];

    public MainWindow()
    {
        InitializeComponent();
        Loaded+=async(_,_)=>{FitWindowToWorkArea();await RestoreSavedPaidLicenseAsync();};
        try
        {
            _localTrial=new LocalTrialService(new ProtectedLocalTrialStore(),
                WindowsLicenseFingerprint.LocalTrial());
            _licenseGate=new LicenseGate(_localTrial);
            _paidLicense=new PaidLicenseClient(_licenseHttp,
                WindowsLicenseFingerprint.Paid(),cache:new SignedPaidLicenseCache());
            _storedPaidCode=_paidCodeStore.Load();
            var previousTrial=_localTrial.Check();
            if(previousTrial.Mode=="trial" && string.IsNullOrEmpty(_storedPaidCode))
                _licenseGate.SelectTrial();
            LicenseNotice.Text=previousTrial.Message+
                (string.IsNullOrEmpty(_storedPaidCode)?"":" | يوجد رمز مدفوع محفوظ؛ سيتحقق التطبيق من صلاحيته عند الدخول.");
            TrialButton.IsEnabled=previousTrial.Mode is "ready" or "trial";
            if(previousTrial.Mode=="expired")
                LicenseNotice.Foreground=new SolidColorBrush(
                    Color.FromRgb(255,164,173));
        }
        catch(Exception)
        {
            LicenseNotice.Text="تعذر التحقق من بصمة Windows أو سجل التجربة. "+
                "لم تُفعّل التجربة. تحقق من صلاحيات الجهاز.";
            TrialButton.IsEnabled=ConnectButton.IsEnabled=false;
        }
        _pages = BuildPages();
        NavList.ItemsSource = _pages;
        SavePasswordInput.IsEnabled = SaveConnectionInput.IsChecked == true;
        RefreshSavedConnections();
    }

    /// <summary>Keep the login button and the fixed voucher footer on-screen
    /// on Windows laptops; the creator scrolls only its form on short displays.</summary>
    private void FitWindowToWorkArea()
    {
        var screen=SystemParameters.WorkArea;
        if(screen.Width<=0||screen.Height<=0)return;
        // Fixed owner-approved window geometry. Never shrink or maximize automatically.
        Left=screen.Width>=Width?screen.Left+(screen.Width-Width)/2:screen.Left;
        Top=screen.Height>=Height?screen.Top+(screen.Height-Height)/2:screen.Top;
    }

    private string ActiveConnectionMode => RemoteModeInput.IsChecked == true ? "remote" : "local";

    private string CurrentPassword => ShowPasswordInput.IsChecked == true
        ? VisiblePasswordInput.Text : PasswordInput.Password;

    private void RefreshSavedConnections()
    {
        try
        {
            _applyingSavedConnection = true;
            var history=_savedConnections.Load();
            SavedConnectionsInput.ItemsSource=history;
            HistoryConnectionsGrid.ItemsSource=history;
            SavedConnectionsInput.SelectedIndex=-1;
            HistoryConnectionsGrid.SelectedIndex=-1;
        }
        catch(Exception ex)
        {
            LoginStatus.Text = "تعذر تحميل محفوظات الاتصال؛ لم تُحذف البيانات: " + ex.Message;
        }
        finally { _applyingSavedConnection = false; }
    }

    private void ConnectionMode_Checked(object sender, RoutedEventArgs e)
    {
        if(HostInput is null || RemoteModeInput is null || _applyingSavedConnection)return;
        string mode=ActiveConnectionMode;
        if(mode==_activeMode)return;
        if(_activeMode=="local")_localHostDraft=HostInput.Text;
        else _remoteHostDraft=HostInput.Text;
        _activeMode=mode;
        HostInput.Text=mode=="local"?_localHostDraft:_remoteHostDraft;
        ConnectionModeHint.Text=mode=="local"
            ? "محلي: اتصال مباشر بالشبكة الخاصة براوتر MikroTik."
            : "بعيد: شغّل VPN على Windows أولا ثم أدخل عنوان الراوتر الذي يمكن الوصول إليه عبر النفق. نجم كارد لا ينشئ VPN تلقائيا.";
    }

    private void SavedConnections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if(_applyingSavedConnection || HostInput is null || SavedConnectionsInput.SelectedItem
            is not SavedRouterConnection item)return;
        _applyingSavedConnection=true;
        try
        {
            _activeMode=item.Mode;
            HistoryConnectionsGrid.SelectedItem=item;
            LocalModeInput.IsChecked=item.Mode=="local";
            RemoteModeInput.IsChecked=item.Mode=="remote";
            if(item.Mode=="local")_localHostDraft=item.Host;else _remoteHostDraft=item.Host;
            HostInput.Text=item.Host;
            PortInput.Text=item.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            UserInput.Text=item.Username;
            TlsInput.IsChecked=item.UseTls;
            ShowPasswordInput.IsChecked=false;
            VisiblePasswordInput.Clear();
            PasswordInput.Password=item.Password;
            SaveConnectionInput.IsChecked=true;
            SavePasswordInput.IsEnabled=true;
            SavePasswordInput.IsChecked=!string.IsNullOrEmpty(item.Password);
            ConnectionModeHint.Text=item.Mode=="remote"
                ? "اتصال بعيد محفوظ: يجب أن يكون VPN على Windows متصلا قبل الدخول."
                : "اتصال محلي محفوظ: تحقق من تواجدك على شبكة الراوتر.";
            LoginStatus.Text="تم استرجاع بيانات الاتصال؛ لا يتم الاتصال تلقائيا.";
        }
        finally { _applyingSavedConnection=false; }
    }

    private void HistoryGrid_SelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(_applyingSavedConnection || HistoryConnectionsGrid.SelectedItem is not
            SavedRouterConnection item)return;
        // Selecting a row fills every field through the same audited handler as
        // the dropdown. It never attempts network login.
        SavedConnectionsInput.SelectedItem=item;
    }

    private void NewConnection_Click(object sender,RoutedEventArgs e)
    {
        _applyingSavedConnection=true;
        try
        {
            SavedConnectionsInput.SelectedIndex=-1;
            HostInput.Text=ActiveConnectionMode=="local"?"192.168.88.1":"";
            PortInput.Text=TlsInput.IsChecked==true?"8729":"8728";
            UserInput.Text="admin";
            PasswordInput.Clear();
            VisiblePasswordInput.Clear();
            ShowPasswordInput.IsChecked=false;
            SaveConnectionInput.IsChecked=false;
            SavePasswordInput.IsChecked=false;
        }
        finally {_applyingSavedConnection=false;}
        LoginStatus.Text="اتصال جديد؛ لا توجد بيانات اعتماد محفوظة لهذا النموذج.";
    }

    private void DeleteSavedConnection_Click(object sender,RoutedEventArgs e)
    {
        if(SavedConnectionsInput.SelectedItem is not SavedRouterConnection saved)
        {
            LoginStatus.Text="اختر اتصالا محفوظا للحذف أولا.";
            return;
        }
        if(MessageBox.Show("حذف إعدادات هذا الاتصال من نجم كارد فقط؟",
            "نجم كارد",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)
            return;
        try
        {
            _savedConnections.Delete(saved);
            RefreshSavedConnections();
            LoginStatus.Text="حُذف الاتصال المحدد دون المساس بالراوتر أو أي برنامج آخر.";
        }
        catch(Exception ex){LoginStatus.Text="تعذر حذف الاتصال: "+ex.Message;}
    }

    private void RevealPassword_Changed(object sender,RoutedEventArgs e)
    {
        if(PasswordInput is null || VisiblePasswordInput is null || ShowPasswordInput is null)
            return;
        if(ShowPasswordInput.IsChecked==true)
        {
            VisiblePasswordInput.Text=PasswordInput.Password;
            PasswordInput.Clear();
            PasswordInput.Visibility=Visibility.Collapsed;
            VisiblePasswordInput.Visibility=Visibility.Visible;
        }
        else
        {
            PasswordInput.Password=VisiblePasswordInput.Text;
            VisiblePasswordInput.Clear();
            VisiblePasswordInput.Visibility=Visibility.Collapsed;
            PasswordInput.Visibility=Visibility.Visible;
        }
    }

    private void SaveConnection_Changed(object sender,RoutedEventArgs e)
    {
        if(SavePasswordInput is null || SaveConnectionInput is null)return;
        SavePasswordInput.IsEnabled=SaveConnectionInput.IsChecked==true;
        if(_applyingSavedConnection)return;
        // Checking the explicit full-credentials opt-in saves the password via
        // the existing DPAPI CurrentUser store; users may uncheck the secret.
        SavePasswordInput.IsChecked=SaveConnectionInput.IsChecked==true;
    }

    private void About_Click(object sender,RoutedEventArgs e)
    {
        MessageBox.Show(
            "نجم كارد 0.1.0 — C#/.NET 10 WPF\n" +
            "برنامج مستقل لإدارة MikroTik وإنشاء الكروت، بهوية عنابية ذهبية.\n" +
            "التجربة المجانية المحلية: 20 يوما تبدأ بعد أول اتصال ناجح بـ MikroTik. "+
            "الترخيص المدفوع متوافق مع منصة حضرموت، ويرتبط بجهاز واحد عبر HTTPS.",
            "حول نجم كارد",MessageBoxButton.OK,MessageBoxImage.Information);
    }

    private static string TrialNotice(LicenseCheck status)
    {
        if(status.Mode=="expired")return
            "انتهت التجربة المجانية. شكرا لتجربة نجم كارد لمدة 20 يوما. "+
            "للاستمرار دون انقطاع، اطلب رمز ترخيص مدفوع من مركز تراخيص حضرموت.";
        if(status.Mode=="clock_error")return status.Message;
        if(status.Mode!="trial")return status.Message;
        if(status.RemainingSeconds<=86400)return
            "تنبيه أخير: بقي من تجربتك أقل من 24 ساعة. "+
            "يمكنك شراء الترخيص الآن لتستمر خدمات الشبكة والطباعة دون انقطاع. "+
            status.Message;
        if(status.RemainingSeconds<=5*86400)return
            "تجربتك تقترب من نهايتها؛ جهّز رمز الترخيص للمتابعة بسهولة. "+
            status.Message;
        return status.Message;
    }

    private void ChooseTrial_Click(object sender,RoutedEventArgs e)
    {
        if(_localTrial is null || _licenseGate is null)return;
        var status=_localTrial.Check();
        if(status.Mode is not ("ready" or "trial"))
        {
            LicenseNotice.Text=status.Message;
            TrialButton.IsEnabled=false;
            return;
        }
        _licenseGate.SelectTrial();
        LicenseNotice.Text=status.Mode=="ready"
            ?"تم اختيار التجربة المجانية. ابدأ الاتصال بالراوتر؛ ستبدأ مدة 480 ساعة عند نجاح الدخول فقط."
            :status.Message;
    }

    private async void ActivateLicense_Click(object sender,RoutedEventArgs e)
    {
        if(_licenseBusy || _paidLicense is null || _licenseGate is null)return;
        var code=PaidLicenseFeedVerifier.NormalizeCode(LicenseCodeInput.Text);
        if(code.Length!=32)
        {
            LicenseNotice.Text="رمز الترخيص غير صالح؛ تأكد من إدخال 32 خانة.";
            return;
        }
        _licenseBusy=true;ActivateLicenseButton.IsEnabled=false;
        LicenseNotice.Text="جارٍ التحقق من توقيع الترخيص وربطه بهذا الجهاز عند الحاجة...";
        try
        {
            var response=await _paidLicense.ActivateAsync(code);
            LicenseNotice.Text=response.Message;
            if(response.Mode is "active" or "claim_pending")
            {
                _paidCodeStore.Save(code);
                _storedPaidCode=code;
            }
            _licenseGate.SetPaid(response);
            if(response.Permitted)
                ShowActivePaidLicense(response);
            else
                LicenseNotice.Foreground=new SolidColorBrush(Color.FromRgb(255,185,168));
        }
        catch(Exception)
        {
            LicenseNotice.Text="تعذر إتمام التفعيل أو حفظ الرمز. "+
                "راجع الإنترنت وصلاحيات Windows ثم تحقق من حالة الرمز.";
        }
        finally{_licenseBusy=false;ActivateLicenseButton.IsEnabled=true;}
    }

    private async Task RestoreSavedPaidLicenseAsync()
    {
        if(string.IsNullOrEmpty(_storedPaidCode)||_paidLicense is null||_licenseGate is null)return;
        ConnectButton.IsEnabled=TrialButton.IsEnabled=ActivateLicenseButton.IsEnabled=false;
        PaidLicenseEntry.Visibility=Visibility.Collapsed;
        LicenseNotice.Text="جار استعادة الترخيص المحفوظ...";
        try
        {
            var result=await _paidLicense.VerifyAsync(_storedPaidCode);
            _licenseGate.SetPaid(result);
            if(result.Permitted)ShowActivePaidLicense(result);
            else
            {
                LicenseNotice.Text=result.Message;
                PaidLicenseEntry.Visibility=Visibility.Visible;
                RenewLicenseButton.Visibility=Visibility.Visible;
                TrialButton.IsEnabled=_localTrial?.Check().Mode is "ready" or "trial";
            }
        }
        catch(Exception)
        {
            LicenseNotice.Text="تعذر استعادة الترخيص المحفوظ. تحقق من صلاحيات Windows ثم أعد الاتصال.";
            PaidLicenseEntry.Visibility=Visibility.Visible;
        }
        finally {ConnectButton.IsEnabled=ActivateLicenseButton.IsEnabled=true;}
    }

    private void ShowActivePaidLicense(LicenseCheck current)
    {
        LicenseNotice.Text=current.Message;
        LicenseNotice.Foreground=new SolidColorBrush(Color.FromRgb(154,235,189));
        TrialButton.Visibility=Visibility.Collapsed;
        PaidLicenseEntry.Visibility=Visibility.Collapsed;
        RenewLicenseButton.Visibility=Visibility.Visible;
        LicenseCodeInput.Clear();
    }

    private void RenewLicense_Click(object sender,RoutedEventArgs e)
    {
        PaidLicenseEntry.Visibility=Visibility.Visible;
        LicenseNotice.Text="للتجديد، حدّث صلاحية الرمز في مركز تراخيص حضرموت "+
            "ثم اضغط تحقق وتفعيل؛ أو أدخل رمزا جديدا صادرًا من المنصة.";
        LicenseNotice.Foreground=new SolidColorBrush(Color.FromRgb(255,224,166));
        LicenseCodeInput.Focus();
    }

    private void UpdateLicenseCountdown()
    {
        var status=_lastLicenseStatus;
        if(status is null || _gateway is null)return;
        var remaining=status.ExpiresAtUtc is DateTimeOffset expiry
            ?Math.Max(0,(long)Math.Ceiling((expiry-DateTimeOffset.UtcNow).TotalSeconds))
            :status.RemainingSeconds;
        if(status.Mode=="trial")
        {
            if(remaining<0)remaining=0;
            var span=TimeSpan.FromSeconds(remaining);
            SidebarLicense.Text="التجربة المجانية · المتبقي\n"+
                ((int)span.TotalDays).ToString("00")+":"+
                span.Hours.ToString("00")+":"+span.Minutes.ToString("00")+":"+
                span.Seconds.ToString("00");
        }
        else if(status.Mode=="active")
        {
            if(status.ExpiresAtUtc is null)
                SidebarLicense.Text="الترخيص المدفوع · فعال (بدون تاريخ انتهاء محدد)";
            else
            {
                var span=TimeSpan.FromSeconds(Math.Max(0,remaining));
                SidebarLicense.Text="الترخيص المدفوع · المتبقي\n"+
                    ((int)span.TotalDays).ToString("00")+":"+
                    span.Hours.ToString("00")+":"+span.Minutes.ToString("00")+":"+
                    span.Seconds.ToString("00");
            }
        }
        else SidebarLicense.Text=status.Message;
    }

    private async void MonitorLicense_Tick(object? sender,EventArgs e)
    {
        if(_monitorBusy || _gateway is null || _licenseGate is null)return;
        _monitorBusy=true;
        try
        {
            if(_activeLicenseMode=="trial" && _localTrial is not null)
            {
                var trial=_localTrial.Check();
                _lastLicenseStatus=trial;
                UpdateLicenseCountdown();
                if(trial.Mode!="trial")
                {
                    await LogoutAsync();
                    LoginStatus.Text=TrialNotice(trial);
                    LicenseNotice.Text=TrialNotice(trial);
                    TrialButton.IsEnabled=false;
                }
            }
            else if(_activeLicenseMode=="active" && _paidLicense is not null &&
                (DateTimeOffset.UtcNow>=_nextPaidVerifyUtc ||
                 _lastLicenseStatus?.ExpiresAtUtc is DateTimeOffset expires &&
                 DateTimeOffset.UtcNow>=expires))
            {
                _nextPaidVerifyUtc=DateTimeOffset.UtcNow.AddMinutes(30);
                var check=await _paidLicense.VerifyAsync(_storedPaidCode);
                _licenseGate.SetPaid(check);
                _lastLicenseStatus=check;
                UpdateLicenseCountdown();
                if(!check.Permitted)
                {
                    await LogoutAsync();
                    LoginStatus.Text=check.Message;
                    LicenseNotice.Text=check.Message;
                    PaidLicenseEntry.Visibility=Visibility.Visible;
                }
            }
        }
        catch(Exception)
        {
            await LogoutAsync();
            LicenseNotice.Text="توقف التحقق من الترخيص؛ تحقق من وقت Windows والإنترنت.";
        }
        finally{_monitorBusy=false;}
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnecting) return;
        _isConnecting = true;
        ConnectButton.IsEnabled = false;
        LoginStatus.Text = "جارٍ التحقق من الترخيص ثم الاتصال بـ MikroTik...";
        try
        {
            if(_licenseGate is null || _localTrial is null || _paidLicense is null)
                throw new InvalidOperationException("نظام الترخيص غير جاهز؛ أعد فتح التطبيق.");
            if(!string.IsNullOrEmpty(_storedPaidCode))
            {
                var result=await _paidLicense.VerifyAsync(_storedPaidCode);
                _licenseGate.SetPaid(result);
                LicenseNotice.Text=result.Message;
                if(result.Permitted)ShowActivePaidLicense(result);
            }
            var allowed=_licenseGate.Preview();
            if(!(allowed.Permitted || allowed.Mode=="ready" && _licenseGate.TrialSelected))
                throw new InvalidOperationException(allowed.Message);

            if (!int.TryParse(PortInput.Text.Trim(), out int port))
                throw new ArgumentException("منفذ API غير صالح.");
            var endpoint = new RouterEndpoint(HostInput.Text.Trim(), port,
                UserInput.Text.Trim(), CurrentPassword, TlsInput.IsChecked == true);
            if(ActiveConnectionMode=="remote" && string.IsNullOrWhiteSpace(endpoint.Host))
                throw new ArgumentException("أدخل عنوان الراوتر عبر اتصال VPN القائم أولا.");
            var authenticated = await RouterOsClient.ConnectAsync(endpoint);
            // All pages share one recoverable session. Slow User Manager scans
            // have their own lane; no writes are replayed after connection loss.
            var session = new ResilientRouterSession(authenticated,
                async token => await RouterOsClient.ConnectAsync(endpoint,token));
            var gateway = new RouterGateway(session);
            try
            {
                await gateway.ReadResourceAsync();
                if(_closed)throw new OperationCanceledException();
            }
            catch { await session.DisposeAsync(); throw; }
            LicenseCheck entitlement;
            try
            {
                // No trial state is written before a verified RouterOS API session.
                entitlement=_licenseGate.OnSuccessfulRouterLogin();
                if(!entitlement.Permitted)
                    throw new InvalidOperationException(entitlement.Message);
            }
            catch { await session.DisposeAsync(); throw; }
            _session = session;
            _gateway = gateway;
            _activeLicenseMode=entitlement.Mode;
            _lastLicenseStatus=entitlement;
            UpdateLicenseCountdown();
            _nextPaidVerifyUtc=DateTimeOffset.UtcNow.AddMinutes(30);
            LicenseNotice.Text=entitlement.Message;
            if(entitlement.Mode=="trial")
                TrialButton.Content="متابعة التجربة المجانية";
            _licenseMonitor ??= new DispatcherTimer
            {
                Interval=TimeSpan.FromMinutes(1)
            };
            _licenseMonitor.Tick-=MonitorLicense_Tick;
            _licenseMonitor.Tick+=MonitorLicense_Tick;
            _licenseMonitor.Start();
            _sidebarCountdown ??= new DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};
            _sidebarCountdown.Tick-=SidebarCountdown_Tick;
            _sidebarCountdown.Tick+=SidebarCountdown_Tick;
            _sidebarCountdown.Start();
            if(SaveConnectionInput.IsChecked==true)
            {
                try
                {
                    _savedConnections.Save(new SavedRouterConnection(
                        endpoint.Host,endpoint.Port,endpoint.Username,endpoint.UseTls,
                        ActiveConnectionMode,endpoint.Password),
                        SavePasswordInput.IsChecked==true);
                    RefreshSavedConnections();
                }
                catch(Exception ex)
                {
                    // Saving connection history is optional; never block an authenticated
                    // RouterOS session because the local profile file is unavailable.
                    // Connection is active; optional saved connection failures are non-fatal.
                    LoginStatus.Text="لم تحفظ بيانات الاتصال محليا: "+ex.Message;
                }
            }
            PasswordInput.Clear();
            VisiblePasswordInput.Clear();
            LoginLayer.Visibility = Visibility.Collapsed;
            WorkspaceLayer.Visibility = Visibility.Visible;
            NavList.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            LoginStatus.Text = "تعذّر الاتصال: " + ex.Message;
        }
        finally { _isConnecting = false; ConnectButton.IsEnabled = true; }
    }

    private void SidebarCountdown_Tick(object? sender,EventArgs e) =>
        UpdateLicenseCountdown();

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_gateway is null || NavList.SelectedItem is not NavEntry page) return;
        bool isImmersive=page.Key is "dashboard" or "generator";
        // Both compact pages draw their own shared title frame.
        WorkspaceHeaderRow.Height=isImmersive ? new GridLength(0) : new GridLength(88);
        PageHost.Margin=isImmersive ? new Thickness(12,7,12,7)
                        : new Thickness(12,10,12,10);
        PageTitle.Text = page.Title;
        PageSubtitle.Text = page.Subtitle;
        PageHost.Content = page.Key switch
        {
            "dashboard" => _dashboardPage ??= new DashboardView(_gateway, _store, NavigateTo, HostInput.Text.Trim()),
            "generator" => new VoucherWorkspaceView(_gateway, _store, HostInput.Text.Trim()+":"+PortInput.Text.Trim(),
                _pendingVoucherService,_pendingVoucherProfile),
            "cards" => new CardsView(_gateway,_store),
            "profiles" => new ProfilesView(_gateway, NavigateToVoucherPackage, _store),
            "sessions" => new SessionsView(_gateway),
            "servers" => new ServersView(_gateway),
            "reports" => new ReportsView(_store),
            "activity" => new ActivityView(_store),
            "tools" => new NetworkMaintenanceView(_gateway, HostInput.Text.Trim()),
            "settings" => new SettingsView(_store),
            _ => null
        };
    }

    private void NavigateToVoucherPackage(string service,string profile)
    {
        if(service is not ("hotspot" or "userman" or "pppoe")
            || string.IsNullOrWhiteSpace(profile))
            return;
        _pendingVoucherService=service;
        _pendingVoucherProfile=profile;
        try { NavigateTo("generator"); }
        finally
        {
            // The destination constructor already received the selection.
            _pendingVoucherService=null;
            _pendingVoucherProfile=null;
        }
    }

    private void NavigateTo(string key)
    {
        var entry = _pages.FirstOrDefault(x => x.Key == key);
        if (entry is not null) NavList.SelectedItem = entry;
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        await LogoutAsync();
    }

    private async Task LogoutAsync()
    {
        _licenseMonitor?.Stop();
        _sidebarCountdown?.Stop();
        _lastLicenseStatus=null;
        _dashboardPage=null;
        _activeLicenseMode="";
        NavList.SelectedIndex = -1;
        PageHost.Content = null;
        WorkspaceLayer.Visibility = Visibility.Collapsed;
        LoginLayer.Visibility = Visibility.Visible;
        LoginStatus.Text = "تم قطع الاتصال بالراوتر.";
        SidebarLicense.Text="الترخيص والتجربة متاحان من شاشة الدخول.";
        _gateway = null;
        var previous = _session;
        _session = null;
        if (previous is not null) await previous.DisposeAsync();
    }

    protected override async void OnClosed(EventArgs e)
    {
        _closed=true;
        _licenseMonitor?.Stop();
        _sidebarCountdown?.Stop();
        var session=_session;
        _session=null;
        _gateway=null;
        var disposal=session?.DisposeAsync();
        _licenseHttp.Dispose();
        _store.Dispose();
        base.OnClosed(e);
        if(disposal is { } pending)await pending;
    }
}
