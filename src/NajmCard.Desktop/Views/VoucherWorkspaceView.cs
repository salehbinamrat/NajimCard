using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

/// <summary>Shared creation, save, print and PDF flow with three service tabs.</summary>
public sealed class VoucherWorkspaceView : UserControl
{
    private bool _busy;
    public VoucherWorkspaceView(RouterGateway gateway,VoucherStore store,string routerKey,
        string? requestedService=null,string? requestedProfile=null)
    {
        var creator=new GeneratorView(gateway,store,routerKey,embedded:true);
        var printer=new PrintingView(store,embedded:true,
            selectedBatch:creator.ConfirmedBatch,
            selectedAppearance:()=>creator.CurrentAppearance);
        creator.PrintOptionsProvider=printer.GetOptions;
        creator.ApplyPrintOptions=printer.ApplyOptions;
        creator.PackagePrintSettingsRestored+=printer.ApplyOptions;
        printer.LayoutSettingsChanged+=creator.RefreshPageCount;
        creator.BatchChanged+=printer.Invalidate;
        creator.AttachPrintingSection(printer);
        creator.RefreshPageCount();

        var root=new DockPanel{LastChildFill=true,FlowDirection=FlowDirection.RightToLeft,
            Background=(Brush)new BrushConverter().ConvertFrom("#1D0B19")!};
        // Scoped burgundy/gold controls including dropdown items retain readable contrast.
        var CardDropdownStyle=new Style(typeof(ComboBox));
        CardDropdownStyle.Setters.Add(new Setter(Control.BackgroundProperty,(Brush)new BrushConverter().ConvertFrom("#301529")!));
        CardDropdownStyle.Setters.Add(new Setter(Control.ForegroundProperty,(Brush)new BrushConverter().ConvertFrom("#FFF4E6")!));
        CardDropdownStyle.Setters.Add(new Setter(Control.BorderBrushProperty,(Brush)new BrushConverter().ConvertFrom("#D2AA61")!));
        CardDropdownStyle.Setters.Add(new Setter(Control.FontWeightProperty,FontWeights.SemiBold));
        CardDropdownStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,29d));
        var CardDropdownItems=new Style(typeof(ComboBoxItem));
        CardDropdownItems.Setters.Add(new Setter(Control.BackgroundProperty,(Brush)new BrushConverter().ConvertFrom("#301529")!));
        CardDropdownItems.Setters.Add(new Setter(Control.ForegroundProperty,(Brush)new BrushConverter().ConvertFrom("#FFF4E6")!));
        root.Resources[typeof(ComboBox)]=CardDropdownStyle;
        root.Resources[typeof(ComboBoxItem)]=CardDropdownItems;
        var whiteInput=new Style(typeof(TextBox));
        whiteInput.Setters.Add(new Setter(Control.BackgroundProperty,(Brush)new BrushConverter().ConvertFrom("#301529")!));
        whiteInput.Setters.Add(new Setter(Control.ForegroundProperty,(Brush)new BrushConverter().ConvertFrom("#FFF4E6")!));
        whiteInput.Setters.Add(new Setter(Control.BorderBrushProperty,
            (Brush)new BrushConverter().ConvertFrom("#D2AA61")!));
        whiteInput.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,30d));
        whiteInput.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(6,4,6,4)));
        root.Resources[typeof(TextBox)]=whiteInput;
        var darkCheck=new Style(typeof(CheckBox));
        darkCheck.Setters.Add(new Setter(Control.ForegroundProperty,
            (Brush)new BrushConverter().ConvertFrom("#F7DFC0")!));
        root.Resources[typeof(CheckBox)]=darkCheck;
        var fields=new Grid{ClipToBounds=true};
        fields.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        fields.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        // The top-level page title comes from MainWindow, never a second heading.
        // Tabs already identify the service without a duplicate subtitle.
        var tabs=new UniformGrid{Columns=3,Margin=new Thickness(0,0,0,5)};
        var tabButtons=new Dictionary<string,Button>(StringComparer.Ordinal);
        foreach(var (id,title) in new[]{
            ("hotspot","هوتسبوت"),("userman","يوزمنجر v6"),
            ("pppoe","برودباند PPPoE")
        })
        {
            var service=id;
            var btn=PageUi.Button(title,false);
            btn.Height=38;
            btn.FontSize=14;
            btn.FontWeight=FontWeights.ExtraBold;
            btn.Margin=new Thickness(3);
            btn.Click+=(_,_)=>creator.SelectService(service);
            tabs.Children.Add(btn);
            tabButtons.Add(service,btn);
        }
        void PaintTabs()
        {
            foreach(var (name,btn) in tabButtons)
            {
                bool selected=name==creator.CurrentService;
                var palette=name switch
                {
                    "hotspot"=>("#EAC77C","#591632"),
                    "userman"=>("#914064","#FFF1DC"),
                    _=>("#583B68","#FFF1DC")
                };
                btn.Background=(Brush)new BrushConverter().ConvertFrom(palette.Item1)!;
                btn.Foreground=(Brush)new BrushConverter().ConvertFrom(palette.Item2)!;
                btn.Opacity=selected?1.0:0.85;
                btn.FontSize=selected?15:14;
            }
        }
        creator.ServiceChanged+=PaintTabs;
        PaintTabs();
        creator.SelectPackageOnLoad(requestedService,requestedProfile);
        Grid.SetRow(tabs,0);Grid.SetRow(creator,1);
        fields.Children.Add(tabs);
        fields.Children.Add(creator);
        // Scrolling only starts on short laptop displays. The form remains
        // full width; the bottom six print actions stay docked and visible.
        var contentGrid=new Grid{ClipToBounds=true};
        var formScroll=new ScrollViewer{
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
            CanContentScroll=false,
            Focusable=false,
            Content=fields
        };
        contentGrid.Children.Add(formScroll);

        // Footer remains visible while forms and print preview are scrolled.
        var footer=new StackPanel
        {
            Background=(Brush)new BrushConverter().ConvertFrom("#270E20")!,
            Margin=new Thickness(0,6,0,0)
        };
        var status=new TextBlock
        {
            Text="احفظ إعدادات كل باقة من أعلى قسم الباقة ومسار الإرسال. الطباعة وPDF للكروت المؤكد إرسالها فقط.",
            Foreground=new SolidColorBrush(Color.FromRgb(255,224,169)),
            TextWrapping=TextWrapping.Wrap,FontSize=11,
            FontWeight=FontWeights.SemiBold,
            TextAlignment=TextAlignment.Center,
            Margin=new Thickness(4,4,4,4)
        };
        footer.Children.Add(status);
        var buttonsGrid=new UniformGrid{Columns=6,Margin=new Thickness(4,0,4,6)};
        footer.Children.Add(buttonsGrid);
        Button MakeButton(string title,string bg,string fg)
        {
            var button=PageUi.Button(title,false);
            button.Margin=new Thickness(3);
            button.MinWidth=0;
            button.Height=43;
            button.Background=(Brush)new BrushConverter().ConvertFromInvariantString(bg)!;
            button.Foreground=(Brush)new BrushConverter().ConvertFromInvariantString(fg)!;
            button.Content=new TextBlock
            {
                Text=title,TextWrapping=TextWrapping.Wrap,
                FontSize=12,FontWeight=FontWeights.ExtraBold,
                Foreground=button.Foreground,TextAlignment=TextAlignment.Center
            };
            buttonsGrid.Children.Add(button);
            return button;
        }
        var actions=new[]{
            MakeButton("إنشاء وإرسال وحفظ","#720D31","#FFFFFF"),
            MakeButton("إنشاء وإرسال","#AD7419","#FFFFFF"),
            MakeButton("معاينة قبل الطباعة","#1756AE","#FFFFFF"),
            MakeButton("حفظ PDF","#102A47","#FFFFFF"),
            MakeButton("معاينة PDF","#0B7773","#FFFFFF"),
            MakeButton("طباعة الدفعة","#6532A6","#FFFFFF")
        };
        async Task Execute(Func<Task> work)
        {
            if(_busy)return;
            _busy=true;
            foreach(var btn in actions)btn.IsEnabled=false;
            foreach(var btn in tabButtons.Values)btn.IsEnabled=false;
            try{await work();}
            catch(Exception ex){status.Text="تعذر تنفيذ العملية: "+ex.Message;}
            finally
            {
                _busy=false;
                foreach(var btn in actions)btn.IsEnabled=true;
                foreach(var btn in tabButtons.Values)btn.IsEnabled=true;
            }
        }
        // Hadramout behavior: choose the save path BEFORE generation or any
        // RouterOS mutations, and stop if the dialog was cancelled.
        actions[0].Click+=async(_,_)=>await Execute(async()=>
        {
            var destination=printer.ChoosePdfDestination();
            if(destination is null)
            {
                status.Text="ألغيت عملية الحفظ قبل إنشاء أي كرت أو إرسال أي أمر.";
                return;
            }
            if(!await creator.CreateAndSendAsync())
            {
                status.Text="لم تنجح كروت مؤكدة؛ لم ينشأ ملف PDF.";
                return;
            }
            status.Text=printer.SavePdfToPath(destination)
                ?"أُرسلت الكروت المؤكدة وحُفظ PDF بنجاح.":
                "حُفظت نتائج الإرسال لكن فشل إخراج PDF؛ لا تعِد الإرسال بسبب فشل الملف.";
        });
        actions[1].Click+=async(_,_)=>await Execute(async()=>
        {
            status.Text=await creator.CreateAndSendAsync()
                ?"اكتمل إرسال كروت مؤكدة وحُفظت حالاتها في SQLite.":
                "تعذر تأكيد الإرسال. راجع حالات الدفعة قبل إعادة المحاولة.";
        });
        actions[2].Click+=async(_,_)=>await Execute(async()=>
        {
            if(creator.HasBatch)
            {
                status.Text=printer.PreparePreview()
                    ? "معاينة الكروت التي ثبت إرسالها في الدفعة المحددة."
                    : "لم تُجهز معاينة الطباعة؛ راجع حالات إرسال الدفعة وإعدادات الورق.";
            }
            else
            {
                await creator.PreparePreview();
                status.Text="عينة توليد محلية دون حفظ أو إرسال إلى الراوتر.";
            }
        });
        actions[3].Click+=(_,_)=>_ = Execute(()=>
        {
            printer.SavePdf();return Task.CompletedTask;
        });
        actions[4].Click+=(_,_)=>_ = Execute(()=>
        {
            printer.PreviewPdf();return Task.CompletedTask;
        });
        actions[5].Click+=(_,_)=>_ = Execute(()=>
        {
            printer.PrintBatch();return Task.CompletedTask;
        });
        DockPanel.SetDock(footer,Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(contentGrid);
        Content=root;
    }
}
