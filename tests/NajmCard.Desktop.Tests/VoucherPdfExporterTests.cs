using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;
public sealed class VoucherPdfExporterTests
{
    private static void RunSta(Action work)
    {
        Exception? error=null;
        var thread=new Thread(()=>
        {
            try {work();}catch(Exception ex){error=ex;}
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();thread.Join();
        if(error is not null)throw error;
    }
    private static FixedDocument Sample()
    {
        var page=new FixedPage {Width=794,Height=1122};
        page.Children.Add(new TextBlock
        {
            Text="نجوم نت · 0012345678",FontSize=22,FontWeight=FontWeights.Bold
        });
        var doc=new FixedDocument();
        var content=new PageContent();
        ((System.Windows.Markup.IAddChild)content).AddChild(page);
        doc.Pages.Add(content);
        return doc;
    }
    [Fact]
    public void Saves_An_Actual_Nonempty_Pdf_From_The_Same_Arabic_Wpf_Card_Page()
    {
        RunSta(()=>
        {
            var path=Path.Combine(Path.GetTempPath(),"najm-pdf-"+Guid.NewGuid()+".pdf");
            try
            {
                VoucherPdfExporter.Export(Sample(),path);
                var data=File.ReadAllBytes(path);
                Assert.True(data.Length>1500);
                Assert.Equal("%PDF-",Encoding.ASCII.GetString(data,0,5));
            }
            finally { if(File.Exists(path))File.Delete(path); }
        });
    }
    [Fact]
    public void Wrong_Extension_Or_Empty_Document_Cannot_Replace_Preexisting_User_Files()
    {
        RunSta(()=>
        {
            var folder=Path.Combine(Path.GetTempPath(),"najm-pdf-"+Guid.NewGuid());
            Directory.CreateDirectory(folder);
            try
            {
                var text=Path.Combine(folder,"personal.txt");
                File.WriteAllText(text,"KEEP THIS FILE");
                Assert.Throws<ArgumentException>(()=>VoucherPdfExporter.Export(Sample(),text));
                Assert.Equal("KEEP THIS FILE",File.ReadAllText(text));
                var pdf=Path.Combine(folder,"old.pdf");
                File.WriteAllText(pdf,"ORIGINAL PDF");
                Assert.Throws<ArgumentException>(()=>VoucherPdfExporter.Export(new FixedDocument(),pdf));
                Assert.Equal("ORIGINAL PDF",File.ReadAllText(pdf));
            }
            finally {Directory.Delete(folder,true);}
        });
    }
}
