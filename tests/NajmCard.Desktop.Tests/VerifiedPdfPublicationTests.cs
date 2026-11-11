using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NajmCard.Desktop.Views;
using Xunit;

namespace NajmCard.Desktop.Tests;

/// <summary>
/// Tests the file transaction without a router or physical printer. The
/// renderer delegate acts like the WPF/PDF worker and must not publish a PDF
/// if its original SENT credentials are no longer verified at completion.
/// </summary>
public sealed class VerifiedPdfPublicationTests
{
    private static Task Publish(
        Func<string,Task> render,Func<bool> verify,Action persist,string file)
    {
        var method=typeof(PrintingView).GetMethod(
            "StageAndPublishVerifiedPdfAsync",
            BindingFlags.Static|BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Task>(method.Invoke(null,
            [render,verify,persist,file]));
    }

    private static string NewFolder()
    {
        var folder=Path.Combine(Path.GetTempPath(),
            "najm-verified-pdf-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public async Task Invalidate_During_Render_Does_Not_Replace_A_Previous_Pdf()
    {
        var folder=NewFolder();
        try
        {
            var target=Path.Combine(folder,"verified.pdf");
            File.WriteAllText(target,"existing safe report");
            bool persisted=false;
            await Assert.ThrowsAsync<InvalidDataException>(()=>Publish(
                path => {File.WriteAllText(path,"unverified new report");
                    return Task.CompletedTask;},
                ()=>false,
                ()=>persisted=true,
                target));
            Assert.False(persisted);
            Assert.Equal("existing safe report",File.ReadAllText(target));
            Assert.Empty(Directory.GetFiles(folder,".najm-verified-*.pdf"));
        }
        finally {Directory.Delete(folder,true);}
    }

    [Fact]
    public async Task Failed_Snapshot_Save_Does_Not_Replace_An_Existing_Pdf()
    {
        var folder=NewFolder();
        try
        {
            var target=Path.Combine(folder,"verified.pdf");
            File.WriteAllText(target,"existing safe report");
            await Assert.ThrowsAsync<IOException>(()=>Publish(
                path => {File.WriteAllText(path,"new report");
                    return Task.CompletedTask;},
                ()=>true,
                ()=>throw new IOException("simulated SQLite snapshot failure"),
                target));
            Assert.Equal("existing safe report",File.ReadAllText(target));
            Assert.Empty(Directory.GetFiles(folder,".najm-verified-*.pdf"));
        }
        finally {Directory.Delete(folder,true);}
    }

    [Fact]
    public async Task Validated_Pdf_Is_Atomically_Published_Only_After_Snapshot_Save()
    {
        var folder=NewFolder();
        try
        {
            var target=Path.Combine(folder,"verified.pdf");
            File.WriteAllText(target,"prior verified report");
            bool persisted=false;
            await Publish(
                path => {File.WriteAllText(path,"new confirmed report");
                    return Task.CompletedTask;},
                ()=>true,
                ()=>{Assert.Equal("prior verified report",File.ReadAllText(target));
                    persisted=true;},
                target);
            Assert.True(persisted);
            Assert.Equal("new confirmed report",File.ReadAllText(target));
            Assert.Empty(Directory.GetFiles(folder,".najm-verified-*.pdf"));
        }
        finally {Directory.Delete(folder,true);}
    }
}
