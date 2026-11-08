using System.Windows;
using System.Windows.Controls;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

/// <summary>Hadramout-style local operation audit with indexed, bounded SQLite paging.</summary>
public sealed class ActivityView : UserControl
{
    private sealed record Category(string Name,string Key)
    {
        public override string ToString()=>Name;
    }
    public ActivityView(VoucherStore store)
    {
        var categories=new[]
        {
            new Category("ALL",""),new Category("NETWORK","NETWORK"),
            new Category("USERMAN","USERMAN"),new Category("MIKROTIK","MIKROTIK"),
            new Category("PRINT","PRINT"),new Category("EXPORT","EXPORT"),
            new Category("GENERAL","GENERAL"),new Category("CARDS","CARDS"),
            new Category("ROUTER","ROUTER")
        };
        var root=PageUi.Column();
        // Primary title is presented once in MainWindow.
        root.Children.Add(PageUi.Hint("سجل العمليات المحلي الحقيقي؛ لا يتصل بالراوتر عند التصفية أو الانتقال بين الصفحات."));
        var category=new ComboBox{ItemsSource=categories,SelectedIndex=0,MinWidth=160,
            Margin=new Thickness(5)};
        var search=PageUi.Input();
        search.MinWidth=280;
        var refresh=PageUi.Button("تحديث",false);
        var controls=new WrapPanel();
        controls.Children.Add(PageUi.Label("الفئة"));controls.Children.Add(category);
        controls.Children.Add(PageUi.Label("البحث"));controls.Children.Add(search);
        controls.Children.Add(refresh);
        root.Children.Add(controls);
        var grid=PageUi.Table(("الوقت","Timestamp"),("الفئة","Category"),
            ("العملية","Action"),("النتيجة","Status"),("التفاصيل","Detail"));
        grid.IsReadOnly=true;grid.CanUserAddRows=false;grid.CanUserDeleteRows=false;
        grid.EnableRowVirtualization=true;grid.MinHeight=365;
        root.Children.Add(grid);
        var pager=new WrapPanel{Margin=new Thickness(0,5,0,6)};
        var previous=PageUi.Button("السابق",false);
        var next=PageUi.Button("التالي",false);
        var pageLabel=PageUi.Status();
        pager.Children.Add(previous);pager.Children.Add(next);pager.Children.Add(pageLabel);
        root.Children.Add(pager);
        var status=PageUi.Status();
        root.Children.Add(status);
        Content=PageUi.Wrap("النشاط","تصفية وتقسيم صفحات سجل نجم كارد المحلي.",
            PageUi.Surface(root,"#9B709A"));
        int current=0;
        bool busy=false;
        async Task LoadAsync()
        {
            if(busy)return;
            busy=true;refresh.IsEnabled=false;
            try
            {
                var key=(category.SelectedItem as Category)?.Key;
                var result=await Task.Run(()=>store.ReadActivityPage(key,search.Text,current,250));
                current=result.Page;
                grid.ItemsSource=result.Items;
                int pages=Math.Max(1,(result.Total+result.PageSize-1)/result.PageSize);
                pageLabel.Text="صفحة "+(current+1)+" من "+pages+" | "+result.Total+" عملية";
                previous.IsEnabled=current>0;next.IsEnabled=current+1<pages;
                status.Text="قراءة محلية مؤكدة دون أي طلب RouterOS.";
            }
            catch(Exception ex)
            {
                grid.ItemsSource=null;status.Text="تعذرت قراءة السجل: "+ex.Message;
            }
            finally{busy=false;refresh.IsEnabled=true;}
        }
        refresh.Click+=async(_,_)=>{current=0;await LoadAsync();};
        category.SelectionChanged+=async(_,_)=>{current=0;await LoadAsync();};
        search.KeyDown+=async(_,e)=>
        {
            if(e.Key==System.Windows.Input.Key.Enter)
            {current=0;await LoadAsync();}
        };
        previous.Click+=async(_,_)=>{current=Math.Max(0,current-1);await LoadAsync();};
        next.Click+=async(_,_)=>{current++;await LoadAsync();};
        Loaded+=async(_,_)=>await LoadAsync();
    }
}
