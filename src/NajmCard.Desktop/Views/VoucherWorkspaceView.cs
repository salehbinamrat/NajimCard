using System.Windows;
using System.IO;
using Microsoft.Win32;
using NajmCard.Core.Services;
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
        printer.LayoutSettingsChanged+=creator.RefreshInlineCardDimensions;
        creator.BatchChanged+=printer.Invalidate;
        creator.CardAppearanceChanged+=printer.Invalidate;
        creator.AttachPrintingSection(printer);
        creator.RefreshPageCount();

        var root=new DockPanel{LastChildFill=true,FlowDirection=FlowDirection.RightToLeft,
            Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!};
        // Keep the creator's compact dimensions on the shared dark templates.
        var dropdown=new Style(typeof(ComboBox),(Style)Application.Current.FindResource("DarkComboBox"));
        dropdown.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,26d));
        dropdown.Setters.Add(new Setter(Control.BackgroundProperty,
            (Brush)new BrushConverter().ConvertFrom("#FFFAF0")!));
        dropdown.Setters.Add(new Setter(Control.ForegroundProperty,
            (Brush)new BrushConverter().ConvertFrom("#471A30")!));
        dropdown.Setters.Add(new Setter(Control.BorderBrushProperty,
            (Brush)new BrushConverter().ConvertFrom("#75203E")!));
        // The global dark ComboBox template creates white TextBlocks inside
        // SelectionBoxItem. Explicit local item text fixes contrast on cream
        // WITHOUT changing the dark sidebar or the application's theme.
        var cream=(Brush)new BrushConverter().ConvertFrom("#FFFAF0")!;
        var maroon=(Brush)new BrushConverter().ConvertFrom("#471A30")!;
        var optionTemplate=new DataTemplate();
        var optionBacking=new FrameworkElementFactory(typeof(Border));
        optionBacking.SetValue(Border.BackgroundProperty,cream);
        optionBacking.SetValue(Border.PaddingProperty,new Thickness(3,1,3,1));
        var optionText=new FrameworkElementFactory(typeof(TextBlock));
        optionText.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding("."));
        optionText.SetValue(TextBlock.ForegroundProperty,maroon);
        optionText.SetValue(TextBlock.FontWeightProperty,FontWeights.Bold);
        optionText.SetValue(TextBlock.FontSizeProperty,13d);
        optionText.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        optionBacking.AppendChild(optionText);
        optionTemplate.VisualTree=optionBacking;
        dropdown.Setters.Add(new Setter(ItemsControl.ItemTemplateProperty,optionTemplate));
        root.Resources[typeof(ComboBox)]=dropdown;
        var input=new Style(typeof(TextBox),(Style)Application.Current.FindResource(typeof(TextBox)));
        input.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,26d));
        input.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(5,2,5,2)));
        input.Setters.Add(new Setter(Control.BackgroundProperty,
            (Brush)new BrushConverter().ConvertFrom("#FFFAF0")!));
        input.Setters.Add(new Setter(Control.ForegroundProperty,
            (Brush)new BrushConverter().ConvertFrom("#471A30")!));
        input.Setters.Add(new Setter(Control.BorderBrushProperty,
            (Brush)new BrushConverter().ConvertFrom("#75203E")!));
        root.Resources[typeof(TextBox)]=input;
        var fields=new Grid{ClipToBounds=true,UseLayoutRounding=true};
        fields.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        fields.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        // The top-level page title comes from MainWindow, never a second heading.
        // Tabs already identify the service without a duplicate subtitle.
        var tabs=new UniformGrid{Columns=3,Margin=new Thickness(0,0,0,2)};
        var tabButtons=new Dictionary<string,Button>(StringComparer.Ordinal);
        foreach(var (id,title) in new[]{
            ("hotspot","هوتسبوت"),("userman","يوزمنجر v6"),
            ("pppoe","برودباند PPPoE")
        })
        {
            var service=id;
            var btn=PageUi.Button(title,false);
            btn.Height=31;
            btn.MinHeight=31;
            btn.Padding=new Thickness(5,3,5,3);
            btn.Content=new TextBlock {Text=title,FontWeight=FontWeights.Bold};
            btn.FontSize=14;
            btn.FontWeight=FontWeights.ExtraBold;
            btn.Margin=new Thickness(1);
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
                var label=(TextBlock)btn.Content;
                label.Foreground=btn.Foreground;
                label.FontSize=selected?15:14;
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
            VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,
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
            Margin=new Thickness(0,2,0,0)
        };
        var status=new TextBlock
        {
            Text="احفظ إعدادات كل باقة من أعلى قسم الباقة ومسار الإرسال. الطباعة وPDF للكروت المؤكد إرسالها فقط.",
            Foreground=new SolidColorBrush(Color.FromRgb(255,224,169)),
            TextWrapping=TextWrapping.Wrap,FontSize=11,
            FontWeight=FontWeights.SemiBold,
            TextAlignment=TextAlignment.Center,
            Margin=new Thickness(3,1,3,2)
        };
        footer.Children.Add(status);
        var buttonsGrid=new UniformGrid{Columns=6,Margin=new Thickness(2,0,2,3)};
        footer.Children.Add(buttonsGrid);
        Button MakeButton(string title,string bg,string fg)
        {
            var button=PageUi.Button(title,false);
            button.Margin=new Thickness(2);
            button.MinWidth=0;
            button.Padding=new Thickness(5,3,5,3);
            button.Height=39;
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
            MakeButton("إنشاء وإرسال واحد","#13653E","#FFF5E6"),
            MakeButton("إنشاء وإرسال","#EAC477","#49142C"),
            MakeButton("معاينة قبل الإنشاء","#1756AE","#FFFFFF"),
            MakeButton("ملف PDF","#641A81","#FFF5E6"),
            MakeButton("معاينة PDF","#0B7773","#FFFFFF"),
            MakeButton("طباعة دفعة","#6532A6","#FFFFFF")
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
        async Task ExportAdditionalAsync()
        {
            if(!printer.AdditionalExportRequested)return;
            if(!printer.ExportCsvAfterSend && !printer.ExportExcelAfterSend)
            {
                status.Text+=" | التصدير الإضافي: حدد CSV أو Excel.";
                return;
            }
            // Never export all local records, only the confirmed current batch.
            var confirmed=creator.ConfirmedBatch()
                .Where(v=>v.SentToRouter && v.DeliveryStatus=="SENT").ToArray();
            if(confirmed.Length==0)
            {
                status.Text+=" | لا توجد كروت مؤكدة للتصدير الإضافي.";
                return;
            }
            var picker=new OpenFolderDialog
            {
                Title="اختر مجلد ملفات الدفعة الإضافية",
                Multiselect=false
            };
            if(picker.ShowDialog()!=true)
            {
                status.Text+=" | أُلغي التصدير الإضافي بعد نجاح الإرسال.";
                return;
            }
            // Unique suffix prevents silently replacing older batch exports.
            var raw=confirmed[0].BatchId;
            var safe=new string(raw.Where(c=>!Path.GetInvalidFileNameChars().Contains(c))
                .Take(55).ToArray());
            if(string.IsNullOrWhiteSpace(safe))safe="Batch";
            var filename="NajmCard-"+safe+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var folder=picker.FolderName;
            var csv=printer.ExportCsvAfterSend;
            var excel=printer.ExportExcelAfterSend;
            await Task.Run(()=>
            {
                if(csv)VoucherBatchCsvExporter.Export(confirmed,
                    Path.Combine(folder,filename+".csv"));
                if(excel)VoucherExcelExporter.Export(confirmed,
                    Path.Combine(folder,filename+".xlsx"),"NajmCard",includePasswords:false);
            });
            status.Text+=" | تم تصدير "+confirmed.Length+" كرتا دون كلمات المرور "+
                (csv&&excel?"(CSV وExcel)":csv?"(CSV)":"(Excel)")+".";
        }
        actions[0].Click+=async(_,_)=>await Execute(async()=>
        {
            var sent=await creator.CreateAndSendAsync(singleCard:true);
            status.Text=sent
                ?"أُرسل كرت واحد وحُفظت حالته."
                :"لم يتأكد إرسال الكرت. راجع حالة الدفعة قبل إعادة المحاولة.";
            if(sent)await ExportAdditionalAsync();
        });
        actions[1].Click+=async(_,_)=>await Execute(async()=>
        {
            var sent=await creator.CreateAndSendAsync();
            status.Text=sent
                ?"اكتمل إرسال كروت مؤكدة وحُفظت حالاتها في SQLite.":
                "تعذر تأكيد الإرسال. راجع حالات الدفعة قبل إعادة المحاولة.";
            if(sent)await ExportAdditionalAsync();
        });
        async Task PreviewDraftPdf()
        {
            var options=printer.GetOptions();
            var appearance=creator.ReadDraftPreviewAppearance();
            status.Text="جار إعداد معاينة PDF للكروت التجريبية...";
            var cards=await creator.BuildDraftPreviewAsync();
            var path=await printer.CreateDraftPreviewPdfAsync(cards,appearance,options);
            try
            {
                PrintingView.OpenPreviewPdf(path);
                status.Text="معاينة PDF لـ "+cards.Length+" كرت تجريبي دون حفظ دفعة أو إرسالها.";
            }
            catch(System.ComponentModel.Win32Exception)
            {
                status.Text="حُفظ ملف المعاينة التجريبية هنا: "+path;
            }
        }
        actions[2].Click+=async(_,_)=>await Execute(PreviewDraftPdf);
        actions[3].Click+=async(_,_)=>await Execute(printer.SavePdfAsync);
        actions[4].Click+=async(_,_)=>await Execute(async()=>
        {
            if(creator.HasBatch || printer.HasSavedBatchSelection)await printer.PreviewPdfAsync();
            else await PreviewDraftPdf();
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
