using System.Diagnostics;
using System.Windows;
using System.IO;
using Microsoft.Win32;
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
        creator.OutputPreferencesProvider=printer.GetOutputPreferences;
        creator.ApplyOutputPreferences=printer.ApplyOutputPreferences;
        creator.PackagePrintSettingsRestored+=printer.ApplyOptions;
        printer.LayoutSettingsChanged+=creator.RefreshPageCount;
        printer.LayoutSettingsChanged+=creator.RefreshInlineCardDimensions;
        creator.BatchChanged+=printer.Invalidate;
        creator.BatchChanged+=printer.ResetPrintBatchSelection;
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
        // Cream input dropdowns need cream rows and maroon text in every
        // state; the shared burgundy popup style must not obscure the list.
        var optionStyle=new Style(typeof(ComboBoxItem));
        optionStyle.Setters.Add(new Setter(Control.BackgroundProperty,cream));
        optionStyle.Setters.Add(new Setter(Control.ForegroundProperty,maroon));
        optionStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8,4,8,4)));
        var optionSurface=new FrameworkElementFactory(typeof(Border));
        optionSurface.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background")
        {
            RelativeSource=new System.Windows.Data.RelativeSource(
                System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        var optionPresenter=new FrameworkElementFactory(typeof(ContentPresenter));
        optionPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        optionSurface.AppendChild(optionPresenter);
        var optionControlTemplate=new ControlTemplate(typeof(ComboBoxItem))
        {
            VisualTree=optionSurface
        };
        optionStyle.Setters.Add(new Setter(Control.TemplateProperty,optionControlTemplate));
        var hovered=new Trigger{Property=ComboBoxItem.IsHighlightedProperty,Value=true};
        hovered.Setters.Add(new Setter(Control.BackgroundProperty,
            (Brush)new BrushConverter().ConvertFrom("#F0D8CE")!));
        optionStyle.Triggers.Add(hovered);
        var selected=new Trigger{Property=ComboBoxItem.IsSelectedProperty,Value=true};
        selected.Setters.Add(new Setter(Control.BackgroundProperty,
            (Brush)new BrushConverter().ConvertFrom("#EAC67F")!));
        optionStyle.Triggers.Add(selected);
        dropdown.Setters.Add(new Setter(ItemsControl.ItemContainerStyleProperty,optionStyle));
        dropdown.Setters.Add(new Setter(ComboBox.MaxDropDownHeightProperty,240d));
        var optionTemplate=new DataTemplate();
        var optionBacking=new FrameworkElementFactory(typeof(Border));
        optionBacking.SetValue(Border.BackgroundProperty,Brushes.Transparent);
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
        // The shared workspace title is docked above the service tabs.
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
                // Only the active service uses the strong gold highlight.
                // A permanent gold Hotspot tab previously made it look active
                // even when User Manager was the selected default.
                var background=selected?"#EAC77C":name switch
                {
                    "hotspot"=>"#5A2840",
                    "userman"=>"#723851",
                    _=>"#583B68"
                };
                btn.Background=(Brush)new BrushConverter().ConvertFrom(background)!;
                btn.Foreground=(Brush)new BrushConverter().ConvertFrom(
                    selected?"#49142C":"#FFF1DC")!;
                btn.BorderBrush=(Brush)new BrushConverter().ConvertFrom(
                    selected?"#F8E1AB":"#76465D")!;
                btn.BorderThickness=new Thickness(selected?2:1);
                btn.Opacity=selected?1.0:0.88;
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
            Text="احفظ إعدادات الباقة والتصميم والطباعة من زر الحفظ بجانب عنوان الصفحة. طباعة الدفعات المؤكدة فقط.",
            Foreground=new SolidColorBrush(Color.FromRgb(255,224,169)),
            TextWrapping=TextWrapping.Wrap,FontSize=11,
            FontWeight=FontWeights.SemiBold,
            TextAlignment=TextAlignment.Center,
            Margin=new Thickness(3,1,3,2)
        };
        var history = PageUi.Button("سجل الدفعات", false);
        history.MinHeight = 24;
        history.Padding = new Thickness(7, 2, 7, 2);
        history.Margin = new Thickness(4, 2, 4, 2);
        history.FontSize = 11;
        history.Click += (_, _) =>
        {
            var dialog = new VoucherDeliveryHistoryWindow(store);
            if (Window.GetWindow(this) is Window owner) dialog.Owner = owner;
            dialog.ShowDialog();
        };
        var statusRow = new DockPanel();
        DockPanel.SetDock(history, Dock.Left);
        statusRow.Children.Add(history);
        statusRow.Children.Add(status);
        footer.Children.Add(statusRow);
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
            MakeButton("إنشاء وإرسال وحفظ","#EAC477","#49142C"),
            MakeButton("إنشاء وإرسال فقط","#13653E","#FFF5E6"),
            MakeButton("معاينة قبل الإنشاء","#1756AE","#FFFFFF"),
            MakeButton("ملف PDF","#641A81","#FFF5E6"),
            MakeButton("معاينة PDF","#0B7773","#FFFFFF"),
            MakeButton("طباعة الدفعة","#6532A6","#FFFFFF")
        };
        async Task Execute(Func<Task> work)
        {
            if(_busy)return;
            _busy=true;
            foreach(var btn in actions)btn.IsEnabled=false;
            foreach(var btn in tabButtons.Values)btn.IsEnabled=false;
            creator.IsEnabled=history.IsEnabled=false;
            try{await work();}
            catch(Exception ex){status.Text="تعذر تنفيذ العملية: "+ex.Message;}
            finally
            {
                _busy=false;
                foreach(var btn in actions)btn.IsEnabled=true;
                foreach(var btn in tabButtons.Values)btn.IsEnabled=true;
                creator.IsEnabled=history.IsEnabled=true;
            }
        }
        async Task WithCardProgressAsync(string operation,Func<Action<string>,Task> work)
        {
            // Measure actual stage durations, including two PDF renders, the
            // RouterOS preflight and the durable per-card sending loop. No
            // estimated completion percentage or network credentials are logged.
            var popup=new VoucherOperationProgressWindow(operation);
            if(Window.GetWindow(this) is Window owner)popup.Owner=owner;
            var watch=Stopwatch.StartNew();
            var stageAt=Stopwatch.GetTimestamp();
            string? currentStage=null;
            var phaseSeconds=new Dictionary<string,double>(StringComparer.Ordinal);
            static string Phase(string stage)
            {
                if(stage.Contains("PDF",StringComparison.OrdinalIgnoreCase))return "ملفات PDF";
                if(stage.Contains("التحقق من الأرقام",StringComparison.Ordinal) ||
                    stage.Contains("جاهزية User Manager",StringComparison.Ordinal))
                    return "فحص الراوتر";
                if(stage.Contains("إرسال الكرت",StringComparison.Ordinal) ||
                    stage.Contains("تسجيل نتيجة الكرت",StringComparison.Ordinal))
                    return "إصدار الكروت";
                if(stage.Contains("قاعدة البيانات",StringComparison.Ordinal))
                    return "الحفظ المحلي";
                return "التحضير";
            }
            void FinishStage()
            {
                if(currentStage is null)return;
                var seconds=Stopwatch.GetElapsedTime(stageAt).TotalSeconds;
                var phase=Phase(currentStage);
                phaseSeconds[phase]=phaseSeconds.GetValueOrDefault(phase)+seconds;
            }
            void ShowStage(string stage)
            {
                FinishStage();
                currentStage=stage;
                stageAt=Stopwatch.GetTimestamp();
                popup.UpdateStage(stage);
            }
            creator.OperationProgressChanged+=ShowStage;
            try
            {
                popup.Show();
                await work(ShowStage);
            }
            finally
            {
                creator.OperationProgressChanged-=ShowStage;
                FinishStage();
                popup.Close();
                if(watch.Elapsed.TotalSeconds>=1)
                {
                    var slowest=phaseSeconds.OrderByDescending(x=>x.Value)
                        .Take(2).Select(x=>x.Key+" "+
                            x.Value.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+"ث");
                    status.Text+=" | زمن العملية "+
                        watch.Elapsed.TotalSeconds.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+
                        "ث؛ الأبطأ: "+string.Join("، ",slowest);
                }
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
            // The same batch can be exported repeatedly; never silently overwrite
            // a prior CSV/XLSX created within the same second.
            var filename="NajmCard-"+safe+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+
                "-"+Guid.NewGuid().ToString("N")[..8];
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
            var picker=new OpenFolderDialog
            {
                Title="اختر مكان حفظ الكروت قبل الإرسال",Multiselect=false
            };
            if(picker.ShowDialog()!=true)return;
            await WithCardProgressAsync("إنشاء وإرسال وحفظ الكروت",async report=>
            {
            var options=printer.GetOptions();
            var appearance=creator.ReadDraftPreviewAppearance();
            string? folder=null;
            string? savedBeforeSend=null;
            int archivedCount=0;
            var sent=await creator.CreateAndSendAsync(saveBeforeSend:async cards=>
            {
                folder=VoucherBatchArchive.CreateDirectory(picker.FolderName,cards);
                savedBeforeSend=Path.Combine(folder,"محفوظة-قبل-الإرسال-لا-تطبع.pdf");
                report("حفظ ملف PDF الأولي للكروت قبل الإرسال...");
                await printer.SaveDraftArchivePdfAsync(cards,appearance,options,savedBeforeSend);
                archivedCount=cards.Count;
                status.Text="حُفظت "+cards.Count+" كرتا قبل الإرسال في: "+folder;
            });
            if(!sent)
            {
                status.Text=archivedCount>0
                    ?"حُفظت الدفعة ولم يتأكد إرسالها: "+creator.LastOperationMessage+" | سجل الدفعات: "+folder
                    :"تعذر إنشاء الدفعة: "+creator.LastOperationMessage;
                return;
            }
            var confirmed=creator.ConfirmedBatch().ToArray();
            var path=Path.Combine(folder!,"كروت-مؤكدة-"+confirmed.Length+".pdf");
            // A pre-send PDF is visibly watermarked, never promoted as if
            // those unverified cards had been activated. Render the final PDF
            // solely from readback-confirmed vouchers, then remove the draft.
            report("حفظ ملف PDF النهائي للكروت المؤكدة في الراوتر...");
            await printer.SaveBatchArchivePdfAsync(confirmed,appearance,options,path);
            if(savedBeforeSend is not null && File.Exists(savedBeforeSend))
                File.Delete(savedBeforeSend);
            status.Text="حُفظت الكروت وأُرسل "+confirmed.Length+" كرتا. ملف PDF: "+path;
            if(confirmed.Length<archivedCount) status.Text+=" | راجع بقية الكروت في «سجل الدفعات».";
            try {PrintingView.OpenPreviewPdf(path);}
            catch(System.ComponentModel.Win32Exception)
            {status.Text+=" | افتح الملف من مجلد الحفظ للمعاينة.";}
            await ExportAdditionalAsync();
            });
        });
        actions[1].Click+=async(_,_)=>await Execute(async()=>
        {
            await WithCardProgressAsync("إنشاء وإرسال الكروت",async report=>
            {
            var sent=await creator.CreateAndSendAsync();
            status.Text=sent
                ?"تم إنشاء الكروت وإرسالها وحفظ حالتها محليا."
                :"لم يتأكد إرسال الكرت: "+creator.LastOperationMessage+" | راجع سجل الدفعات قبل إعادة المحاولة.";
            if(sent)await ExportAdditionalAsync();
            });
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
                status.Text="معاينة تجريبية غير مفعلة: "+cards.Length+
                    " كرتا بعلامة «غير مفعلة في الراوتر». لا تُستخدم للدخول.";
            }
            catch(System.ComponentModel.Win32Exception)
            {
                status.Text="حُفظ ملف المعاينة التجريبية هنا: "+path;
            }
        }
        actions[2].Click+=async(_,_)=>await Execute(PreviewDraftPdf);
        actions[3].Click+=async(_,_)=>await Execute(async()=>
        {
            await printer.SavePdfAsync();
            status.Text=printer.LastOperationMessage;
        });
        actions[4].Click+=async(_,_)=>await Execute(async()=>
        {
            if(!creator.HasBatch && !printer.HasSavedBatchSelection)
            {
                status.Text="لا توجد دفعة مؤكدة للإرسال. استخدم «معاينة قبل الإنشاء» "+
                    "للقوالب التجريبية؛ أرقام المعاينة ليست كروتا عاملة.";
                return;
            }
            await printer.PreviewPdfAsync();
            status.Text=printer.LastOperationMessage;
        });
        actions[5].Click+=(_,_)=>_ = Execute(()=>
        {
            printer.PrintBatch();
            status.Text=printer.LastOperationMessage;
            return Task.CompletedTask;
        });
        DockPanel.SetDock(footer,Dock.Bottom);
        root.Children.Add(footer);
        var titleLabel=new TextBlock{Text="إنشاء وطباعة الكروت",
            HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,
            FlowDirection=FlowDirection.RightToLeft};
        titleLabel.SetResourceReference(StyleProperty,"WorkspaceTitleText");
        // One save action for the complete profile settings, print options,
        // card appearance and generation parameters. It lives in the title row.
        var pageSave=creator.PackageSaveButton;
        pageSave.HorizontalAlignment=HorizontalAlignment.Left;
        pageSave.VerticalAlignment=VerticalAlignment.Center;
        pageSave.Margin=new Thickness(6,2,8,2);
        pageSave.MinHeight=34;
        pageSave.MinWidth=165;
        pageSave.Padding=new Thickness(11,5,11,5);
        var titleGrid=new Grid{FlowDirection=FlowDirection.LeftToRight,MinHeight=43};
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        Grid.SetColumn(pageSave,0);
        Grid.SetColumn(titleLabel,1);
        titleGrid.Children.Add(pageSave);
        titleGrid.Children.Add(titleLabel);
        var header=new Border{Child=titleGrid};
        header.SetResourceReference(StyleProperty,"WorkspaceTitleFrame");
        DockPanel.SetDock(header,Dock.Top);
        root.Children.Add(header);
        root.Children.Add(contentGrid);
        Content=root;
    }
}
