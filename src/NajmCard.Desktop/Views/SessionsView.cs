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

public class SessionsView : UserControl
{
    public SessionsView(RouterGateway gateway)
    {
        var grid = PageUi.Table(("الكرت", "User"), ("عنوان IP", "Address"), ("MAC", "MacAddress"), ("المدة", "Uptime"));
        var status = PageUi.Status();var stack = PageUi.Column();var refresh = PageUi.Button("تحديث المتصلين");
        stack.Children.Add(refresh);stack.Children.Add(grid);stack.Children.Add(status);
        Content = PageUi.Wrap("المتصلون حالياً", "جلسات Hotspot الفعلية المتصلة بالراوتر، بلا بيانات تجريبية.",PageUi.Surface(stack,"#568E78"));
        async Task Load() { try { var x = await gateway.ReadSessionsAsync();grid.ItemsSource=x;status.Text = "جلسات متصلة: " + x.Count;}catch(Exception e){status.Text=e.Message;} }
        refresh.Click += async (_, _) => await Load();Loaded += async (_, _) => await Load();
    }
}
