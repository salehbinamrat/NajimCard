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
    public event Action? ServiceChanged;
    public Func<CardPrintOptions>? PrintOptionsProvider;
    public Action<CardPrintOptions>? ApplyPrintOptions;
    public string CurrentService => SelectedService;
    public VoucherCardAppearance CurrentAppearance=>_currentAppearance;
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
    {Content="متطابقتين",GroupName="NajmPasswordRule",IsChecked=true};
    private readonly RadioButton _credentialsDifferent = new()
    {Content="مختلفتين",GroupName="NajmPasswordRule"};
    private string _templateImagePath = "";
    private readonly ComboBox _profile = new() { MinWidth = 160 };
    private readonly ComboBox _customer = new() { MinWidth = 165 };
    private readonly StackPanel _customerPanel = PageUi.Column();
    private readonly TextBox _count = PageUi.Input("20");
    private readonly TextBox _pagesEstimate = PageUi.Input("1");
    private readonly TextBox _prefix = PageUi.Input("");
    private readonly TextBox _length = PageUi.Input("8");
    private readonly TextBox _usernameCharacters = PageUi.Input(VoucherGenerator.Digits);
    private readonly TextBox _passwordCharacters = PageUi.Input(VoucherGenerator.Mixed);
    private readonly ComboBox _usernameAlphabetType=new()
    {
        ItemsSource=new[]{"أرقام فقط","أحرف فقط","مختلطة","مخصصة"},
        SelectedIndex=0
    };
    private readonly ComboBox _passwordAlphabetType=new()
    {
        ItemsSource=new[]{"أرقام فقط","أحرف فقط","مختلطة","مخصصة"},
        SelectedIndex=2
    };
    private readonly TextBox _passwordLength = PageUi.Input("6");
    private readonly TextBlock _cardPreview = PageUi.Status();
    private Border? _appearancePreviewBorder;
    private VoucherCardAppearance _currentAppearance = new();
    private readonly TextBox _designTitle = PageUi.Input("نجم كارد");
    private readonly TextBox _designFontSize = PageUi.Input("18");
    private readonly TextBox _designBackground = PageUi.Input("#FFF6EB");
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
        _service.SelectedIndex = 0;

        var form = PageUi.Column();
        var servicePanel=PageUi.Column();
        // Keep Hadramout's direct per-package save action visible beside
        // the routing header. It persists locally without sending RouterOS commands.
        var routeHeader=new Grid{FlowDirection=FlowDirection.RightToLeft};
        routeHeader.Children.Add(ReferenceHeader("بيانات ومسار الإرسال"));
        var quickSaveButton=PageUi.Button("حفظ إعدادات الباقة",false);
        quickSaveButton.HorizontalAlignment=HorizontalAlignment.Left;
        quickSaveButton.VerticalAlignment=VerticalAlignment.Center;
        quickSaveButton.Margin=new Thickness(5,1,5,4);
        quickSaveButton.Padding=new Thickness(9,2,9,2);
        quickSaveButton.MinHeight=26;
        quickSaveButton.FontSize=11;
        quickSaveButton.FontWeight=FontWeights.Bold;
        quickSaveButton.Foreground=Brushes.White;
        quickSaveButton.Background=(Brush)new BrushConverter().ConvertFrom("#8B122A")!;
        quickSaveButton.ToolTip="حفظ خيارات التوليد والتصميم والطباعة لهذه الباقة في قاعدة نجم كارد المحلية.";
        var packageSaveFeedback=PageUi.Status();
        packageSaveFeedback.FontSize=11;
        packageSaveFeedback.TextWrapping=TextWrapping.Wrap;
        packageSaveFeedback.Visibility=Visibility.Collapsed;
        quickSaveButton.Click+=(_,_)=>
        {
            var saved=SaveCurrentPackage();
            packageSaveFeedback.Text=_status.Text;
            packageSaveFeedback.Foreground=(Brush)new BrushConverter().ConvertFrom(
                saved?"#A5F3CD":"#FFC1BF")!;
            packageSaveFeedback.Visibility=Visibility.Visible;
        };
        routeHeader.Children.Add(quickSaveButton);
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
        servicePanel.Children.Add(routeInputs);
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
            ("عدد الكروت", _count), ("عدد الصفحات", _pagesEstimate),
            ("بادئة الرقم", _prefix), ("طول المستخدم", _length),
            ("طول كلمة المرور", _passwordLength)
        })
        {
            var cell = PageUi.Column();
            cell.Margin = new Thickness(2,0,2,0);
            var caption=PageUi.Label(label);
            caption.FontSize=11;
            cell.Children.Add(caption);
            cell.Children.Add(input);
            row.Children.Add(cell);
        }

        // Hadramout's username and password alphabets are independent.
        // The user may type exact ASCII character sets, without duplicate characters.
        var formatSection=PageUi.Column();
        formatSection.Children.Add(ReferenceHeader("إنشاء المستخدم وطباعة الكروت"));
        var credentialsTop=new Grid{Margin=new Thickness(0,1,0,1)};
        credentialsTop.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(2.2,GridUnitType.Star)});
        credentialsTop.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        credentialsTop.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        var modeCell=PageUi.Column();
        modeCell.Children.Add(PageUi.Label("طريقة الدخول"));
        modeCell.Children.Add(_credentialMode);
        Grid.SetColumn(modeCell,0);
        credentialsTop.Children.Add(modeCell);
        var sameCell=PageUi.Column();
        sameCell.Children.Add(PageUi.Label("كلمة المرور"));
        sameCell.Children.Add(_credentialsSame);
        Grid.SetColumn(sameCell,1);
        credentialsTop.Children.Add(sameCell);
        var differentCell=PageUi.Column();
        differentCell.Children.Add(PageUi.Label("خيار آخر"));
        differentCell.Children.Add(_credentialsDifferent);
        Grid.SetColumn(differentCell,2);
        credentialsTop.Children.Add(differentCell);
        formatSection.Children.Add(credentialsTop);
        var basicsSection=PageUi.Column();
        basicsSection.Children.Add(ReferenceHeader("بيانات التوليد الأساسية"));
        row.Margin=new Thickness(0,1,0,2);
        basicsSection.Children.Add(row);
        var basicsSurface=embedded?CompactSurface(basicsSection,"#D2AA61"):
            PageUi.Surface(basicsSection,"#D2AA61");
        var characterKinds=new UniformGrid{Columns=2,Margin=new Thickness(0,0,0,1)};
        var userKind=PageUi.Column();
        userKind.Children.Add(PageUi.Label("نوع اسم المستخدم"));
        userKind.Children.Add(_usernameAlphabetType);
        characterKinds.Children.Add(userKind);
        var passwordKind=PageUi.Column();
        passwordKind.Children.Add(PageUi.Label("نوع كلمة المرور"));
        passwordKind.Children.Add(_passwordAlphabetType);
        characterKinds.Children.Add(passwordKind);
        formatSection.Children.Add(characterKinds);
        var characterRow=new UniformGrid{Columns=2,Margin=new Thickness(0,0,0,1)};
        var usernameCharsCell=PageUi.Column();
        usernameCharsCell.Children.Add(PageUi.Label("أحرف اسم المستخدم"));
        usernameCharsCell.Children.Add(_usernameCharacters);
        characterRow.Children.Add(usernameCharsCell);
        var passwordCharsCell=PageUi.Column();
        passwordCharsCell.Children.Add(PageUi.Label("أحرف كلمة المرور"));
        passwordCharsCell.Children.Add(_passwordCharacters);
        characterRow.Children.Add(passwordCharsCell);
        formatSection.Children.Add(characterRow);
        void ApplyAlphabet(ComboBox selector,TextBox target)
        {
            if(_restoringPreset)return;
            switch(selector.SelectedIndex)
            {
                case 0:target.Text=VoucherGenerator.Digits;break;
                case 1:target.Text=VoucherGenerator.UppercaseLetters;break;
                case 2:target.Text=VoucherGenerator.Mixed;break;
                // Custom preserves existing characters for exact manual editing.
                default:break;
            }
        }
        _usernameAlphabetType.SelectionChanged+=(_,_) =>
            ApplyAlphabet(_usernameAlphabetType,_usernameCharacters);
        _passwordAlphabetType.SelectionChanged+=(_,_) =>
            ApplyAlphabet(_passwordAlphabetType,_passwordCharacters);
        formatSection.Children.Add(_lengthWithPrefix);
        var formatSurface=embedded?CompactSurface(formatSection,"#D2AA61"):
            PageUi.Surface(formatSection,"#D2AA61");
        form.Children.Add(formatSurface);

        _customerPanel.Children.Add(PageUi.Label("حساب مدير User Manager v6"));
        _customerPanel.Children.Add(_customer);
        _customerPanel.Visibility = Visibility.Collapsed;
        _server.ItemsSource=new[]{"all"};
        _server.SelectedIndex=0;
        servicePanel.Children.Add(_customerPanel);
        // Feedback appears only on explicit save, within the routing section.
        servicePanel.Children.Add(packageSaveFeedback);
        void UpdatePasswordMode()
        {
            bool userOnly=_credentialMode.SelectedIndex==0;
            // Collapse option captions as well as controls, to avoid
            // empty gutters when the username-only format is selected.
            sameCell.Visibility=differentCell.Visibility=
                userOnly?Visibility.Collapsed:Visibility.Visible;
            credentialsTop.ColumnDefinitions[0].Width=new GridLength(
                userOnly?1:2.2,GridUnitType.Star);
            credentialsTop.ColumnDefinitions[1].Width=new GridLength(
                userOnly?0:1,GridUnitType.Star);
            credentialsTop.ColumnDefinitions[2].Width=new GridLength(
                userOnly?0:1,GridUnitType.Star);
            _credentialsSame.Visibility=_credentialsDifferent.Visibility=
                userOnly?Visibility.Collapsed:Visibility.Visible;
            _samePassword.IsChecked=userOnly || _credentialsDifferent.IsChecked!=true;
            bool hasSeparatePassword=!userOnly && _samePassword.IsChecked!=true;
            _passwordLength.IsEnabled=hasSeparatePassword;
            passwordKind.Visibility=hasSeparatePassword?Visibility.Visible:Visibility.Collapsed;
            passwordCharsCell.Visibility=hasSeparatePassword?Visibility.Visible:Visibility.Collapsed;
            characterKinds.Columns=hasSeparatePassword?2:1;
            characterRow.Columns=hasSeparatePassword?2:1;
            _designShowPassword.IsChecked=!userOnly;
        }
        _credentialMode.SelectionChanged+=(_,_)=>UpdatePasswordMode();
        _credentialsSame.Checked+=(_,_)=>{_samePassword.IsChecked=true;UpdatePasswordMode();};
        _credentialsDifferent.Checked+=(_,_)=>{_samePassword.IsChecked=false;UpdatePasswordMode();};
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
            _designTitle.Text=savedStyle.NetworkTitle;
            _designFontFamily.SelectedItem=savedStyle.FontFamily;
            _designFontSize.Text=savedStyle.UsernameFontSize.ToString(CultureInfo.InvariantCulture);
            _designBackground.Text=savedStyle.BackgroundColor;
            _designTextColor.Text=savedStyle.UsernameColor;
            _designBorderColor.Text=savedStyle.BorderColor;
            _designBorderWidth.Text=savedStyle.BorderWidth.ToString(CultureInfo.InvariantCulture);
            _designShowPassword.IsChecked=savedStyle.ShowPassword;
            _designShowProfile.IsChecked=savedStyle.ShowProfile;
            ApplyCardAppearance(savedStyle);
        }
        catch(Exception ex)
        {
            _designStatus.Text="تعذر قراءة المظهر المحفوظ؛ لم تُعدل البيانات: "+ex.Message;
        }

        _service.SelectionChanged += async (_, _) =>
        {
            packageSaveFeedback.Text="";
            packageSaveFeedback.Visibility=Visibility.Collapsed;
            _batch = [];
            BatchChanged?.Invoke();
            ServiceChanged?.Invoke();
            _table.ItemsSource = null;
            _sendButton.IsEnabled = false;
            _cardPreview.Text="";
            _profile.ItemsSource = null;
            _customer.ItemsSource = null;
            _customerPanel.Visibility = SelectedService == "userman"
                ? Visibility.Visible : Visibility.Collapsed;
            await LoadProfilesAsync();
        };
        _profile.SelectionChanged += (_, _) =>
        {
            if(_restoringPreset)return;
            packageSaveFeedback.Text="";
            packageSaveFeedback.Visibility=Visibility.Collapsed;
            _batch=[];
            BatchChanged?.Invoke();
            SelectMatchingOwner();
            RestorePackagePreset();
        };
        _customer.SelectionChanged += (_,_) =>
        {
            if(_restoringPreset)return;
            packageSaveFeedback.Text="";
            packageSaveFeedback.Visibility=Visibility.Collapsed;
            RestorePackagePreset();
        };
        SubscribeLiveCardPreview();
        SubscribeLiveCardAppearance();
        Loaded += async (_, _) => await LoadProfilesAsync();
    }

    private VoucherCardAppearance ReadCurrentAppearance()
    {
        static double Number(string v)=>double.Parse(v.Trim().Replace(',','.'),
            CultureInfo.InvariantCulture);
        return new VoucherCardAppearance(_designTitle.Text.Trim(),
            _designFontFamily.SelectedItem?.ToString()??"Arial",
            Number(_designFontSize.Text),_designBackground.Text.Trim(),
            _designTextColor.Text.Trim(),_designBorderColor.Text.Trim(),
            Number(_designBorderWidth.Text),_designShowPassword.IsChecked==true,
            _designShowProfile.IsChecked==true,_templateImagePath).Validate();
    }

    public bool SaveCurrentPackage()
    {
        try
        {
            if(_sending||_preparing||_restoringPreset)
                throw new InvalidOperationException("لا يمكن حفظ إعدادات الباقة أثناء إرسال الكروت أو استعادة الإعدادات.");
            var profile=_profile.SelectedItem?.ToString()??"";
            if(profile.Length==0)throw new ArgumentException("اختر الباقة من الراوتر.");
            if(SelectedService=="hotspot" && _server.SelectedItem?.ToString()!="all")
                throw new InvalidOperationException(
                    "السيرفر المختار ظاهر للفحص، لكن إرسال كروت لسيرفر محدد غير متاح بعد. "+
                    "اختر all حتى لا توزع الكروت على السيرفر الخطأ.");
            int length=int.Parse(_length.Text,CultureInfo.InvariantCulture);
            bool same=_samePassword.IsChecked==true;
            var saved=new VoucherPackagePreset(_routerKey,SelectedService,profile,
                SelectedService=="userman"?_customer.SelectedItem?.ToString()??"":"",
                int.Parse(_count.Text,CultureInfo.InvariantCulture),_prefix.Text.Trim(),
                length,_usernameCharacters.Text.Trim(),same,
                same?length:int.Parse(_passwordLength.Text,CultureInfo.InvariantCulture),
                _passwordCharacters.Text.Trim(),_lengthWithPrefix.IsChecked==true,
                PrintOptionsProvider?.Invoke()??new CardPrintOptions(),ReadCurrentAppearance(),
                _credentialMode.SelectedIndex==0).Validate();
            _store.SavePackagePreset(saved);
            ApplyCardAppearance(saved.Appearance);
            _status.Text="حُفظت إعدادات الباقة "+profile+" حتى بعد إغلاق البرنامج.";
            return true;
        }
        catch(Exception ex){_status.Text="تعذر حفظ إعدادات الباقة: "+ex.Message;return false;}
    }

    // Restore current package preferences without changing navigation or router data.
    public void RestoreSavedPackageSettings() => RestorePackagePreset();

    private void RestorePackagePreset()
    {
        if(_restoringPreset)return;
        string profile=_profile.SelectedItem?.ToString()??"";
        string owner=SelectedService=="userman"?_customer.SelectedItem?.ToString()??"":"";
        if(profile.Length==0||(SelectedService=="userman"&&owner.Length==0))return;
        try
        {
            var v=_store.LoadPackagePreset(_routerKey,SelectedService,profile,owner);
            _restoringPreset=true;
            try
            {
                _count.Text=(v?.Quantity??60).ToString(CultureInfo.InvariantCulture);
                _prefix.Text=v?.Prefix??"";
                _length.Text=(v?.UsernameLength??10).ToString(CultureInfo.InvariantCulture);
                _usernameCharacters.Text=v?.UsernameCharacters??VoucherGenerator.Digits;
                _passwordCharacters.Text=v?.PasswordCharacters??VoucherGenerator.Mixed;
                static int AlphabetIndex(string value)=>value switch
                {
                    VoucherGenerator.Digits=>0,
                    VoucherGenerator.UppercaseLetters=>1,
                    VoucherGenerator.Mixed=>2,
                    _=>3
                };
                _usernameAlphabetType.SelectedIndex=AlphabetIndex(_usernameCharacters.Text);
                _passwordAlphabetType.SelectedIndex=AlphabetIndex(_passwordCharacters.Text);
                _passwordLength.Text=(v?.PasswordLength??6).ToString(CultureInfo.InvariantCulture);
                _samePassword.IsChecked=v?.SamePassword??true;
                _credentialsSame.IsChecked=v?.SamePassword??true;
                _credentialsDifferent.IsChecked=!(v?.SamePassword??true);
                // A never-saved package follows Hadramout's username-only default.
                _credentialMode.SelectedIndex=v?.UsernameOnly==false?1:0;
                _lengthWithPrefix.IsChecked=v?.LengthIncludesPrefix??true;
                var d=v?.Appearance??new VoucherCardAppearance();
                _designTitle.Text=d.NetworkTitle;
                _designFontFamily.SelectedItem=d.FontFamily;
                _designFontSize.Text=d.UsernameFontSize.ToString(CultureInfo.InvariantCulture);
                _designBackground.Text=d.BackgroundColor;
                _designTextColor.Text=d.UsernameColor;
                _designBorderColor.Text=d.BorderColor;
                _designBorderWidth.Text=d.BorderWidth.ToString(CultureInfo.InvariantCulture);
                _designShowPassword.IsChecked=d.ShowPassword;
                _designShowProfile.IsChecked=d.ShowProfile;
                ApplyCardAppearance(d);
                PackagePrintSettingsRestored?.Invoke(v?.Print??new CardPrintOptions());
                _status.Text=v is null?"لا يوجد قالب محفوظ لهذه الباقة.":
                    "استُعيدت إعدادات التوليد والتصميم والطباعة المحفوظة لهذه الباقة.";
            }
            finally {_restoringPreset=false;UpdateLiveCardPreview();}
        }
        catch(Exception ex){_status.Text="تعذر استعادة الباقة؛ لم يتغير السجل: "+ex.Message;}
    }

    public Task PreparePreview() => PrepareBatchAsync(false);
    public async Task<bool> CreateAndSendAsync()
    {
        if(_sending||_preparing||!SaveCurrentPackage())return false;
        _batch=[];BatchChanged?.Invoke();
        await PrepareBatchAsync(true);
        if(_batch.Count==0)return false;
        await UploadAsync();
        return _batch.Any(v=>v.SentToRouter&&v.DeliveryStatus=="SENT");
    }

    private void SelectMatchingOwner()
    {
        if (SelectedService != "userman") return;
        var name = _profile.SelectedItem?.ToString();
        var owner = _umProfiles.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.Ordinal))?.Owner;
        if (!string.IsNullOrWhiteSpace(owner) && _customer.Items.Contains(owner))
            _customer.SelectedItem = owner;
    }

    /// <summary>
    /// All generation and DPAPI/SQLite bulk work runs off the WPF UI dispatcher.
    /// Preview is intentionally ephemeral: it never writes to the database or router.
    /// </summary>
    private async Task PrepareBatchAsync(bool persist)
    {
        if(_sending || _preparing)return;
        _preparing = true;
        _service.IsEnabled = false;
        _previewButton.IsEnabled = _generateButton.IsEnabled = _sendButton.IsEnabled = false;
        try
        {
            var options=ReadGenerationOptions(forPreview:false);
            var service=SelectedService;
            var profile=_profile.SelectedItem?.ToString() ?? "";
            if(service=="hotspot" && _server.SelectedItem?.ToString()!="all")
                throw new InvalidOperationException(
                    "حدد السيرفر all قبل معاينة أو إصدار الكروت؛ "+
                    "الإرسال الموجه لسيرفر خاص لا يزال بحاجة لاختبارات RouterOS.");
            var customer=service=="userman"?_customer.SelectedItem?.ToString()??"":"";
            if(service=="userman" && string.IsNullOrWhiteSpace(customer))
                throw new ArgumentException("اختر حساب مدير User Manager v6 قبل إنشاء الكروت.");
            _status.Text = persist
                ? "جارٍ توليد الدفعة وحفظها محليا بشكل آمن..."
                : "جارٍ إعداد عينة فعلية محلية دون حفظ أو إرسال...";
            var candidates=await Task.Run(()=>
            {
                var reserved=_store.ReservedNames(service);
                var newCards=VoucherGenerator.Generate(options,reserved)
                    .Select(x=>x with {Service=service,Customer=customer}).ToArray();
                if(persist)_store.SaveBatch(newCards);
                return newCards;
            });
            _batch=persist?candidates:[];
            BatchChanged?.Invoke();
            _table.ItemsSource=candidates.Take(500).ToArray();
            _sendButton.IsEnabled=persist;
            var sample=candidates[0];
            _cardPreview.Text=sample.Username+
                (ShowPreviewPassword?"\n"+sample.Password:"");
            _status.Text=persist
                ? $"حُفظت {candidates.Length:N0} كرتا في قاعدة نجم كارد. لم تُرسل إلى الراوتر. "+ 
                    (candidates.Length>500?"الجدول يعرض أول 500 كرت لتسريع العرض.":"")
                : $"معاينة {candidates.Length:N0} كرتا؛ لم تُحفظ ولم تُرسل. "+
                    (candidates.Length>500?"الجدول يعرض أول 500 كرت.":"");
        }
        catch(Exception ex)
        {
            _batch=[];
            _table.ItemsSource=null;
            _sendButton.IsEnabled=false;
            _cardPreview.Text="تعذر تجهيز المعاينة أو إنشاء الكروت.";
            _status.Text="فشل تجهيز الدفعة: "+ex.Message;
            BatchChanged?.Invoke();
        }
        finally
        {
            _preparing=false;
            _service.IsEnabled=true;
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
        if (!PageUi.Confirm("سيتم إرسال " + _batch.Count + " كرتاً إلى خدمة " +
            description + " على الراوتر. هل تريد المتابعة؟"))
            return;

        _sending = true;
        _sendButton.IsEnabled = false;
        _service.IsEnabled = false;
        try
        {
            var current = _batch.ToArray();
            var result = await new VoucherBatchDispatcher(_gateway, _store).SendAsync(current);
            if (result.UncertainOutcome)
            {
                _batch = _store.GetBatchVouchers(service, current[0].BatchId).ToArray();
                BatchChanged?.Invoke();
                _table.ItemsSource = _batch.Take(500).ToArray();
                _status.Text = "الاتصال أو رد الراوتر غير مؤكد. يُمنع إعادة إرسال كروت " +
                    "UNKNOWN/SENDING أو طباعتها قبل التحقق من الراوتر.";
            }
            else
            {
                var persisted = _store.GetBatchVouchers(service, current[0].BatchId).ToArray();
                _batch = persisted;
                BatchChanged?.Invoke();
                _table.ItemsSource = _batch.Take(500).ToArray();
                _status.Text = "تم إرسال " + result.Succeeded +
                    " كرتاً، وفشل " + result.Failed.Count +
                    ". جميع الحالات محفوظة محلياً.";
            }
        }
        catch (Exception ex) { _status.Text = "فشل الإرسال: " + ex.Message; }
        finally
        {
            _sending = false;
            _sendButton.IsEnabled = _batch.Count > 0;
            _service.IsEnabled = true;
        }
    }
}

