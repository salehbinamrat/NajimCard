using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Printing;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// The sole source of A4/A3 layout for WPF preview, printer and PDF export.
/// When embedded, the selected batch is supplied by GeneratorView; only SENT
/// cards from that batch are printable. No globally newest voucher fallback.
/// </summary>
public partial class PrintingView : UserControl
{
    // Artwork can be light OR dark. The prior single cream backing concealed
    // white/light voucher numbers. Choose a translucent backing from real ink
    // luminance, keeping text legible without altering the customer's artwork.
    internal static Color ChooseArtworkTextBacking(string foreground)
    {
        var ink=(Color)ColorConverter.ConvertFromString(foreground)!;
        static double Linear(byte part)
        {
            var v=part/255.0;
            return v<=0.04045 ? v/12.92 : Math.Pow((v+0.055)/1.055,2.4);
        }
        var luminance=0.2126*Linear(ink.R)+0.7152*Linear(ink.G)+0.0722*Linear(ink.B);
        return luminance>=0.18
            ? Color.FromArgb(225,28,12,29)
            : Color.FromArgb(225,255,250,237);
    }
    private readonly Func<CardPrintOptions> _getOptions;
    private readonly Action<CardPrintOptions> _applyOptions;
    private readonly Func<bool> _preparePreview;
    private readonly Action _printBatch;
    private readonly Func<string,Task<bool>> _exportPdf;
    private readonly Func<IReadOnlyList<Voucher>,VoucherCardAppearance,CardPrintOptions,string,Task> _renderPdfAsync;
    private readonly Func<IReadOnlyList<Voucher>,VoucherCardAppearance,CardPrintOptions,string,Task> _renderDraftPdfAsync;
    private readonly Action _invalidate;
    private readonly TextBlock _status;
    private readonly CheckBox _additionalExport = new() {Content="تصدير إضافي مع الإرسال",IsChecked=false};
    private readonly CheckBox _csvExport = new() {Content="CSV",IsChecked=false,IsEnabled=false};
    private readonly CheckBox _excelExport = new() {Content="Excel",IsChecked=false,IsEnabled=false};
    public bool ExportCsvAfterSend => _additionalExport.IsChecked==true && _csvExport.IsChecked==true;
    public bool ExportExcelAfterSend => _additionalExport.IsChecked==true && _excelExport.IsChecked==true;
    public bool AdditionalExportRequested => _additionalExport.IsChecked==true;
    private ComboBox? _batchSelector;
    public bool HasSavedBatchSelection=>_batchSelector?.SelectedItem is ComboBoxItem
        {Tag:PrintableBatchSummary};


    private static Border ReferenceHeader(string title)=>new()
    {
        Background=(Brush)new BrushConverter().ConvertFrom("#57213E")!,
        BorderBrush=(Brush)new BrushConverter().ConvertFrom("#D2AA61")!,
        BorderThickness=new Thickness(0,0,0,1),
        Padding=new Thickness(8,4,8,4),
        Margin=new Thickness(0,0,0,2),
        CornerRadius=new CornerRadius(5,5,0,0),
        Child=new TextBlock
        {
            Text=title,FontFamily=new FontFamily("Arial"),FontSize=15,
            FontWeight=FontWeights.ExtraBold,Foreground=Brushes.White,
            TextAlignment=TextAlignment.Right
        }
    };

    public event Action? LayoutSettingsChanged;
    public CardPrintOptions GetOptions()=>_getOptions();
    public void ApplyOptions(CardPrintOptions options)=>_applyOptions(options);
    public bool PreparePreview()=>_preparePreview();
    public string LastOperationMessage => _status.Text;
    public void PrintBatch()=>_printBatch();
    public Task<bool> SavePdfToPathAsync(string path)=>_exportPdf(path);
    public void Invalidate()=>_invalidate();

    public string? ChoosePdfDestination()
    {
        var dlg=new SaveFileDialog
        {
            Title="حفظ PDF للدفعة",
            Filter="PDF (*.pdf)|*.pdf",
            DefaultExt=".pdf",AddExtension=true,
            FileName="نجم_كارد_"+DateTime.Now.ToString("yyyyMMdd_HHmm")+".pdf",
            OverwritePrompt=true
        };
        return dlg.ShowDialog()==true?dlg.FileName:null;
    }

    public async Task SavePdfAsync()
    {
        var path=ChoosePdfDestination();
        if(path is not null)await SavePdfToPathAsync(path);
    }

    public async Task PreviewPdfAsync()
    {
        if(!PageUi.Confirm("سيتم فتح ملف PDF مؤقت قد يحتوي كلمات مرور الكروت المرسلة. هل تريد المتابعة؟"))
            return;
        try
        {
            var path=NewPreviewPdfPath();
            if(await SavePdfToPathAsync(path))OpenPreviewPdf(path);
        }
        catch(Exception ex){_status.Text="تعذر فتح معاينة PDF: "+ex.Message;}
    }

    internal async Task<string> CreateDraftPreviewPdfAsync(IReadOnlyList<Voucher> cards,
        VoucherCardAppearance appearance,CardPrintOptions options)
    {
        var path=NewPreviewPdfPath();
        await _renderDraftPdfAsync(cards,appearance,options,path);
        return path;
    }

    internal Task SaveDraftArchivePdfAsync(IReadOnlyList<Voucher> cards,
        VoucherCardAppearance appearance,CardPrintOptions options,string path) =>
        _renderDraftPdfAsync(cards,appearance,options,path);

    internal Task SaveBatchArchivePdfAsync(IReadOnlyList<Voucher> cards,
        VoucherCardAppearance appearance,CardPrintOptions options,string path) =>
        _renderPdfAsync(cards,appearance,options,path);

    internal static void OpenPreviewPdf(string path) =>
        Process.Start(new ProcessStartInfo(path){UseShellExecute=true});

    private static string NewPreviewPdfPath()
    {
        var directory=Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),"NajmCard","Preview");
        Directory.CreateDirectory(directory);
        foreach(var file in Directory.EnumerateFiles(directory,"Najm_*.pdf"))
        {
            try
            {
                if(File.GetLastWriteTimeUtc(file)<DateTime.UtcNow.AddDays(-2))
                    File.Delete(file);
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }
        return Path.Combine(directory,"Najm_"+Guid.NewGuid().ToString("N")+".pdf");
    }

    public PrintingView(VoucherStore store,bool embedded=false,
        Func<IReadOnlyList<Voucher>>? selectedBatch=null,
        Func<VoucherCardAppearance>? selectedAppearance=null)
    {
        _status=PageUi.Status();
        var stack=PageUi.Column();
        if(!embedded)stack.Children.Add(PageUi.Hint(
            "أبعاد فعلية بالملليمتر، وورق A4/A3 واتجاهات مختلفة. "+
            "PDF والمعاينة والطباعة تستخدم القالب نفسه؛ الكروت المرسلة بنجاح فقط."));

        // Reference layout: five basic printing controls, a page settings
        // strip and a separate ordering row in the lower-left workspace.
        // All fields still feed the original CardPrintOptions and planner.
        var qty=PageUi.Input("60");
        var width=PageUi.Input("50");
        var height=PageUi.Input("19");
        var rows=PageUi.Input("15");
        var cols=PageUi.Input("4");
        var gapX=PageUi.Input("0.5");
        var gapY=PageUi.Input("0.5");
        var margin=PageUi.Input("0.5");
        var paper=new ComboBox {ItemsSource=new[]{"A4","A3"},SelectedIndex=0};
        var orientation=new ComboBox {ItemsSource=new[]{"طولي","عرضي"},SelectedIndex=0};
        var order=new ComboBox {ItemsSource=new[]{
            "صفوف يسار","صفوف يمين","صفوف متعرجة","أعمدة يسار","أعمدة يمين"
        },SelectedIndex=1};
        var basicPrintFields=new UniformGrid{Columns=4,Margin=new Thickness(0,1,0,2)};
        var pagePrintFields=new UniformGrid{Columns=embedded?3:5,Margin=new Thickness(0,1,0,2)};
        void Field(UniformGrid host,string title,FrameworkElement control)
        {
            control.MinWidth=0;
            control.Width=double.NaN;
            var part=PageUi.Column();
            part.Margin=new Thickness(2,0,2,3);
            var caption=PageUi.Label(title);
            if(embedded)
            {
                caption.FontSize=11;
                caption.Margin=new Thickness(0,2,0,2);
                caption.Foreground=(Brush)new BrushConverter().ConvertFrom("#48192A")!;
            }
            part.Children.Add(caption);
            var choices=title switch
            {
                "عرض الكرت (مم)"=>new[]{"30","35","40","45","50","55","60","65","70","75","80"},
                "ارتفاع الكرت (مم)"=>new[]{"15","16","18","19","20","22","25","30","35","40"},
                "الصفوف"=>new[]{"1","2","3","4","5","6","8","10","12","15","16","18","20"},
                "الأعمدة"=>new[]{"1","2","3","4","5","6","7","8","9","10"},
                "الفاصل الأفقي" or "الفاصل الرأسي" or "الهامش"
                    =>new[]{"0","0.2","0.3","0.5","0.7","1","1.5","2","3","4","5"},
                _=>null
            };
            if(control is TextBox value && choices is not null)
                part.Children.Add(PageUi.CompactNumberChoice(value,choices));
            else part.Children.Add(control);
            host.Children.Add(part);
        }
        stack.Children.Add(ReferenceHeader("إعداد الطباعة والدفعة"));
        Field(basicPrintFields,"الأعمدة",cols);
        Field(basicPrintFields,"الصفوف",rows);
        Field(basicPrintFields,"الاتجاه",orientation);
        Field(basicPrintFields,"الورق",paper);
        stack.Children.Add(basicPrintFields);

        if(!embedded)
        {
            Field(pagePrintFields,"عرض الكرت (مم)",width);
            Field(pagePrintFields,"ارتفاع الكرت (مم)",height);
        }
        Field(pagePrintFields,"الهامش",margin);
        Field(pagePrintFields,"الفاصل الأفقي",gapX);
        Field(pagePrintFields,"الفاصل الرأسي",gapY);
        stack.Children.Add(pagePrintFields);

        var ordering=new UniformGrid{Columns=selectedBatch is null?4:3,
            Margin=new Thickness(2,1,2,3)};
        var (batchSelector,selectedPrinter)=CreatePrintSelectors(store,selectedBatch);
        Field(ordering,"الدفعة",batchSelector);
        Field(ordering,"الطابعة",selectedPrinter);
        Field(ordering,"ترتيب الكروت",order);
        if(selectedBatch is null)Field(ordering,"أقصى كروت للمعاينة",qty);
        stack.Children.Add(ordering);

        var exportRow=new WrapPanel{Margin=new Thickness(5,1,5,2)};
        foreach(var check in new[]{_additionalExport,_csvExport,_excelExport})
        {
            // The custom global CheckBox template shows the square but not its
            // Content. Use an explicit adjacent caption to prevent blank options.
            var title=new TextBlock
            {
                Text=check.Content?.ToString()??"",
                FontFamily=new FontFamily("Arial"),FontSize=11,
                FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center,
                Foreground=(Brush)new BrushConverter().ConvertFrom("#48192A")!
            };
            check.Content=null;
            check.Margin=new Thickness(2,0,2,0);
            title.MouseLeftButtonUp+=(_,args)=>
            {
                if(check.IsEnabled)check.IsChecked=check.IsChecked!=true;
                args.Handled=true;
            };
            var pair=new StackPanel{Orientation=Orientation.Horizontal,
                FlowDirection=FlowDirection.RightToLeft,Margin=new Thickness(5,2,5,2)};
            pair.Children.Add(check);pair.Children.Add(title);
            exportRow.Children.Add(pair);
        }
        _additionalExport.Checked+=(_,_)=>_csvExport.IsEnabled=_excelExport.IsEnabled=true;
        _additionalExport.Unchecked+=(_,_)=>_csvExport.IsEnabled=_excelExport.IsEnabled=false;
        stack.Children.Add(exportRow);
        rows.TextChanged+=(_,_)=>LayoutSettingsChanged?.Invoke();
        cols.TextChanged+=(_,_)=>LayoutSettingsChanged?.Invoke();
        width.TextChanged+=(_,_)=>LayoutSettingsChanged?.Invoke();
        height.TextChanged+=(_,_)=>LayoutSettingsChanged?.Invoke();

        var actions=new WrapPanel{Margin=new Thickness(0,1,0,10)};
        var previewButton=PageUi.Button("معاينة قبل الطباعة");
        var printButton=PageUi.Button("طباعة الدفعة",false);
        printButton.IsEnabled=false;
        actions.Children.Add(previewButton);actions.Children.Add(printButton);
        actions.Visibility=embedded?Visibility.Collapsed:Visibility.Visible;
        stack.Children.Add(actions);
        stack.Children.Add(_status);
        var viewer=new DocumentViewer
        {
            Height=550,Margin=new Thickness(0,8,0,6),
            Background=new SolidColorBrush(Color.FromRgb(34,12,26))
        };
        viewer.Visibility=embedded?Visibility.Collapsed:Visibility.Visible;
        stack.Children.Add(viewer);
        var printPanel=PageUi.Surface(stack,"#A97A48");
        if(embedded)
        {
            printPanel.Padding=new Thickness(6);
            printPanel.CornerRadius=new CornerRadius(9);
            printPanel.Margin=new Thickness(0);
            // Size to the last working control; never fill the left column
            // with an empty 300px bordered block.
            printPanel.VerticalAlignment=VerticalAlignment.Top;
            printPanel.Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!;
            printPanel.BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!;
            _status.Margin=new Thickness(2,0,2,1);
            _status.Foreground=(Brush)new BrushConverter().ConvertFrom("#542037")!;
        }
        Content=embedded?printPanel:
            PageUi.Wrap("إنشاء وطباعة الكروت","المعاينة والطباعة من البيانات المحلية المؤكدة.",
                printPanel);

        static int Int(string value)
        {
            if(!int.TryParse(value,CultureInfo.InvariantCulture,out int n))
                throw new ArgumentException("عدد الصفوف أو الأعمدة غير صالح.");
            return n;
        }
        static double Num(string value)
        {
            if(!double.TryParse(value.Trim().Replace(',','.'),
                NumberStyles.Float,CultureInfo.InvariantCulture,out var n))
                throw new ArgumentException("أدخل أبعادا صحيحة بالملليمتر.");
            return n;
        }
        static double Dip(double mm)=>mm*96d/25.4;
        CardPrintOptions Options()
        {
            var modes=new[]{"left_to_right","right_to_left","snake","column_left","column_right"};
            var result=new CardPrintOptions(
                Rows:Int(rows.Text),Columns:Int(cols.Text),
                CardWidthMm:Num(width.Text),CardHeightMm:Num(height.Text),
                GapHorizontalMm:Num(gapX.Text),GapVerticalMm:Num(gapY.Text),
                MarginMm:Num(margin.Text),
                Paper:paper.SelectedItem?.ToString()??"A4",
                Orientation:orientation.SelectedIndex==1?"landscape":"portrait",
                Order:modes[Math.Clamp(order.SelectedIndex,0,4)],Duplex:"single");
            CardPrintLayoutPlanner.Calculate(result,1);
            return result;
        }
        void Apply(CardPrintOptions opts)
        {
            rows.Text=opts.Rows.ToString(CultureInfo.InvariantCulture);
            cols.Text=opts.Columns.ToString(CultureInfo.InvariantCulture);
            width.Text=opts.CardWidthMm.ToString(CultureInfo.InvariantCulture);
            height.Text=opts.CardHeightMm.ToString(CultureInfo.InvariantCulture);
            gapX.Text=opts.GapHorizontalMm.ToString(CultureInfo.InvariantCulture);
            gapY.Text=opts.GapVerticalMm.ToString(CultureInfo.InvariantCulture);
            margin.Text=opts.MarginMm.ToString(CultureInfo.InvariantCulture);
            paper.SelectedItem=opts.Paper;
            orientation.SelectedIndex=opts.Orientation=="landscape"?1:0;
            order.SelectedIndex=opts.Order switch
            {
                "right_to_left"=>1,"snake"=>2,"column_left"=>3,"column_right"=>4,_=>0
            };
        }

        FixedDocument? prepared=null;
        VoucherCardAppearance? designUsed=null;
        int preparedCount=0;
        void Clear()
        {
            prepared=null;designUsed=null;preparedCount=0;
            viewer.Document=null;printButton.IsEnabled=false;
        }
        _invalidate=()=>{Clear();if(selectedBatch is not null)batchSelector.SelectedIndex=0;};
        _getOptions=Options;
        _applyOptions=Apply;
        foreach(var input in new[]{qty,width,height,rows,cols,gapX,gapY,margin})
            input.TextChanged+=(_,_)=>Clear();
        foreach(var choice in new[]{paper,orientation,order,batchSelector})
            choice.SelectionChanged+=(_,_)=>Clear();

        static FixedDocument MakeDocument(IReadOnlyList<Voucher> cards,
            CardPrintOptions opts,VoucherCardAppearance appearance,bool previewOnly=false)
        {
            var plan=CardPrintLayoutPlanner.Calculate(opts,cards.Count);
            var doc=new FixedDocument();
            Brush fill=(Brush)new BrushConverter().ConvertFromInvariantString(appearance.BackgroundColor)!;
            bool hasArtwork=false;
            if(!string.IsNullOrWhiteSpace(appearance.TemplateImagePath))
            {
                if(!File.Exists(appearance.TemplateImagePath))
                    throw new FileNotFoundException(
                        "صورة قالب الكرت المحفوظة غير موجودة؛ لن يتم طباعة قالب مختلف بصمت.",
                        appearance.TemplateImagePath);
                try
                {
                    var bitmap=new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption=BitmapCacheOption.OnLoad;
                    bitmap.UriSource=new Uri(appearance.TemplateImagePath,UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    fill=new ImageBrush(bitmap){Stretch=Stretch.Fill};
                    hasArtwork=true;
                }
                catch(Exception ex) when(ex is IOException or NotSupportedException
                    or InvalidOperationException or ArgumentException
                    or FormatException or UnauthorizedAccessException
                    or System.Runtime.InteropServices.COMException)
                {
                    throw new InvalidDataException("تعذر فك صورة القالب المحفوظة؛ لن تُطبع دفعة بتصميم خاطئ.",ex);
                }
            }
            bool builtinArtwork=NajmNetCardArtwork.IsDefault(
                appearance.NetworkTitle,appearance.TemplateImagePath,
                appearance.BackgroundColor);
            if(builtinArtwork)hasArtwork=true;
            var ink=(Brush)new BrushConverter().ConvertFromInvariantString(appearance.EffectiveElementColor)!;
            var numberInk=(Brush)new BrushConverter().ConvertFromInvariantString(appearance.UsernameColor)!;
            var borderInk=(Brush)new BrushConverter().ConvertFromInvariantString(appearance.BorderColor)!;
            var family=new FontFamily(appearance.FontFamily);
            var slotsByPage=plan.Slots.ToLookup(slot=>slot.PageIndex);
            for(int pageNo=0;pageNo<plan.PdfPages;pageNo++)
            {
                var page=new FixedPage
                {
                    Width=Dip(plan.PageWidthMm),Height=Dip(plan.PageHeightMm),Background=Brushes.White
                };
                foreach(var slot in slotsByPage[pageNo])
                {
                    var card=cards[slot.VoucherIndex];
                    // Use the identical normalized element positions in preview and print.
                    // Every visible caption, credential and QR has its own transparent layer.
                    var positioned=new CardElementPanel
                    {
                        FlowDirection=FlowDirection.LeftToRight,ClipToBounds=true
                    };
                    UIElement Layer(TextBlock text)
                    {
                        if(!hasArtwork)return text;
                        return new Border
                        {
                            Background=new SolidColorBrush(ChooseArtworkTextBacking(appearance.EffectiveElementColor)),
                            CornerRadius=new CornerRadius(3),Padding=new Thickness(1,0,1,0),
                            Child=text
                        };
                    }
                    void AddText(string value,CardElementPosition pos,double size,
                        bool enabled=true,bool credential=false,bool usernameCredential=false)
                    {
                        if(!enabled || string.IsNullOrWhiteSpace(value))return;
                        var line=new TextBlock
                        {
                            Text=value,FontFamily=family,
                            Tag=credential?VoucherPdfExporter.CopyableCredentialTag:null,
                            FontWeight=usernameCredential?FontWeights.ExtraBold:FontWeights.Bold,FontSize=size,
                            Foreground=usernameCredential?numberInk:ink,TextAlignment=TextAlignment.Center,
                            FlowDirection=FlowDirection.LeftToRight,
                            TextTrimming=TextTrimming.CharacterEllipsis,
                            Background=Brushes.Transparent
                        };
                        if(credential)
                        {
                            var plate=CardCredentialPlate.Build(line,opts.CardWidthMm,opts.CardHeightMm);
                            if(usernameCredential)
                                CardCredentialPlate.ApplyNumberBacking(plate,appearance,hasArtwork);
                            positioned.Add(plate,pos);
                        }
                        else positioned.Add(Layer(line),pos);
                    }
                    AddText(appearance.NetworkTitle,
                        appearance.TitlePosition??CardElementPanel.DefaultTitle,
                        10*appearance.TitleScale,appearance.ShowTitle);
                    AddText(card.Username,appearance.CredentialsPosition??
                        CardElementPanel.DefaultCredentials,appearance.UsernameFontSize,
                        credential:true,usernameCredential:true);
                    AddText(card.Password,appearance.PasswordPosition??
                        CardElementPanel.PasswordBelow(appearance.CredentialsPosition),
                        10*appearance.PasswordScale,appearance.ShowPassword,credential:true);
                    AddText(card.Profile+" · "+card.Service,appearance.ProfilePosition??
                        CardElementPanel.DefaultProfile,9*appearance.ProfileScale,
                        appearance.ShowProfile);
                    AddText(appearance.CapacityText,appearance.CapacityPosition??
                        new CardElementPosition(0.96,0.24),9,
                        appearance.ShowCapacity);
                    AddText(appearance.PhoneText,appearance.PhonePosition??
                        new CardElementPosition(0.02,0.98),9,
                        appearance.ShowPhone);
                    AddText(card.CreatedAt.ToString("HH:mm",CultureInfo.InvariantCulture),
                        appearance.TimePosition??new CardElementPosition(0.94,0.76),
                        9,appearance.ShowTime);
                    AddText(card.CreatedAt.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
                        appearance.DatePosition??new CardElementPosition(0.98,0.98),
                        9,appearance.ShowDate);
                    AddText((slot.VoucherIndex+1).ToString("D4",CultureInfo.InvariantCulture),
                        appearance.SequencePosition??new CardElementPosition(0.08,0.57),
                        9,appearance.ShowSequence);
                    AddText(card.Service,appearance.RoutePosition??
                        new CardElementPosition(0.08,0.13),9,appearance.ShowRoute);
                    if(appearance.ShowQr)
                    {
                        var qr=new Image
                        {
                            Source=CardQrRenderer.Render(card.Username),
                            Width=appearance.QrSize,Height=appearance.QrSize,
                            Stretch=Stretch.Uniform
                        };
                        positioned.Add(qr,appearance.QrPosition??
                            new CardElementPosition(0.85,0.45));
                    }
                    FrameworkElement cardContent=positioned;
                    if(builtinArtwork)
                    {
                        var withArtwork=new Grid
                        {
                            Width=Dip(opts.CardWidthMm),Height=Dip(opts.CardHeightMm),
                            ClipToBounds=true
                        };
                        withArtwork.Children.Add(NajmNetCardArtwork.Create(
                            Dip(opts.CardWidthMm),Dip(opts.CardHeightMm)));
                        withArtwork.Children.Add(positioned);
                        cardContent=withArtwork;
                    }
                    var cardBorder=new Border
                    {
                        Width=Dip(opts.CardWidthMm),Height=Dip(opts.CardHeightMm),
                        BorderBrush=borderInk,BorderThickness=new Thickness(appearance.BorderWidth),
                        Background=fill,ClipToBounds=true,Child=cardContent
                    };
                    FixedPage.SetLeft(cardBorder,Dip(slot.LeftMm));
                    FixedPage.SetTop(cardBorder,Dip(slot.TopMm));
                    page.Children.Add(cardBorder);
                    if(previewOnly)
                    {
                        // Sample or pre-send numbers must never resemble issued,
                        // working vouchers, even if the user prints this PDF.
                        var warning=new Border
                        {
                            Width=Dip(opts.CardWidthMm),Height=Dip(opts.CardHeightMm),
                            Background=new SolidColorBrush(Color.FromArgb(125,255,245,235)),
                            BorderBrush=Brushes.DarkRed,
                            BorderThickness=new Thickness(2),
                            Child=new TextBlock
                            {
                                Text="معاينة تجريبية\\nغير مفعلة في الراوتر",
                                Foreground=Brushes.DarkRed,FontWeight=FontWeights.ExtraBold,
                                FontFamily=new FontFamily("Arial"),FontSize=15,
                                TextAlignment=TextAlignment.Center,
                                TextWrapping=TextWrapping.Wrap,
                                HorizontalAlignment=HorizontalAlignment.Center,
                                VerticalAlignment=VerticalAlignment.Center,
                                FlowDirection=FlowDirection.RightToLeft
                            }
                        };
                        FixedPage.SetLeft(warning,Dip(slot.LeftMm));
                        FixedPage.SetTop(warning,Dip(slot.TopMm));
                        page.Children.Add(warning);
                    }
                }
                var pageContent=new PageContent();
                ((System.Windows.Markup.IAddChild)pageContent).AddChild(page);
                doc.Pages.Add(pageContent);
            }
            return doc;
        }

        Task RenderExportAsync(IReadOnlyList<Voucher> cards,
            VoucherCardAppearance appearance,CardPrintOptions options,
            string path,bool previewOnly)
        {
            var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            // PDF rendering and WPF visual tree construction stay on one STA.
            var worker=new Thread(()=>
            {
                try
                {
                    VoucherPdfExporter.Export(
                        MakeDocument(cards,options,appearance,previewOnly),path);
                    completion.SetResult();
                }
                catch(Exception ex){completion.SetException(ex);}
            }){IsBackground=true};
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            return completion.Task;
        }
        _renderPdfAsync=(cards,appearance,options,path)=>
            RenderExportAsync(cards,appearance,options,path,false);
        _renderDraftPdfAsync=(cards,appearance,options,path)=>
            RenderExportAsync(cards,appearance,options,path,true);

        (Voucher[] Cards,VoucherCardAppearance Style,CardPrintOptions Options)? ConfirmPrintJob()
        {
            var requested=(batchSelector.SelectedItem as ComboBoxItem)?.Tag is
                PrintableBatchSummary previous
                ?store.GetBatchVouchers(previous.Service,previous.BatchId)
                :selectedBatch is null
                    ?store.GetPrintableVouchers(Math.Clamp(Int(qty.Text),1,10000))
                    :selectedBatch();
            var cards=requested.Where(x=>x.DeliveryStatus=="SENT"&&x.SentToRouter)
                .ToArray();
            if(cards.Length==0)
            {
                _status.Text="لا توجد كروت مؤكدة الإرسال من الدفعة المختارة؛ لا تُطبع حالات UNKNOWN/FAILED.";
                return null;
            }
            if(cards.Select(x=>x.Username).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=cards.Length)
                throw new InvalidOperationException("تكرار أرقام الكروت في الدفعة يمنع الطباعة.");
            var style=(selectedAppearance?.Invoke()??store.LoadCardAppearance()).Validate();
            var opts=Options();
            if(!PageUi.Confirm("هل تريد معاينة "+cards.Length+
                " كرتا مؤكدا إرساله، "+(style.ShowPassword?"مع كلمات المرور؟":"دون كلمات المرور؟")))
                return null;
            return (cards,style,opts);
        }

        bool Prepare()
        {
            Clear();
            try
            {
                if(ConfirmPrintJob() is not { } job)return false;
                var (cards,style,opts)=job;
                prepared=MakeDocument(cards,opts,style);
                preparedCount=cards.Length;designUsed=style;
                viewer.Document=prepared;
                printButton.IsEnabled=true;
                _status.Text="جاهزة: "+cards.Length+" كرت، "+
                    CardPrintLayoutPlanner.Calculate(opts,cards.Length).PhysicalSheets+
                    " ورقة. راجع توافق إعداد الطابعة مع "+opts.Paper+".";
                return true;
            }
            catch(Exception ex){_status.Text="تعذر إعداد معاينة الطباعة: "+ex.Message;return false;}
        }

        void Print()
        {
            if(prepared is null&&!Prepare())return;
            if(!PageUi.Confirm("إرسال "+preparedCount+
                " كرتا مؤكدا إرساله إلى الطابعة؛ هل مقاس الورق والاتجاه صحيحان؟"))return;
            LocalPrintServer? chosenServer=null;
            try
            {
                var dlg=new PrintDialog();
                var printerName=selectedPrinter.SelectedItem?.ToString();
                if(!string.IsNullOrWhiteSpace(printerName) &&
                    printerName!="الافتراضية")
                {
                    chosenServer=new LocalPrintServer();
                    dlg.PrintQueue=chosenServer.GetPrintQueue(printerName);
                }
                var physical=Options();
                var ticket=dlg.PrintTicket??new PrintTicket();
                ticket.PageMediaSize=new PageMediaSize(
                    physical.Paper=="A3"?PageMediaSizeName.ISOA3:PageMediaSizeName.ISOA4);
                ticket.PageOrientation=physical.Orientation=="landscape"
                    ?PageOrientation.Landscape:PageOrientation.Portrait;
                dlg.PrintTicket=ticket;
                if(dlg.ShowDialog()!=true)return;
                dlg.PrintDocument(prepared!.DocumentPaginator,"NajmCard Confirmed Voucher Batch");
                _status.Text="أُرسلت المهمة للطابعة؛ راجع محاذاة الورق على نسخة اختبار.";
            }
            catch(Exception ex){_status.Text="تعذرت الطباعة: "+ex.Message;}
            finally{chosenServer?.Dispose();}
        }
        async Task<bool> Export(string path)
        {
            try
            {
                if(ConfirmPrintJob() is not { } job)return false;
                _status.Text="جارٍ إخراج PDF للدفعة المؤكدة...";
                await _renderPdfAsync(job.Cards,job.Style,job.Options,path);
                _status.Text="تم حفظ PDF للدفعة المؤكدة: "+path;
                return true;
            }
            catch(Exception ex){_status.Text="تعذر تصدير PDF: "+ex.Message;return false;}
        }
        _preparePreview=()=>
        {
            if(!Prepare())return false;
            if(embedded && prepared is not null)
            {
                var previewWindow=new Window
                {
                    Title="نجم كارد — معاينة الدفعة قبل الطباعة",
                    Width=1000,Height=750,MinWidth=680,MinHeight=500,
                    WindowStartupLocation=WindowStartupLocation.CenterOwner,
                    Owner=Window.GetWindow(this),FlowDirection=FlowDirection.RightToLeft
                };
                var previewViewer=new DocumentViewer
                {
                    Document=prepared,Background=(Brush)Application.Current.FindResource("SurfaceBrush")
                };
                previewWindow.Content=previewViewer;
                previewWindow.ShowDialog();
            }
            return true;
        };
        _printBatch=Print;
        _exportPdf=Export;
        previewButton.Click+=(_,_)=>Prepare();
        printButton.Click+=(_,_)=>Print();
    }
}
