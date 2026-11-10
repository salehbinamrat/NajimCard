using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NajmCard.Desktop.Views;

/// <summary>
/// Built-in Najm Net vector card. No external downloads and no hard-coded
/// package, price, duration or telephone information on actual printed cards.
/// The preview and PDF share exactly the same background artwork.
/// </summary>
internal static class NajmNetCardArtwork
{
    internal static bool IsDefault(string networkTitle,string templateImagePath,
        string paperColor) =>
        (string.Equals(networkTitle,"نجم نت",StringComparison.Ordinal) ||
         string.Equals(networkTitle,"نجم كارد",StringComparison.Ordinal)) &&
        string.IsNullOrWhiteSpace(templateImagePath) &&
        string.Equals(paperColor,"#FFF6EB",StringComparison.OrdinalIgnoreCase);

    internal static FrameworkElement Create(double width,double height)
    {
        var canvas=new Canvas
        {
            Width=500,Height=190,ClipToBounds=true,
            Background=new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#FAF2FF")!,
                (Color)ColorConverter.ConvertFromString("#E5C5FF")!,0)
        };
        static Brush Fill(string a,string b,double angle)=>
            new LinearGradientBrush((Color)ColorConverter.ConvertFromString(a)!,
                (Color)ColorConverter.ConvertFromString(b)!,angle);
        void Polygon(string path,string a,string b,double angle,double opacity=1)
        {
            canvas.Children.Add(new Path
            {
                Data=Geometry.Parse(path),Fill=Fill(a,b,angle),Opacity=opacity
            });
        }
        Polygon("M0,0 L180,0 L62,190 L0,190 Z","#5B0D99","#C149F8",30);
        Polygon("M0,0 L85,0 L0,124 Z","#BB5FFA","#45036A",20);
        Polygon("M78,0 L202,0 L95,110 Z","#FFFFFF","#DB9DFD",80,.55);
        Polygon("M500,0 L390,0 L465,190 L500,190 Z","#E8B3FF","#4F19A5",65);
        Polygon("M500,0 L420,0 L500,105 Z","#A643EC","#F5D5FF",40,.85);
        Polygon("M0,142 L200,190 L0,190 Z","#5B149E","#E6A0F9",25,.65);
        Polygon("M500,148 L310,190 L500,190 Z","#7320BE","#F4D2FF",155,.78);
        var leftDisc=new Ellipse
        {
            Width=172,Height=180,Fill=Fill("#470675","#AC37EB",12),
            Stroke=new SolidColorBrush(Color.FromRgb(235,183,255)),
            StrokeThickness=3,Opacity=.95
        };
        Canvas.SetLeft(leftDisc,-62);Canvas.SetTop(leftDisc,5);
        canvas.Children.Add(leftDisc);
        var central=new Border
        {
            Width=280,Height=66,CornerRadius=new CornerRadius(13),
            BorderThickness=new Thickness(2),
            BorderBrush=new SolidColorBrush(Color.FromRgb(150,59,192)),
            Background=new SolidColorBrush(Color.FromArgb(190,255,255,255))
        };
        Canvas.SetLeft(central,113);Canvas.SetTop(central,63);
        canvas.Children.Add(central);
        var footer=new Border
        {
            Width=275,Height=26,CornerRadius=new CornerRadius(8),
            BorderThickness=new Thickness(1),
            BorderBrush=new SolidColorBrush(Color.FromRgb(179,105,211)),
            Background=new SolidColorBrush(Color.FromArgb(190,255,255,255))
        };
        Canvas.SetLeft(footer,135);Canvas.SetTop(footer,155);
        canvas.Children.Add(footer);
        var star=new Path
        {
            Data=Geometry.Parse("M0,-14 L4,-4 L15,-4 L6,3 L9,14 L0,8 L-9,14 L-6,3 L-15,-4 L-4,-4 Z"),
            Fill=Fill("#BF1041","#FF4C3B",40),
            Width=30,Height=28,Stretch=Stretch.Fill
        };
        Canvas.SetLeft(star,320);Canvas.SetTop(star,12);
        canvas.Children.Add(star);
        return new Viewbox
        {
            Width=width,Height=height,
            Stretch=Stretch.Fill,Child=canvas,
            HorizontalAlignment=HorizontalAlignment.Stretch,
            VerticalAlignment=VerticalAlignment.Stretch,
            IsHitTestVisible=false
        };
    }
}
