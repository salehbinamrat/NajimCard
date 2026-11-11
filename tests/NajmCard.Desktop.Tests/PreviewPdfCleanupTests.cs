using System.IO;
using System.Reflection;
using NajmCard.Desktop.Views;
using Xunit;

namespace NajmCard.Desktop.Tests;

public sealed class PreviewPdfCleanupTests
{
    [Fact]
    public void Preview_Cleanup_Is_Bounded_And_Does_Not_Delete_Real_Archives()
    {
        var root=Directory.CreateTempSubdirectory("najm-preview-cleanup-").FullName;
        try
        {
            var now=DateTime.UtcNow;
            for(var i=0;i<128;i++)
            {
                var path=Path.Combine(root,$"Najm_{i:D3}.pdf");
                File.WriteAllText(path,"preview");
                File.SetLastWriteTimeUtc(path,now.AddMinutes(-i));
            }
            var expired=Path.Combine(root,"Najm_expired.pdf");
            File.WriteAllText(expired,"old preview");
            File.SetLastWriteTimeUtc(expired,now.AddDays(-3));
            var archive=Path.Combine(root,"كروت-مؤكدة-50.pdf");
            File.WriteAllText(archive,"saved archive");
            File.SetLastWriteTimeUtc(archive,now.AddDays(-30));

            var cleanup=typeof(PrintingView).GetMethod("PrunePreviewPdfFiles",
                BindingFlags.Static|BindingFlags.NonPublic);
            Assert.NotNull(cleanup);
            cleanup.Invoke(null,[root,now]);

            Assert.Equal(120,Directory.GetFiles(root,"Najm_*.pdf").Length);
            Assert.False(File.Exists(expired));
            Assert.True(File.Exists(Path.Combine(root,"Najm_000.pdf")));
            Assert.False(File.Exists(Path.Combine(root,"Najm_127.pdf")));
            Assert.Equal("saved archive",File.ReadAllText(archive));
        }
        finally{Directory.Delete(root,true);}
    }
}
