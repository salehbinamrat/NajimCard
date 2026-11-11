using System.Windows.Controls;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

public class SettingsView : UserControl
{
    public SettingsView(VoucherStore store)
    {
        var stack=PageUi.Column();var status=PageUi.Status();var backup=PageUi.Button("إنشاء نسخة احتياطية الآن");
        stack.Children.Add(PageUi.Label("مسار بيانات نجم كارد المستقلة"));stack.Children.Add(PageUi.Hint(store.DirectoryPath));
        stack.Children.Add(PageUi.Hint("كلمات مرور الكروت محمية بميزة Windows DPAPI لحساب المستخدم المحلي. نقل قاعدة البيانات إلى مستخدم آخر لا يكفي لاستعادتها."));
        stack.Children.Add(backup);stack.Children.Add(status);
        Content=PageUi.Wrap("الإعدادات والنسخ الاحتياطي", "ملفات البيانات منفصلة عن برنامج حضرموت ولا تشاركه أي قاعدة أو ترخيص.",PageUi.Surface(stack,"#6557A4"));
        backup.Click+=(_,_)=>{try{status.Text="تم إنشاء النسخة: "+store.Backup();}catch(Exception ex){status.Text="فشل النسخ: "+ex.Message;}};
    }
}
