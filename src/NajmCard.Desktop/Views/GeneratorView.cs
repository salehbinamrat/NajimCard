using System.Windows.Controls.Primitives;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

public partial class GeneratorView : UserControl
{
    private const string DefaultTemplate="NajmCard-Hadramout-Compact";
    private readonly RouterGateway _gateway;
    private readonly VoucherStore _store;
    private readonly string _routerKey;
    private bool _restoringPreset;
    private Grid? _embeddedPrintHost;
    public void AttachPrintingSection(UIElement printing)
    {
        if(_embeddedPrintHost is null)throw new InvalidOperationException("صفحة الطباعة غير مدمجة.");
        _embeddedPrintHost.Children.Add(printing);
    }
    public event Action<CardPrintOptions>? PackagePrintSettingsRestored;
    public event Action? BatchChanged;
    internal event Action? CardAppearanceChanged;
    public event Action? ServiceChanged;
    public Func<CardPrintOptions>? PrintOptionsProvider;
    public Action<CardPrintOptions>? ApplyPrintOptions;
    public Func<VoucherOutputPreferences>? OutputPreferencesProvider;
    public Action<VoucherOutputPreferences>? ApplyOutputPreferences;
    public string CurrentService => SelectedService;
    public string LastOperationMessage => _status.Text;
    // Embedded page hosts this same save action beside the workspace title.
    internal Button PackageSaveButton { get; private set; } = null!;
    public VoucherCardAppearance CurrentAppearance=>_currentAppearance with {ShowPassword=ShowPreviewPassword};
    public bool HasBatch => _batch.Count>0;
    public IReadOnlyList<Voucher> ConfirmedBatch()
    {
        if(_batch.Count==0)return [];
        var service=_batch[0].Service;
        return _store.GetBatchVouchers(service,_batch[0].BatchId)
            .Where(x=>x.DeliveryStatus=="SENT" && x.SentToRouter).ToArray();
    }
    public void SelectService(string service)
    {
        if(_sending||_preparing)return;
        _service.SelectedIndex=service switch {"userman"=>1,"pppoe"=>2,_=>0};
    }
    private readonly ComboBox _service = new() { MinWidth = 200 };
    private readonly ComboBox _server = new() { MinWidth = 100 };
    private readonly ComboBox _credentialMode = new()
    {
        ItemsSource=new[]{"اسم مستخدم فقط","اسم مستخدم وكلمة مرور"},
        SelectedIndex=0
    };
    private readonly RadioButton _credentialsSame = new()
    {Content=new TextBlock{Text="متطابقين",Foreground=PageUi.Muted},GroupName="NajmPasswordRule",IsChecked=true};
    private readonly RadioButton _credentialsDifferent = new()
    {Content=new TextBlock{Text="مختلفين",Foreground=PageUi.Muted},GroupName="NajmPasswordRule"};
    private string _templateImagePath = "";
    private readonly ComboBox _profile = new() { MinWidth = 160 };
    private readonly ComboBox _customer = new() { MinWidth = 165 };
    private readonly StackPanel _customerPanel = PageUi.Column();
    private readonly TextBox _count = PageUi.Input("60");
    private readonly TextBox _pagesEstimate = PageUi.Input("1");
    private readonly TextBox _prefix = PageUi.Input("");
    private readonly TextBox _length = PageUi.Input("8");
    private readonly TextBox _usernameCharacters = PageUi.Input(VoucherGenerator.LowercaseMixed);
    private readonly TextBox _passwordCharacters = PageUi.Input(VoucherGenerator.LowercaseMixed);
    private static readonly string[] AlphabetChoices=
        ["أرقام فقط","أحرف صغيرة فقط","مختلطة صغيرة","أحرف كبيرة فقط","مختلطة كبيرة","مخصصة"];
    private readonly ComboBox _usernameAlphabetType=new()
    {
        ItemsSource=AlphabetChoices,
        SelectedIndex=2
    };
    private readonly ComboBox _passwordAlphabetType=new()
    {
        ItemsSource=AlphabetChoices,
        SelectedIndex=2
    };
    private readonly TextBox _passwordLength = PageUi.Input("6");
    private readonly TextBlock _cardPreview = PageUi.Status();
    private Border? _appearancePreviewBorder;
    private VoucherCardAppearance _currentAppearance = new(NetworkTitle:"نجم نت");
    private readonly TextBox _designTitle = PageUi.Input("نجم نت");
    private readonly TextBox _designFontSize = PageUi.Input("18");
    private readonly TextBox _designBackground = PageUi.Input("#FFF6EB");
    private readonly TextBox _designElementColor = PageUi.Input("#4C1730");
    private readonly TextBox _designTextColor = PageUi.Input("#4C1730");
    private readonly TextBox _designBorderColor = PageUi.Input("#B89159");
    private readonly TextBox _designBorderWidth = PageUi.Input("1");
    private readonly ComboBox _designFontFamily = new()
    {
        ItemsSource = new[] {"Arial", "Tahoma", "Segoe UI", "Calibri"},
        SelectedIndex=0
    };
    private readonly CheckBox _designShowPassword = new()
    {
        Content="إظهار كلمة المرور على الكرت",IsChecked=true
    };
    private readonly CheckBox _designShowProfile = new()
    {
        Content="إظهار اسم الباقة على الكرت",IsChecked=true
    };

    // The reference artwork includes independent, draggable optional card fields.
    // Capacity and phone come from operator-entered template data, not guessed RouterOS values.
    private readonly CheckBox _designShowCapacity = new() {Content="السعة"};
    private readonly CheckBox _designShowPhone = new() {Content="الهاتف"};
    private readonly CheckBox _designShowQr = new() {Content="QR"};
    private readonly CheckBox _designShowTime = new() {Content="الوقت"};
    private readonly CheckBox _designShowDate = new() {Content="التاريخ"};
    private readonly CheckBox _designShowTitle = new() {Content="العنوان",IsChecked=true};
    private readonly CheckBox _designShowSequence = new() {Content="التسلسل"};
    private readonly CheckBox _designShowRoute = new() {Content="المسار"};
    private readonly TextBox _designCapacityText = PageUi.Input("");
    private readonly TextBox _designPhoneText = PageUi.Input("");
    private readonly TextBox _designQrSize = PageUi.Input("24");
    private readonly Button _generateButton = PageUi.Button("إنشاء وحفظ الدفعة");
    private readonly Button _previewButton = PageUi.Button("معاينة قبل الإنشاء", false);
    private readonly CheckBox _lengthWithPrefix = new()
    {
        Content = "طول رقم الكرت يشمل البادئة",
        IsChecked = true,
        Margin = new Thickness(0,3,0,2)
    };
    private bool _preparing;
    private int _profileRequestVersion;
    private readonly CheckBox _samePassword = new()
    {
        Content = "استخدام رقم الكرت كلمة مرور أيضاً",
        IsChecked = true,
        Margin = new Thickness(0, 3, 0, 2)
    };
    private readonly DataGrid _table = PageUi.Table(
        ("الرقم", "Username"), ("الخدمة", "Service"), ("الباقة", "Profile"),
        ("الدفعة", "BatchId"), ("حالة الإرسال", "DeliveryStatus"));
    private readonly TextBlock _status = PageUi.Status();
    private readonly Button _sendButton = PageUi.Button("إرسال الدفعة المحفوظة إلى الراوتر", false);
    private IReadOnlyList<UserManagerV6Profile> _umProfiles = [];
    private IReadOnlyList<Voucher> _batch = [];
    private bool _sending;

    private string SelectedService => _service.SelectedIndex switch
    {
        1 => "userman",
        2 => "pppoe",
        _ => "hotspot"
    };
    public GeneratorView(RouterGateway gateway, VoucherStore store, string routerKey = "", bool embedded=false)
    {
        _gateway = gateway;
        _store = store;
        _routerKey=string.IsNullOrWhiteSpace(routerKey)?"default:8728":routerKey.Trim();
        _service.Items.Add("Hotspot");
        _service.Items.Add("User Manager v6");
        _service.Items.Add("PPPoE");
        _service.SelectedIndex = 1; // User Manager v6 is the initial service; users can switch freely.
        var form = PageUi.Column();
        var servicePanel=PageUi.Column();
        // Keep Hadramout's direct per-package save action visible beside
        // the routing header. It persists locally without sending RouterOS commands.
        var routeHeader=new Grid{FlowDirection=FlowDirection.RightToLeft};
        routeHeader.Children.Add(ReferenceHeader("باقة وسيرفر الإرسال"));
        var quickSaveButton=PageUi.Button("حفظ إعدادات الباقة",false);
        quickSaveButton.HorizontalAlignment=HorizontalAlignment.Right;
        quickSaveButton.VerticalAlignment=VerticalAlignment.Center;
        quickSaveButton.Margin=new Thickness(5,1,5,4);
        quickSaveButton.Padding=new Thickness(14,6,14,6);
        quickSaveButton.MinHeight=34;
        quickSaveButton.FontSize=13;
        quickSaveButton.FontWeight=FontWeights.Bold;
        quickSaveButton.Foreground=(Brush)new BrushConverter().ConvertFrom("#56172F")!;
        quickSaveButton.Background=(Brush)new BrushConverter().ConvertFrom("#F0C96E")!;
        quickSaveButton.ToolTip="حفظ خيارات التوليد والتصميم والطباعة لهذه الباقة في قاعدة نجم كارد المحلية.";
        WirePackageSaveNotification(quickSaveButton);
        PackageSaveButton=quickSaveButton;
        servicePanel.Children.Add(routeHeader);
        var routeInputs=new UniformGrid{Columns=2,Margin=new Thickness(0,1,0,1)};
        var serverBox=PageUi.Column();
        serverBox.Children.Add(PageUi.Label("السيرفر"));
        serverBox.Children.Add(_server);
        var profileBox=PageUi.Column();
        profileBox.Children.Add(PageUi.Label("الباقة من الراوتر"));
        profileBox.Children.Add(_profile);
        routeInputs.Children.Add(serverBox);
        routeInputs.Children.Add(profileBox);
        quickSaveButton.MinWidth=126;
        quickSaveButton.MinHeight=27;
        quickSaveButton.Padding=new Thickness(12,3,12,3);
        quickSaveButton.Margin=new Thickness(0);
        quickSaveButton.Content=new TextBlock
        {
            Text="حفظ إعدادات الباقة",Foreground=(Brush)new BrushConverter()
                .ConvertFrom("#52152F")!,FontFamily=new FontFamily("Arial"),
            FontWeight=FontWeights.ExtraBold,FontSize=12,
            TextAlignment=TextAlignment.Center
        };
        if(!embedded)routeHeader.Children.Add(quickSaveButton);
        servicePanel.Children.Add(routeInputs);
        servicePanel.Children.Add(_customerPanel);
        var catalogRow=new DockPanel{Margin=new Thickness(0,3,0,0)};
        var refreshCatalog=PageUi.Button("تحديث الباقات",false);
        refreshCatalog.MinWidth=0;
        refreshCatalog.MinHeight=22;
        refreshCatalog.FontSize=11;
        refreshCatalog.Padding=new Thickness(6,1,6,1);
        refreshCatalog.Margin=new Thickness(0,0,5,0);
        refreshCatalog.Click+=async (_,_) =>
        {
            if(_sending || _preparing)return; // Even failed/disabled dropdowns must allow retry.
            await LoadProfilesAsync();
        };
        DockPanel.SetDock(refreshCatalog,Dock.Right);
        catalogRow.Children.Add(refreshCatalog);
        _catalogStatus.Margin=new Thickness(2,1,2,1);
        _catalogStatus.FontSize=11;
        _catalogStatus.Text="بانتظار قراءة الباقات";
        _catalogStatus.TextTrimming=TextTrimming.CharacterEllipsis;
        _catalogStatus.TextWrapping=TextWrapping.NoWrap;
        _catalogStatus.VerticalAlignment=VerticalAlignment.Center;
        _catalogStatus.SetBinding(ToolTipProperty,new System.Windows.Data.Binding(nameof(TextBlock.Text))
        {
            Source=_catalogStatus
        });
        catalogRow.Children.Add(_catalogStatus);
        servicePanel.Children.Add(catalogRow);
        if(!embedded)
        {
            servicePanel.Children.Add(PageUi.Label("خدمة الكروت"));
            servicePanel.Children.Add(_service);
        }
        var routeSurface=embedded?CompactSurface(servicePanel,"#D2AA61"):
            PageUi.Surface(servicePanel,"#D2AA61");
        form.Children.Add(routeSurface);
        // Same five aligned generation fields as the supplied screen;
        // page count is computed from actual rows/columns, never manually faked.
        _pagesEstimate.IsReadOnly=false;
        _pagesEstimate.IsTabStop=true;
        _pagesEstimate.ToolTip="عدل الصفحات لتوليد شبكة كاملة لكل صفحة، أو عدل عدد الكروت لحساب الصفحات المطلوبة.";
        var row = new UniformGrid { Columns = 5 };
        foreach (var (label, input) in new (string, Control)[]
        {
            ("طول كلمة المرور", _passwordLength), ("طول المستخدم", _length),
            ("بادئة الرقم", _prefix), ("عدد الصفحات", _pagesEstimate),
            ("عدد الكروت", _count)
        })
        {
            var cell = PageUi.Column();
            cell.Margin = new Thickness(2,0,2,0);
            var caption=PageUi.Label(label);
            caption.FontSize=11;
            cell.Children.Add(caption);
            if(ReferenceEquals(input,_length) || ReferenceEquals(input,_passwordLength))
                cell.Children.Add(PageUi.CompactNumberChoice((TextBox)input,
                    "4","5","6","7","8","9","10","11","12","13","14","15","16"));
            else cell.Children.Add(input);
            row.Children.Add(cell);
        }
        // Hadramout's username and password alphabets are independent.
        // The user may type exact ASCII character sets, without duplicate characters.
        var formatSection=PageUi.Column();
        formatSection.Children.Add(ReferenceHeader("اسم المستخدم وكلمة المرور"));
        var credentialsTop=new Grid{Margin=new Thickness(0,1,0,1)};
        credentialsTop.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        credentialsTop.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var modeCell=PageUi.Column();
        modeCell.Margin=new Thickness(1,0,2,0);
        modeCell.Children.Add(_credentialMode);
        Grid.SetColumn(modeCell,0);
        credentialsTop.Children.Add(modeCell);
        var sameCell=PageUi.Column();
        sameCell.Children.Add(_credentialsSame);
        var differentCell=PageUi.Column();
        differentCell.Children.Add(_credentialsDifferent);
        var passwordModeRow=new UniformGrid{Columns=2,Margin=new Thickness(0)};
        passwordModeRow.Children.Add(sameCell);
        passwordModeRow.Children.Add(differentCell);
        var basicsSection=PageUi.Column();
        basicsSection.Children.Add(ReferenceHeader("بيانات التوليد الأساسية"));
        row.Margin=new Thickness(0,1,0,2);
        basicsSection.Children.Add(row);
        var basicsSurface=embedded?CompactSurface(basicsSection,"#D2AA61"):
            PageUi.Surface(basicsSection,"#D2AA61");
        // Each service's alphabet TYPE and character SET occupy one horizontal row.
        // This removes the old stacked rows without hiding any generation option.
        var usernameGroup=new UniformGrid{Columns=2,Margin=new Thickness(1,0,1,0)};
        var alphabetFields=new UniformGrid{Columns=2};
        var userKind=PageUi.Column();
        userKind.Children.Add(PageUi.Label("نوع اسم المستخدم"));
        userKind.Children.Add(_usernameAlphabetType);
        var usernameCharsCell=PageUi.Column();
        usernameCharsCell.Children.Add(PageUi.Label("أحرف اسم المستخدم"));
        usernameCharsCell.Children.Add(_usernameCharacters);
        Grid.SetColumn(passwordModeRow,1);
        credentialsTop.Children.Add(passwordModeRow);
        formatSection.Children.Add(credentialsTop);
        formatSection.Children.Add(usernameGroup);
        usernameGroup.Children.Add(userKind);
        alphabetFields.Children.Add(usernameCharsCell);
        var passwordKind=PageUi.Column();
        passwordKind.Children.Add(PageUi.Label("نوع كلمة المرور"));
        passwordKind.Children.Add(_passwordAlphabetType);
        var passwordCharsCell=PageUi.Column();
        passwordCharsCell.Children.Add(PageUi.Label("أحرف كلمة المرور"));
        passwordCharsCell.Children.Add(_passwordCharacters);
        usernameGroup.Children.Insert(0,passwordKind);
        alphabetFields.Children.Insert(0,passwordCharsCell);
        formatSection.Children.Add(alphabetFields);
        void ApplyAlphabet(ComboBox selector,TextBox target)
        {
            if(_restoringPreset)return;
            switch(selector.SelectedIndex)
            {
                case 0:target.Text=VoucherGenerator.Digits;break;
                case 1:target.Text=VoucherGenerator.LowercaseLetters;break;
                case 2:target.Text=VoucherGenerator.LowercaseMixed;break;
                case 3:target.Text=VoucherGenerator.UppercaseLetters;break;
                case 4:target.Text=VoucherGenerator.Mixed;break;
                // Custom preserves existing characters for exact manual editing.
                default:break;
            }
        }
        _usernameAlphabetType.SelectionChanged+=(_,_) =>
            ApplyAlphabet(_usernameAlphabetType,_usernameCharacters);
        _passwordAlphabetType.SelectionChanged+=(_,_) =>
            ApplyAlphabet(_passwordAlphabetType,_passwordCharacters);
        formatSection.Children.Add(PageUi.LabeledCheckBox(_lengthWithPrefix));
        var formatSurface=embedded?CompactSurface(formatSection,"#D2AA61"):
            PageUi.Surface(formatSection,"#D2AA61");
        form.Children.Add(formatSurface);
        _customerPanel.Children.Add(PageUi.Label("مدير المستخدمين (السيرفر 6)"));
        _customerPanel.Children.Add(_customer);
        _customerPanel.Visibility = SelectedService=="userman"?Visibility.Visible:Visibility.Collapsed;
        _server.ItemsSource=new[]{"all"};
        _server.SelectedIndex=0;
        void UpdatePasswordMode()
        {
            bool userOnly=_credentialMode.SelectedIndex==0;
            passwordModeRow.IsEnabled=!userOnly;
            // Keep password matching as a separate preference. Username-only
            // disables the controls, but must never turn into same-password.
            _samePassword.IsChecked=!userOnly && _credentialsSame.IsChecked==true;
            bool hasSeparatePassword=!userOnly && _credentialsDifferent.IsChecked==true;
            _passwordLength.IsEnabled=hasSeparatePassword;
            passwordKind.IsEnabled=passwordCharsCell.IsEnabled=hasSeparatePassword;
            _designShowPassword.IsChecked=!userOnly;
        }
        void CredentialChoiceChanged()
        {
            UpdatePasswordMode();
            InvalidatePendingBatchForCredentialChange();
        }
        _credentialMode.SelectionChanged+=(_,_)=>CredentialChoiceChanged();
        _credentialsSame.Checked+=(_,_)=>CredentialChoiceChanged();
        _credentialsDifferent.Checked+=(_,_)=>CredentialChoiceChanged();
        UpdatePasswordMode();
        // Exact reference arrangement: wide graphic preview, narrow appearance
        // settings, compact header tools; editing is a separate window.
        var designSurface=BuildDesignerPanel(embedded);
        form.Children.Add(basicsSurface);
        form.Children.Add(designSurface);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 13, 0, 5)
        };
        _generateButton.Click += async (_, _) => await PrepareBatchAsync(true);
        _previewButton.Click += async (_, _) => await PrepareBatchAsync(false);
        _sendButton.IsEnabled = false;
        _sendButton.Click += async (_, _) => await UploadAsync();
        actions.Children.Add(_previewButton);
        actions.Children.Add(_generateButton);
        actions.Children.Add(_sendButton);
        actions.Visibility=embedded?Visibility.Collapsed:Visibility.Visible;
        form.Children.Add(actions);
        form.Children.Add(_status);
        // Compact sample data overlays the vector card; title and package
        // are rendered separately, not duplicated inside the username box.
        _cardPreview.Name="CardNumberPreview";
        _cardPreview.Text="";
        _cardPreview.ToolTip="قالب افتراضي تجريبي؛ ليس كرتا محفوظا أو مرسلا.";
        _cardPreview.FontSize = embedded?17:16;
        _cardPreview.FontWeight = FontWeights.SemiBold;
        _cardPreview.Foreground = PageUi.Gold;
        _cardPreview.TextAlignment = TextAlignment.Center;
        _cardPreview.FlowDirection = FlowDirection.LeftToRight;
        _appearancePreviewBorder=embedded?CompactSurface(BuildSampleVoucherVisual(),"#A47B55")
            :PageUi.Surface(_cardPreview,"#A47B55");
        _appearancePreviewBorder.ToolTip="القالب الافتراضي: "+DefaultTemplate;
        Grid.SetColumn(_appearancePreviewBorder,0);
        // A fixed 465px graphic alone overflowed the preview column on
        // ordinary 1366px displays. Scale its artwork but not surrounding UI.
        _appearancePreviewBorder.Margin=new Thickness(0);
        var scalableCard=new Viewbox{
            MaxHeight=153,
            Stretch=Stretch.Uniform,
            StretchDirection=StretchDirection.Both,
            HorizontalAlignment=HorizontalAlignment.Stretch,
            VerticalAlignment=VerticalAlignment.Center,
            Margin=new Thickness(0),
            Child=_appearancePreviewBorder
        };
        Grid.SetColumn(scalableCard,0);
        _designPreviewRow.Children.Insert(0,scalableCard);
        if(embedded)
        {
            // The parent Grid constrains the two aligned rows; no fixed-width
            // Viewbox, stray side gutters, or overflow-hidden StackPanel.
            form.Children.Clear();
            Content=CreateReferenceLayout(
                routeSurface,formatSurface,basicsSurface,designSurface);
        }
        else
        {
            var regular=PageUi.Column();
            regular.Children.Add(PageUi.Surface(form,"#C99E65"));
            regular.Children.Add(PageUi.Surface(_table,"#8266B1"));
            Content=PageUi.Wrap("إنشاء وطباعة كروت الشبكة",
                "Hotspot / User Manager v6 / PPPoE",regular);
        }
        FlowDirection = FlowDirection.RightToLeft;
        _count.TextChanged+=(_,_)=>{
            if(_syncingPageCount)return;
            _quantityIsDerivedFromSheets=false;
            RefreshPageCount();
        };
        _pagesEstimate.TextChanged+=(_,_)=>{
            if(_syncingPageCount)return;
            _quantityIsDerivedFromSheets=true;
            RefreshPageCount();
        };
        RefreshPageCount();
        try
        {
            var savedStyle=_store.LoadCardAppearance();
            LoadCardAppearanceFields(savedStyle);
        }
        catch(Exception ex)
        {
            _designStatus.Text="تعذر قراءة المظهر المحفوظ؛ لم تُعدل البيانات: "+ex.Message;
        }
        _service.SelectionChanged += async (_, _) =>
        {
            _batch = [];
            BatchChanged?.Invoke();
            ServiceChanged?.Invoke();
            _table.ItemsSource = null;
            _sendButton.IsEnabled = false;
            SetPreviewCredentials("");
            _profile.ItemsSource = null;
            _customer.ItemsSource = null;
            _server.ItemsSource = new[]{"all"};
            _server.SelectedIndex=0;
            var userManagerMode=SelectedService=="userman";
            _customerPanel.Visibility=userManagerMode?Visibility.Visible:Visibility.Collapsed;

            if(IsLoaded)await LoadProfilesAsync();
        };
        _profile.SelectionChanged += (_, _) =>
        {
            if(_restoringPreset || _applyingProfileCatalog)return;
            _batch=[];
            BatchChanged?.Invoke();
            _applyingProfileCatalog=true;
            try{SelectMatchingOwner();}
            finally{_applyingProfileCatalog=false;}
            RestorePackagePreset();
        };
        _customer.SelectionChanged += (_,_) =>
        {
            if(_restoringPreset || _applyingProfileCatalog)return;
            // User Manager owners are separate profiles for issuing purposes.
            // Never leave a pending batch tied to another owner's credentials.
            _batch=[];
            BatchChanged?.Invoke();
            RestorePackagePreset();
        };
        SubscribeLiveCardPreview();
        SubscribeGenerationInputChanges();
        SubscribeLiveCardAppearance();
        Loaded += async (_, _) => await LoadProfilesAsync();
        Unloaded += (_,_)=>_profileRequestVersion++;
    }
    private VoucherCardAppearance ReadCurrentAppearance()
    {
        static double Number(string v)=>double.Parse(v.Trim().Replace(',','.'),
            CultureInfo.InvariantCulture);
        return (_currentAppearance with
        {
            NetworkTitle=_designTitle.Text.Trim(),
            FontFamily=_designFontFamily.SelectedItem?.ToString()??"Arial",
            UsernameFontSize=Number(_designFontSize.Text),
            BackgroundColor=_designBackground.Text.Trim(),
            UsernameColor=_designTextColor.Text.Trim(),
            ElementColor=_designElementColor.Text.Trim(),
            BorderColor=_designBorderColor.Text.Trim(),
            BorderWidth=Number(_designBorderWidth.Text),
            ShowPassword=_designShowPassword.IsChecked==true,
            ShowProfile=_designShowProfile.IsChecked==true,
            ShowCapacity=_designShowCapacity.IsChecked==true,
            ShowPhone=_designShowPhone.IsChecked==true,
            ShowQr=_designShowQr.IsChecked==true,
            ShowTime=_designShowTime.IsChecked==true,
            ShowDate=_designShowDate.IsChecked==true,
            ShowTitle=_designShowTitle.IsChecked==true,
            ShowSequence=_designShowSequence.IsChecked==true,
            ShowRoute=_designShowRoute.IsChecked==true,
            CapacityText=_designCapacityText.Text.Trim(),
            PhoneText=_designPhoneText.Text.Trim(),
            QrSize=Number(_designQrSize.Text),
            TemplateImagePath=_templateImagePath
        }).Validate();
    }
    internal VoucherCardAppearance ReadDraftPreviewAppearance() =>
        ReadCurrentAppearance() with {ShowPassword=ShowPreviewPassword};
    public async Task<bool> CreateAndSendAsync(bool singleCard=false,
        Func<IReadOnlyList<Voucher>,Task>? saveBeforeSend=null)
    {
        // Preset storage is independent from issuing: an invalid print layout
        // must not block the separately validated RouterOS card transaction.
        if(_sending||_preparing)return false;
        _batch=[];BatchChanged?.Invoke();
        ReportOperationProgress("التحقق من إعدادات الكروت...");
        await PrepareBatchAsync(true,singleCard);
        if(_batch.Count==0)return false;
        // A failed archive write must stop before any RouterOS write.
        if(saveBeforeSend is not null)await saveBeforeSend(_batch.ToArray());
        await UploadAsync();
        // A partially acknowledged batch is not a successful "send all"
        // operation. Preserve its durable status and do not announce success
        // or print an incomplete batch as if every voucher was delivered.
        return _batch.Count>0 && _batch.All(v=>
            v.SentToRouter && v.DeliveryStatus=="SENT");
    }

    /// <summary>
    /// All generation and DPAPI/SQLite bulk work runs off the WPF UI dispatcher.
    /// Preview is intentionally ephemeral: it never writes to the database or router.
    /// </summary>
    private async Task PrepareBatchAsync(bool persist,bool singleCard=false)
    {
        if(_sending || _preparing)return;
        _preparing = true;
        _service.IsEnabled = false;
        SetCredentialControlsBusy(true);
        _previewButton.IsEnabled = _generateButton.IsEnabled = _sendButton.IsEnabled = false;
        try
        {
            var options=ReadGenerationOptions(forPreview:false);
            if(singleCard)options=options with {Count=1};
            var service=SelectedService;
            var profile=_profile.SelectedItem?.ToString() ?? "";
            if(service=="hotspot" && _server.SelectedItem?.ToString()!="all")
                throw new InvalidOperationException(
                    "حدد السيرفر all قبل معاينة أو إصدار الكروت؛ "+
                    "الإرسال الموجه لسيرفر خاص لا يزال بحاجة لاختبارات RouterOS.");
            var customer=service=="userman"?_customer.SelectedItem?.ToString()??"":"";
            if(service=="userman" && string.IsNullOrWhiteSpace(customer))
                throw new ArgumentException("اختر مدير المستخدمين (السيرفر 6) قبل إنشاء الكروت.");
            _status.Text = persist
                ? "جارٍ توليد الدفعة وحفظها محليا بشكل آمن..."
                : "جارٍ إعداد عينة فعلية محلية دون حفظ أو إرسال...";
            // Hadramout stores the print template with the issuing batch, so
            // later changes to another package cannot alter older vouchers.
            // Invalid print controls do not block the independent send-only
            // path; those batches require explicit print settings before print.
            CardPrintOptions? capturedPrint=null;
            VoucherCardAppearance? capturedAppearance=null;
            VoucherOutputPreferences? capturedOutput=null;
            string printWarning="";
            if(persist)
            {
                try
                {
                    capturedPrint=PrintOptionsProvider?.Invoke()??new CardPrintOptions();
                    CardPrintLayoutPlanner.Calculate(capturedPrint,options.Count);
                    capturedAppearance=ReadDraftPreviewAppearance();
                    // Print artwork is optional for issuing. Reject a missing
                    // external template from the snapshot but allow the
                    // independently valid send-only RouterOS transaction.
                    if(!string.IsNullOrWhiteSpace(capturedAppearance.TemplateImagePath))
                    {
                        var picture=new FileInfo(capturedAppearance.TemplateImagePath);
                        if(!picture.Exists || picture.Length>15L*1024*1024 ||
                           picture.Length<=0)
                            throw new InvalidDataException(
                                "صورة القالب غير متاحة أو حجمها غير صالح؛ "+
                                "ستصدر الكروت دون اعتماد قالب طباعة مفقود.");
                    }
                    capturedOutput=(OutputPreferencesProvider?.Invoke()
                        ??new VoucherOutputPreferences()).Validate();
                }
                catch(Exception ex) when(ex is ArgumentException or FormatException
                    or OverflowException or InvalidOperationException or InvalidDataException
                    or UnauthorizedAccessException)
                {
                    capturedPrint=null;
                    printWarning=" | إعدادات الطباعة غير صالحة ولم تُحفظ مع الدفعة: "+
                        ex.Message+"؛ راجعها قبل طباعة الكروت.";
                }
            }
            ReportOperationProgress("توليد أرقام الكروت...");
            IProgress<string> saveStage=new Progress<string>(ReportOperationProgress);
            var candidates=await Task.Run(()=>
            {
                var reserved=_store.ReservedNames(service);
                var newCards=VoucherGenerator.Generate(options,reserved)
                    .Select(x=>x with {Service=service,Customer=customer}).ToArray();
                VoucherBatchPrintSnapshot? snapshot=null;
                if(persist && capturedPrint is not null && capturedAppearance is not null)
                    snapshot=new VoucherBatchPrintSnapshot(
                        service,newCards[0].BatchId,profile,customer,
                        capturedPrint,capturedAppearance,capturedOutput);
                if(persist)
                {
                    saveStage.Report("حفظ الكروت في قاعدة البيانات المحلية...");
                    _store.SaveBatch(newCards,snapshot);
                }
                return newCards;
            });
            _batch=persist?candidates:[];
            BatchChanged?.Invoke();
            _table.ItemsSource=candidates.Take(500).ToArray();
            _sendButton.IsEnabled=persist;
            var sample=candidates[0];
            SetPreviewCredentials(sample.Username,sample.Password);
            _status.Text=persist
                ? $"حُفظت {candidates.Length:N0} كرتا في قاعدة نجم كارد. لم تُرسل إلى الراوتر. "+ 
                    (candidates.Length>500?"الجدول يعرض أول 500 كرت لتسريع العرض.":"")
                    +printWarning
                : $"معاينة {candidates.Length:N0} كرتا؛ لم تُحفظ ولم تُرسل. "+
                    (candidates.Length>500?"الجدول يعرض أول 500 كرت.":"");
        }
        catch(Exception ex)
        {
            _batch=[];
            _table.ItemsSource=null;
            _sendButton.IsEnabled=false;
            SetPreviewCredentials("تعذر تجهيز المعاينة أو إنشاء الكروت.");
            _status.Text="فشل تجهيز الدفعة: "+ex.Message;
            BatchChanged?.Invoke();
        }
        finally
        {
            _preparing=false;
            _service.IsEnabled=true;
            SetCredentialControlsBusy(false);
            _previewButton.IsEnabled=_generateButton.IsEnabled=true;
        }
    }

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
        var savedMode = DescribeSavedCredentialMode(_batch);
        if (savedMode.Length == 0)
        {
            _status.Text = "تحتوي الدفعة على أنماط مختلفة من بيانات الدخول؛ أوقف الإرسال.";
            return;
        }
        if (!PageUi.Confirm("سيتم إرسال " + _batch.Count + " كرتاً إلى خدمة " +
            description + " على الراوتر. نوع بيانات الدخول المحفوظة: " +
            savedMode + ". هل تريد المتابعة؟"))
            return;

        _sending = true;
        _sendButton.IsEnabled = false;
        _service.IsEnabled = false;
        SetCredentialControlsBusy(true);
        try
        {
            var current = _batch.ToArray();
            var result = await new VoucherBatchDispatcher(_gateway, _store)
                .SendAsync(current, progress:new Progress<string>(ReportOperationProgress));
            if (result.UncertainOutcome)
            {
                _batch = await Task.Run(()=>_store.GetBatchVouchers(service, current[0].BatchId).ToArray());
                BatchChanged?.Invoke();
                _table.ItemsSource = _batch.Take(500).ToArray();
                var reason=_store.FirstDeliveryError(service,current[0].BatchId);
                _status.Text = "الاتصال أو رد الراوتر غير مؤكد. يُمنع إعادة إرسال كروت " +
                    "UNKNOWN/SENDING أو طباعتها قبل التحقق من الراوتر." +
                    (string.IsNullOrWhiteSpace(reason)?"":" السبب: "+reason);
            }
            else
            {
                var persisted = await Task.Run(()=>_store.GetBatchVouchers(service, current[0].BatchId).ToArray());
                _batch = persisted;
                BatchChanged?.Invoke();
                _table.ItemsSource = _batch.Take(500).ToArray();
                var reason=result.Failed.Count>0?
                    _store.FirstDeliveryError(service,current[0].BatchId):null;
                _status.Text = "تم إرسال " + result.Succeeded +
                    " كرتاً، وفشل " + result.Failed.Count +
                    ". جميع الحالات محفوظة محلياً." +
                    (string.IsNullOrWhiteSpace(reason)?"":" السبب: "+reason);
            }
        }
        catch (Exception ex)
        {
            _status.Text = "توقف الإرسال: " + ex.Message;
            // Earlier cards may already be committed even when a later
            // database/network operation throws. Never leave stale CREATED
            // objects in the UI or infer that the whole batch failed.
            try
            {
                var batchId=_batch[0].BatchId;
                _batch=await Task.Run(()=>_store.GetBatchVouchers(service,batchId).ToArray());
                _table.ItemsSource=_batch.Take(500).ToArray();
                BatchChanged?.Invoke();
                _status.Text+=" | حُدثت الحالات المحفوظة؛ راجع سجل الدفعات قبل أي إعادة إرسال.";
            }
            catch(Exception refreshError)
            {
                _status.Text+=" | تعذر تحديث الحالات: "+refreshError.Message+
                    "؛ راجع سجل الدفعات والراوتر قبل إعادة المحاولة.";
            }
        }
        finally
        {
            _sending = false;
            _sendButton.IsEnabled = _batch.Count > 0;
            _service.IsEnabled = true;
            SetCredentialControlsBusy(false);
        }
    }
}

