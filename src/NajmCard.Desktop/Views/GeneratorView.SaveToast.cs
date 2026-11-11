using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace NajmCard.Desktop.Views;

public partial class GeneratorView
{
    private void WirePackageSaveNotification(Button saveButton)
    {
        var popup=new Popup
        {
            PlacementTarget=saveButton,Placement=PlacementMode.Bottom,
            AllowsTransparency=true,StaysOpen=false
        };
        var message=new TextBlock
        {
            FontFamily=new FontFamily("Arial"),FontSize=13,
            FontWeight=FontWeights.Bold,Foreground=Brushes.White,
            TextWrapping=TextWrapping.Wrap,MaxWidth=430
        };
        var surface=new Border
        {
            Child=message,CornerRadius=new CornerRadius(8),
            BorderThickness=new Thickness(1),
            BorderBrush=(Brush)new BrushConverter().ConvertFrom("#EAC48A")!,
            Padding=new Thickness(13,9,13,9),Margin=new Thickness(0,4,0,0)
        };
        popup.Child=surface;
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(7)};
        timer.Tick+=(_,_)=>{timer.Stop();popup.IsOpen=false;};
        saveButton.Click+=(_,_) =>
        {
            var saved=SaveCurrentPackage();
            message.Text=_status.Text;
            // Keep the exact validation failure visible after the popup closes.
            if(saved)_status.Text="";
            else _status.ToolTip=message.Text;
            surface.Background=(Brush)new BrushConverter().ConvertFrom(
                saved?"#176D4F":"#96243F")!;
            popup.IsOpen=true;
            timer.Stop();timer.Start();
        };
        Unloaded+=(_,_)=>{timer.Stop();popup.IsOpen=false;};
    }
}
