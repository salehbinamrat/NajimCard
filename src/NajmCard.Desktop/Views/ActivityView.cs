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
            Margin=new Thickness(0,0,8,0)};
        var search=PageUi.Input();
        search.MinWidth=250;
        search.Margin=new Thickness(0,0,8,0);
        var refresh=PageUi.Button("تحديث",false);
        var controls=new WrapPanel{Margin=new Thickness(0,0,0,8)};
        var categoryField=PageUi.Column();
        categoryField.Children.Add(PageUi.Label("الفئة"));
        categoryField.Children.Add(category);
        var searchField=PageUi.Column();
        searchField.Children.Add(PageUi.Label("البحث"));
        searchField.Children.Add(search);
        refresh.VerticalAlignment=VerticalAlignment.Bottom;
        controls.Children.Add(categoryField);
        controls.Children.Add(searchField);
        controls.Children.Add(refresh);
        root.Children.Add(controls);
        var grid=PageUi.Table(("الوقت","Timestamp"),("الفئة","Category"),
            ("العملية","Action"),("النتيجة","Status"),("التفاصيل","Detail"));
        grid.IsReadOnly=true;grid.CanUserAddRows=false;grid.CanUserDeleteRows=false;
        grid.EnableRowVirtualization=true;grid.MinHeight=330;grid.MaxHeight=400;
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
            PageUi.Surface(root));
        int current=0;
        int requestedPage=0;
        int requestVersion=0;
        bool busy=false;
        async Task LoadAsync(int page)
        {
            requestedPage=page;
            requestVersion++;
            if(busy)return;
            busy=true;
            previous.IsEnabled=next.IsEnabled=false;
            try
            {
                int version;
                do
                {
                    version=requestVersion;
                    var key=(category.SelectedItem as Category)?.Key;
                    var query=search.Text;
                    var pageToLoad=requestedPage;
                    try
                    {
                        var result=await Task.Run(()=>store.ReadActivityPage(key,query,pageToLoad,250));
                        if(version!=requestVersion)continue;
                        current=result.Page;
                        grid.ItemsSource=result.Items;
                        int pages=Math.Max(1,(result.Total+result.PageSize-1)/result.PageSize);
                        pageLabel.Text="صفحة "+(current+1)+" من "+pages+" | "+result.Total+" عملية";
                        previous.IsEnabled=current>0;
                        next.IsEnabled=current+1<pages;
                        status.Text="قراءة محلية مؤكدة دون أي طلب RouterOS.";
                    }
                    catch(Exception ex)
                    {
                        if(version!=requestVersion)continue;
                        grid.ItemsSource=null;
                        pageLabel.Text="";
                        status.Text="تعذرت قراءة السجل: "+ex.Message;
                    }
                }
                while(version!=requestVersion);
            }
            finally{busy=false;}
        }
        refresh.Click+=async(_,_)=>await LoadAsync(0);
        category.SelectionChanged+=async(_,_)=>await LoadAsync(0);
        search.KeyDown+=async(_,e)=>
        {
            if(e.Key==System.Windows.Input.Key.Enter)await LoadAsync(0);
        };
        previous.Click+=async(_,_)=>await LoadAsync(Math.Max(0,current-1));
        next.Click+=async(_,_)=>await LoadAsync(current+1);
        Loaded+=async(_,_)=>await LoadAsync(0);
    }
}
