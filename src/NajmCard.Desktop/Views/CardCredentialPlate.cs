using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NajmCard.Core.Models;

namespace NajmCard.Desktop.Views;

/// <summary>
/// Shared WPF credential geometry used by both the editable card preview and
/// the FixedDocument/PDF pipeline. Native card measurements are millimetres
/// converted to DIPs; only the outer on-screen Viewbox may enlarge the image.
/// </summary>
internal static class CardCredentialPlate
{
    private static double Dip(double mm)=>mm*96d/25.4d;

    internal static Border Build(TextBlock text,double cardWidthMm,double cardHeightMm)
    {
        text.Margin=new Thickness(0);
        text.TextWrapping=TextWrapping.NoWrap;
        text.TextTrimming=TextTrimming.None;
        text.TextAlignment=TextAlignment.Center;
        text.VerticalAlignment=VerticalAlignment.Center;
        var plate=new Border
        {
            Background=Brushes.Transparent,
            BorderBrush=Brushes.Transparent,
            BorderThickness=new Thickness(0),
            Padding=new Thickness(1),
            ClipToBounds=true,
            Child=new Viewbox
            {
                Stretch=Stretch.Uniform,
                StretchDirection=StretchDirection.DownOnly,
                HorizontalAlignment=HorizontalAlignment.Center,
                VerticalAlignment=VerticalAlignment.Center,
                Child=text
            }
        };
        Resize(plate,cardWidthMm,cardHeightMm);
        return plate;
    }

    internal static void ApplyNumberBacking(Border plate,VoucherCardAppearance appearance,bool hasArtwork)
    {
        var needsBacking=hasArtwork || appearance.NumberNeedsBacking();
        var backing=PrintingView.ChooseArtworkTextBacking(appearance.UsernameColor);
        var contrastingBrush=backing.R<128?Brushes.Black:Brushes.White;
        var text=(TextBlock)((Viewbox)plate.Child).Child;
        text.Background=needsBacking?contrastingBrush:Brushes.Transparent;
        text.Padding=new Thickness(needsBacking?2:0,0,needsBacking?2:0,0);
    }

    internal static void Resize(Border plate,double cardWidthMm,double cardHeightMm)
    {
        if(!double.IsFinite(cardWidthMm)||!double.IsFinite(cardHeightMm)||
           cardWidthMm<=0||cardHeightMm<=0)
            throw new ArgumentOutOfRangeException(nameof(cardWidthMm),"أبعاد الكرت غير صالحة.");
        plate.Width=Math.Max(12,Dip(cardWidthMm)*0.78);
        plate.Height=Math.Max(12,Dip(cardHeightMm)*0.34);
    }
}
