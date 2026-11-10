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

public class ToolsView : UserControl
{
    public ToolsView(RouterGateway gateway, string defaultRouterHost = "")
    {
        HotspotTemplateWorkspace? workspace = null;
        HotspotTemplateFile? selected = null;
        string loadedText = "";
        bool changingSelection = false;

        var stack = PageUi.Column();

        // Real RouterOS6 read-only inventory. Never read automatically on page creation.
        var networkPanel=PageUi.Column();
        networkPanel.Children.Add(PageUi.Title("واجهات الشبكة وعناوين IP والأجهزة المجاورة"));
        networkPanel.Children.Add(PageUi.Hint(
            "قراءة مباشرة عند الضغط على تحديث من RouterOS API: الواجهات والعناوين والأجهزة " +
            "المكتشفة فقط. لا تُعرض كلمات مرور ولا تُرسل أوامر تغيير إعدادات الراوتر."));
        var refreshNetwork=PageUi.Button("تحديث بيانات الواجهات و IP والأجهزة");
        networkPanel.Children.Add(refreshNetwork);
        var networkStatus=PageUi.Status();
        networkStatus.Text="لم تُقرأ بيانات الشبكة بعد. اضغط تحديث لبدء القراءة.";
        networkPanel.Children.Add(networkStatus);
        var interfaceGrid=PageUi.Table(
            ("الواجهة","Name"),("النوع","Type"),("التشغيل","RunningLabel"),
            ("التفعيل","DisabledLabel"),("MAC","MacAddress"));
        interfaceGrid.IsReadOnly=true;
        networkPanel.Children.Add(PageUi.Label("الواجهات"));
        networkPanel.Children.Add(interfaceGrid);
        var addressGrid=PageUi.Table(
            ("عنوان IP","Address"),("الشبكة","Network"),
            ("الواجهة","Interface"),("الحالة","DisabledLabel"));
        addressGrid.IsReadOnly=true;
        networkPanel.Children.Add(PageUi.Label("عناوين IP"));
        networkPanel.Children.Add(addressGrid);
        var neighborGrid=PageUi.Table(
            ("الجهاز","Identity"),("الواجهة","Interface"),("عنوان IP","IpAddress"),
            ("MAC","MacAddress"),("المنصة","Platform"));
        neighborGrid.IsReadOnly=true;
        networkPanel.Children.Add(PageUi.Label("الأجهزة المجاورة"));
        networkPanel.Children.Add(neighborGrid);
        stack.Children.Add(PageUi.Surface(networkPanel));

        // Hadramout maintenance parity: count-only preflight before listing
        // discovered neighbors. No active network scan, no RouterOS mutations.
        var neighborPanel=PageUi.Column();
        neighborPanel.Children.Add(PageUi.Title("اكتشاف الأجهزة المجاورة — فحص محدود"));
        neighborPanel.Children.Add(PageUi.Hint(
            "يطلب البرنامج عدد أجهزة MikroTik Neighbors أولا، ويلغي القراءة إذا كانت النتيجة " +
            "غير موثوقة أو تجاوزت 5000 جهاز. البحث والنسخ محليان فقط."));
        var neighborActions=new WrapPanel {Margin=new Thickness(0,1,0,9)};
        var refreshNeighbors=PageUi.Button("قراءة الأجهزة المجاورة");
        var copyNeighbor=PageUi.Button("نسخ IP / MAC المحدد",false);
        copyNeighbor.IsEnabled=false;
        neighborActions.Children.Add(refreshNeighbors);
        neighborActions.Children.Add(copyNeighbor);
        neighborPanel.Children.Add(neighborActions);
        neighborPanel.Children.Add(PageUi.Label("بحث بالاسم أو IP أو MAC أو الواجهة"));
        var neighborSearch=PageUi.Input();
        neighborPanel.Children.Add(neighborSearch);
        var detailedNeighbors=PageUi.Table(
            ("الجهاز","Identity"),("IPv4 / العنوان","PreferredAddress"),
            ("MAC","MacAddress"),("الواجهة","Interface"),
            ("المنصة","Platform"),("الإصدار","Version"),("اللوحة","Board"));
        detailedNeighbors.IsReadOnly=true;
        detailedNeighbors.CanUserAddRows=false;
        detailedNeighbors.CanUserDeleteRows=false;
        detailedNeighbors.SelectionChanged+=(_,_) =>
            copyNeighbor.IsEnabled=detailedNeighbors.SelectedItem is RouterNeighborDetail;
        neighborPanel.Children.Add(detailedNeighbors);
        var neighborStatus=PageUi.Status();
        neighborPanel.Children.Add(neighborStatus);
        stack.Children.Add(PageUi.Surface(neighborPanel));

        IReadOnlyList<RouterNeighborDetail> lastNeighbors=[];
        void FilterNeighbors()
        {
            var term=neighborSearch.Text.Trim();
            // In-memory filtering only; typing never sends another API request.
            var matching=string.IsNullOrEmpty(term)?lastNeighbors:lastNeighbors.Where(item=>
                item.Identity.Contains(term,StringComparison.OrdinalIgnoreCase) ||
                item.PreferredAddress.Contains(term,StringComparison.OrdinalIgnoreCase) ||
                item.MacAddress.Contains(term,StringComparison.OrdinalIgnoreCase) ||
                item.Interface.Contains(term,StringComparison.OrdinalIgnoreCase) ||
                item.Platform.Contains(term,StringComparison.OrdinalIgnoreCase) ||
                item.Version.Contains(term,StringComparison.OrdinalIgnoreCase)).ToArray();
            detailedNeighbors.ItemsSource=matching;
            neighborStatus.Text=matching.Count+" جهاز من أصل "+lastNeighbors.Count+
                " | البحث محلي دون إعادة الاتصال.";
        }
        neighborSearch.TextChanged+=(_,_)=>FilterNeighbors();
        refreshNeighbors.Click+=async(_,_) =>
        {
            refreshNeighbors.IsEnabled=false;
            copyNeighbor.IsEnabled=false;
            detailedNeighbors.ItemsSource=null;
            lastNeighbors=[];
            neighborStatus.Text="جارٍ التحقق من عدد أجهزة Neighbors على الراوتر...";
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                lastNeighbors=await gateway.ReadNearbyDevicesAsync(timeout.Token);
                FilterNeighbors();
            }
            catch(Exception ex)
            {
                neighborStatus.Text="تعذرت قراءة الأجهزة: "+ex.Message+
                    " | لم تعرض نتائج جزئية.";
            }
            finally {refreshNeighbors.IsEnabled=true;}
        };
        copyNeighbor.Click+=(_,_) =>
        {
            if(detailedNeighbors.SelectedItem is not RouterNeighborDetail selected)return;
            var text=selected.PreferredAddress+" / "+selected.MacAddress;
            try
            {
                Clipboard.SetText(text);
                neighborStatus.Text="نُسخ IP / MAC للجهاز المحدد فقط.";
            }
            catch(Exception ex)
            {
                neighborStatus.Text="تعذر النسخ إلى الحافظة: "+ex.Message;
            }
        };

        refreshNetwork.Click+=async (_,_) =>
        {
            refreshNetwork.IsEnabled=false;
            // Never retain stale partial rows after an API failure or access denial.
            interfaceGrid.ItemsSource=null;
            addressGrid.ItemsSource=null;
            neighborGrid.ItemsSource=null;
            networkStatus.Text="جارٍ قراءة ثلاث قوائم من RouterOS API...";
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try
            {
                var inventory=await gateway.ReadNetworkInventoryAsync(timeout.Token);
                interfaceGrid.ItemsSource=inventory.Interfaces;
                addressGrid.ItemsSource=inventory.Addresses;
                neighborGrid.ItemsSource=inventory.Neighbors;
                networkStatus.Text="تم التحديث: "+inventory.Interfaces.Count+
                    " واجهة، "+inventory.Addresses.Count+" عنوان IP، "+
                    inventory.Neighbors.Count+" جهاز مجاور. القراءة فقط.";
            }
            catch(Exception ex)
            {
                networkStatus.Text="تعذرت قراءة البيانات أو رفض الراوتر الصلاحية: "+
                    ex.Message+" | لم تُعرض أي نتائج جزئية.";
            }
            finally { refreshNetwork.IsEnabled=true; }
        };

        var template = PageUi.Column();
        template.Children.Add(PageUi.Title("محرر صفحات Hotspot المحلي"));
        template.Children.Add(PageUi.Hint(
            "افتح مجلد القالب الأصلي الذي يحتوي على login.html. يتم حفظ نسخة احتياطية قبل تعديل أي ملف نصي، " +
            "وتُحفظ متغيرات MikroTik كما كتبتها. هذه المرحلة محلية فقط ولا ترفع ملفات إلى الراوتر."));
        var folderLabel=PageUi.Status();
        folderLabel.Text="لم يتم فتح مجلد صفحات Hotspot بعد.";
        var status=PageUi.Status();
        var toolbar = new StackPanel
        {
            Orientation=Orientation.Horizontal,Margin=new Thickness(0,4,0,12)
        };
        var choose=PageUi.Button("فتح مجلد Hotspot");
        var save=PageUi.Button("حفظ مع نسخة احتياطية",false);
        var export=PageUi.Button("تصدير القالب ZIP",false);
        save.IsEnabled=false;
        export.IsEnabled=false;
        toolbar.Children.Add(choose);
        toolbar.Children.Add(save);
        toolbar.Children.Add(export);
        template.Children.Add(toolbar);
        template.Children.Add(folderLabel);

        var files=PageUi.Table(
            ("الملف", "RelativePath"),("الحجم بالبايت", "SizeBytes"),
            ("قابل للتحرير", "Editable"));
        files.MaxHeight=270;
        files.IsReadOnly=true;
        files.CanUserAddRows=false;
        files.CanUserDeleteRows=false;
        template.Children.Add(PageUi.Label("ملفات القالب والموارد المرافقة"));
        template.Children.Add(files);
        template.Children.Add(PageUi.Label("محرر HTML / CSS / JavaScript (UTF-8)"));
        var editor=new TextBox
        {
            AcceptsReturn=true,AcceptsTab=true,TextWrapping=TextWrapping.NoWrap,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
            FlowDirection=FlowDirection.LeftToRight,
            FontFamily=new FontFamily("Consolas"),FontSize=14,
            Foreground=new SolidColorBrush(Color.FromRgb(245,222,188)),
            Background=new SolidColorBrush(Color.FromRgb(35,15,28)),
            BorderBrush=new SolidColorBrush(Color.FromRgb(176,122,85)),
            MinHeight=275,MaxHeight=480,IsReadOnly=true,
            Padding=new Thickness(11)
        };
        template.Children.Add(editor);
        template.Children.Add(status);
        stack.Children.Add(PageUi.Surface(template));
        var ftpPanel=PageUi.Column();
        ftpPanel.Children.Add(PageUi.Title("رفع وتنزيل صفحات Hotspot من MikroTik"));
        ftpPanel.Children.Add(PageUi.Hint(
            "التنزيل ينشئ مجلداً محلياً جديداً. الرفع يتطلب أولاً تنزيل وحفظ ZIP كامل " +
            "من ملفات الراوتر الحالية؛ إذا فشل النسخ الاحتياطي يتوقف الرفع دون أي كتابة. " +
            "مجلد الراوتر يجب أن يكون hotspot أو flash/hotspot أو disk1/hotspot."));
        var ftpHost=PageUi.Input(defaultRouterHost);
        var ftpPort=PageUi.Input("21");
        var ftpUser=PageUi.Input("admin");
        var ftpPassword=new PasswordBox
        {
            Width=240,FlowDirection=FlowDirection.LeftToRight,
            Margin=new Thickness(0,0,10,0)
        };
        var ftpRemote=PageUi.Input("hotspot");
        ftpHost.MinWidth=230;
        ftpPort.MinWidth=80;
        ftpUser.MinWidth=170;
        ftpRemote.MinWidth=230;
        ftpPanel.Children.Add(PageUi.Label("عنوان الراوتر أو IP لخدمة FTP"));
        ftpPanel.Children.Add(ftpHost);
        ftpPanel.Children.Add(PageUi.Label("منفذ FTP"));
        ftpPanel.Children.Add(ftpPort);
        ftpPanel.Children.Add(PageUi.Label("اسم المستخدم المصرح بإدارة ملفات Hotspot"));
        ftpPanel.Children.Add(ftpUser);
        ftpPanel.Children.Add(PageUi.Label("كلمة مرور FTP (تُستخدم لهذه العملية فقط)"));
        ftpPanel.Children.Add(ftpPassword);
        ftpPanel.Children.Add(PageUi.Label("المجلد على الراوتر"));
        ftpPanel.Children.Add(ftpRemote);
        var ftpTls=new CheckBox
        {
            Content="استخدام FTPS المشفر عند دعمه من الراوتر",
            IsChecked=true,Foreground=PageUi.Gold,
            Margin=new Thickness(0,11,0,6)
        };
        var ftpPlain=new CheckBox
        {
            Content="أوافق صراحة على FTP غير المشفر (ترسل كلمة المرور دون تشفير؛ استخدم شبكة داخلية موثوقة فقط)",
            IsChecked=false,Foreground=PageUi.Gold,
            Margin=new Thickness(0,2,0,13)
        };
        ftpPanel.Children.Add(ftpTls);
        ftpPanel.Children.Add(ftpPlain);
        var ftpButtons=new WrapPanel {Margin=new Thickness(0,7,0,3)};
        var ftpDownload=PageUi.Button("تنزيل صفحات الراوتر",false);
        var ftpUpload=PageUi.Button("رفع القالب بعد النسخ الاحتياطي",false);
        var ftpCancel=PageUi.Button("إلغاء عملية FTP",false);
        ftpCancel.IsEnabled=false;
        ftpButtons.Children.Add(ftpDownload);
        ftpButtons.Children.Add(ftpUpload);
        ftpButtons.Children.Add(ftpCancel);
        ftpPanel.Children.Add(ftpButtons);
        var ftpStatus=PageUi.Status();
        ftpStatus.Text="لم يبدأ اتصال FTP. لا تُحفظ كلمة المرور محلياً.";
        ftpPanel.Children.Add(ftpStatus);
        stack.Children.Add(PageUi.Surface(ftpPanel));

        // RouterOS 6 UM database status: one on-demand read-only API request.
        // Unlike the v7 feature in Hadramout, there is intentionally no
        // optimize, rebuild or reboot action here.
        var umDatabasePanel=PageUi.Column();
        umDatabasePanel.Children.Add(PageUi.Title("حالة قاعدة User Manager v6"));
        umDatabasePanel.Children.Add(PageUi.Hint(
            "قراءة معلومات قاعدة مدير المستخدمين دون إعادة بناء أو تحسين قاعدة البيانات. " +
            "لا تُعرض خيارات RouterOS 7 غير المتوافقة مع RouterOS 6."));
        var readUmDatabase=PageUi.Button("قراءة حالة قاعدة User Manager");
        var umDatabaseStatus=PageUi.Status();
        umDatabaseStatus.Text="لم تُقرأ معلومات قاعدة User Manager بعد.";
        umDatabasePanel.Children.Add(readUmDatabase);
        umDatabasePanel.Children.Add(umDatabaseStatus);
        stack.Children.Add(PageUi.Surface(umDatabasePanel));
        readUmDatabase.Click+=async(_,_) =>
        {
            readUmDatabase.IsEnabled=false;
            umDatabaseStatus.Text="جارٍ قراءة حالة User Manager v6...";
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try
            {
                var db=await gateway.ReadUserManagerV6DatabaseStatusAsync(timeout.Token);
                umDatabaseStatus.Text=db.Diagnostic+
                    (db.Available
                        ? " | حجم القاعدة: "+db.DbSize+
                          " | مساحة القرص المتاحة: "+db.FreeSpace
                        : "");
            }
            catch(Exception ex)
            {
                umDatabaseStatus.Text="تعذّرت قراءة حالة القاعدة: "+ex.Message;
            }
            finally {readUmDatabase.IsEnabled=true;}
        };

        var future=PageUi.Column();
        future.Children.Add(PageUi.Title("بقية أدوات الشبكة"));
        future.Children.Add(PageUi.Hint(
            "إدارة Winbox المتقدمة وVPN والتشخيص عن بعد لم تكتمل بعد، " +
            "ولا تظهر لها خيارات تنفيذ غير مرتبطة بخدمات حقيقية."));
        stack.Children.Add(PageUi.Surface(future));
        Content=PageUi.Wrap("الأدوات الاحترافية",
            "تحرير صفحات Hotspot محلياً ونقلها إلى الراوتر مع النسخ الاحتياطي أولاً.",
            stack);

        var ftpTransfer=new HotspotFtpTransferService(new HotspotFtpWebRequestConnector());
        CancellationTokenSource? runningTransfer=null;
        HotspotFtpConnection FtpOptions()
        {
            if(!int.TryParse(ftpPort.Text.Trim(),out int port))
                throw new ArgumentException("منفذ FTP يجب أن يكون رقماً صحيحاً.");
            return new HotspotFtpConnection(
                ftpHost.Text.Trim(),port,ftpUser.Text.Trim(),
                ftpPassword.Password,ftpTls.IsChecked==true,
                ftpPlain.IsChecked==true);
        }
        async Task Transfer(Func<CancellationToken,Task> action)
        {
            if(runningTransfer is not null)return;
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(20));
            runningTransfer=timeout;
            ftpDownload.IsEnabled=ftpUpload.IsEnabled=false;
            ftpCancel.IsEnabled=true;
            try
            {
                await action(timeout.Token);
            }
            catch(OperationCanceledException)
            {
                ftpStatus.Text="أُلغيت العملية أو انتهت المهلة. إن كان الرفع قد بدأ، " +
                    "تحقق من الملفات على الراوتر قبل تكراره، واحتفظ بالنسخة الاحتياطية.";
            }
            catch(Exception ex)
            {
                ftpStatus.Text="تعذرت عملية FTP: "+ex.Message+
                    " | عند فشل الرفع راجع ملفات الراوتر والنسخة الاحتياطية قبل إعادة المحاولة.";
            }
            finally
            {
                runningTransfer=null;
                ftpDownload.IsEnabled=ftpUpload.IsEnabled=true;
                ftpCancel.IsEnabled=false;
            }
        }
        ftpCancel.Click+=(_,_)=>runningTransfer?.Cancel();
        ftpDownload.Click+=async(_,_)=>
        {
            if(runningTransfer is not null)return;
            var chosen=new OpenFolderDialog
            {
                Title="اختر المجلد الأب لتنزيل نسخة جديدة من صفحات Hotspot"
            };
            if(chosen.ShowDialog()!=true)return;
            var destination=chosen.FolderName;
            await Transfer(async ct=>
            {
                ftpStatus.Text="جارٍ قراءة ملفات الراوتر وتنزيلها إلى مجلد جديد...";
                var result=await ftpTransfer.DownloadSnapshotAsync(
                    FtpOptions(),ftpRemote.Text.Trim(),destination,ct);
                ftpStatus.Text="نُزل "+result.Files+" ملفاً بالكامل إلى "+result.LocalFolder+
                    " | لم يتم تعديل أي ملف على الراوتر.";
            });
        };
        ftpUpload.Click+=async(_,_)=>
        {
            if(runningTransfer is not null)return;
            if(workspace is null)
            {
                ftpStatus.Text="افتح مجلد قالب Hotspot محلياً أولاً.";
                return;
            }
            if(Unsaved())
            {
                ftpStatus.Text="احفظ تعديلات الصفحات في المحرر أولاً؛ لا يمكن رفع نسخة غير محفوظة.";
                return;
            }
            if(!PageUi.Confirm("رفع قالب Hotspot المحلي واستبدال الملفات المطابقة في " +
                "مجلد الراوتر المحدد؟ سيُنزل البرنامج نسخة ZIP كاملة من صفحات الراوتر " +
                "قبل الرفع. قد تتغير صفحة الدخول الحالية فور استبدال ملفاتها."))
                return;
            var backupDialog=new SaveFileDialog
            {
                Title="اختر اسم ZIP جديداً لحفظ نسخة الراوتر قبل الرفع",
                Filter="ZIP archive (*.zip)|*.zip",
                DefaultExt=".zip",
                AddExtension=true,
                OverwritePrompt=true,
                FileName="NajmCard-Router-Hotspot-Backup-"+
                    DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip"
            };
            if(backupDialog.ShowDialog()!=true)return;
            var sourceWorkspace=workspace;
            var backup=backupDialog.FileName;
            await Transfer(async ct=>
            {
                ftpStatus.Text="جارٍ تنزيل نسخة الراوتر الاحتياطية. لن يُرفع أي ملف قبل اكتمالها...";
                var result=await ftpTransfer.UploadWithBackupAsync(
                    FtpOptions(),sourceWorkspace,ftpRemote.Text.Trim(),backup,ct);
                ftpStatus.Text="اكتمل رفع "+result.UploadedFiles+
                    " ملفاً. جرى الاحتفاظ بنسخة الراوتر السابقة ("+result.BackupFiles+
                    " ملف) في "+result.BackupArchive;
            });
        };

        bool Unsaved() => selected is {Editable:true} &&
            !string.Equals(editor.Text,loadedText,StringComparison.Ordinal);
        bool ConfirmDiscard() => !Unsaved() ||
            PageUi.Confirm("هناك تغييرات في الصفحة لم تحفظ. هل تريد تجاهلها؟");

        void LoadSelected(HotspotTemplateFile? file)
        {
            if(workspace is null || file is null)
            {
                selected=null;
                editor.Text="";
                loadedText="";
                editor.IsReadOnly=true;
                save.IsEnabled=false;
                return;
            }
            if(!file.Editable)
            {
                selected=file;
                editor.Text="ملف صور أو مورد ثنائي؛ لا يُحرر كنص. سيُحفظ كما هو داخل ZIP.";
                loadedText=editor.Text;
                editor.IsReadOnly=true;
                save.IsEnabled=false;
                status.Text="المورد المحدد غير نصي؛ استخدم أدوات تصميم الصور لتعديله.";
                return;
            }
            try
            {
                var text=workspace.ReadText(file.RelativePath);
                selected=file;
                loadedText=text;
                editor.Text=text;
                editor.IsReadOnly=false;
                save.IsEnabled=true;
                status.Text="الملف المفتوح: "+file.RelativePath;
            }
            catch(Exception ex)
            {
                selected=null;
                editor.Text="";
                editor.IsReadOnly=true;
                save.IsEnabled=false;
                status.Text="تعذرت قراءة UTF-8 لهذا الملف: "+ex.Message;
            }
        }

        files.SelectionChanged += (_,_) =>
        {
            if(changingSelection) return;
            var next=files.SelectedItem as HotspotTemplateFile;
            if(ReferenceEquals(next,selected)) return;
            if(!ConfirmDiscard())
            {
                changingSelection=true;
                files.SelectedItem=selected;
                changingSelection=false;
                return;
            }
            LoadSelected(next);
        };

        choose.Click += (_,_) =>
        {
            if(!ConfirmDiscard()) return;
            var dialog=new OpenFolderDialog
            {
                Title="اختر مجلد Hotspot الذي يحتوي على login.html"
            };
            if(dialog.ShowDialog()!=true) return;
            try
            {
                var next=HotspotTemplateWorkspace.Open(dialog.FolderName);
                workspace=next;
                changingSelection=true;
                files.SelectedItem=null;
                files.ItemsSource=next.Inventory.Files;
                changingSelection=false;
                LoadSelected(null);
                folderLabel.Text="المجلد: "+dialog.FolderName;
                export.IsEnabled=true;
                status.Text="ملفات القالب: "+next.Inventory.Files.Count+
                    " | الملفات المقترحة غير الموجودة: "+
                    (next.Inventory.MissingRecommended.Count==0 ? "لا يوجد" :
                    string.Join("، ",next.Inventory.MissingRecommended));
            }
            catch(Exception ex) { status.Text="تعذر فتح القالب: "+ex.Message; }
        };

        save.Click += (_,_) =>
        {
            if(workspace is null || selected is not {Editable:true})return;
            try
            {
                var backup=workspace.SaveText(selected.RelativePath,editor.Text);
                loadedText=editor.Text;
                status.Text="حُفظ الملف محلياً. النسخة الاحتياطية السابقة: "+backup;
            }
            catch(Exception ex)
            {
                status.Text="لم يُحفظ الملف؛ احتفظ بالتعديلات ثم راجع السبب: "+ex.Message;
            }
        };

        export.Click += async (_,_) =>
        {
            if(workspace is null)return;
            if(Unsaved())
            {
                status.Text="احفظ تغييرات المحرر أو تجاهلها قبل تصدير القالب.";
                return;
            }
            var dialog=new SaveFileDialog
            {
                Title="حفظ نسخة ZIP من صفحات Hotspot",
                Filter="ZIP archive (*.zip)|*.zip",
                DefaultExt=".zip",
                AddExtension=true,
                OverwritePrompt=true,
                FileName="NajmCard-Hotspot-Pages.zip"
            };
            if(dialog.ShowDialog()!=true)return;
            choose.IsEnabled=save.IsEnabled=export.IsEnabled=false;
            status.Text="جارٍ التحقق من الملفات وتجهيز ZIP في المجلد المحدد...";
            try
            {
                var current=workspace;
                int count=await Task.Run(()=>current.ExportZip(dialog.FileName));
                status.Text="اكتمل تصدير "+count+" ملفاً إلى "+dialog.FileName+
                    ". ملف ZIP محلي فقط؛ لم يرفع شيء إلى MikroTik.";
            }
            catch(Exception ex)
            {
                status.Text="تعذر تصدير القالب: "+ex.Message;
            }
            finally
            {
                choose.IsEnabled=export.IsEnabled=true;
                save.IsEnabled=selected?.Editable==true;
            }
        };
    }
}
