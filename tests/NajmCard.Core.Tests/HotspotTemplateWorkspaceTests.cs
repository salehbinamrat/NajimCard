using System.IO;
using System.IO.Compression;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// Real filesystem and ZIP regression tests for safe local MikroTik Hotspot
/// template handling. Inspired by Hadramout core/hotspot.py, without FTP writes.
/// </summary>
public sealed class HotspotTemplateWorkspaceTests
{
    private static string Root()
    {
        var path=Path.Combine(Path.GetTempPath(),"najm-hotspot-template-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Requires_LoginHtml_At_Actual_Template_Root()
    {
        var root=Root();
        try
        {
            Directory.CreateDirectory(Path.Combine(root,"sub"));
            File.WriteAllText(Path.Combine(root,"sub","login.html"),"wrong level");
            Assert.ThrowsAny<InvalidDataException>(()=>HotspotTemplateWorkspace.Open(root));
            File.WriteAllText(Path.Combine(root,"login.html"),"$(username)");
            var workspace=HotspotTemplateWorkspace.Open(root);
            Assert.Equal(2,workspace.Inventory.Files.Count);
            Assert.Contains("alogin.html",workspace.Inventory.MissingRecommended);
            Assert.Contains("status.html",workspace.Inventory.MissingRecommended);
            Assert.Contains("logout.html",workspace.Inventory.MissingRecommended);
            Assert.Equal("$(username)",workspace.ReadText("login.html"));
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public void Saves_Utf8_With_Mikrotik_Variables_Intact_And_Backs_Up_Previous_File()
    {
        var root=Root();
        try
        {
            var login=Path.Combine(root,"login.html");
            File.WriteAllText(login,"old $(link-login-only)");
            var workspace=HotspotTemplateWorkspace.Open(root);
            const string updated="<html lang=\"ar\">اسم المستخدم $(username) $(link-login-only)</html>";
            var backup=workspace.SaveText("login.html",updated);
            Assert.True(File.Exists(backup));
            Assert.Equal("old $(link-login-only)",File.ReadAllText(backup));
            Assert.Equal(updated,File.ReadAllText(login));
            Assert.Equal(updated,workspace.ReadText("login.html"));
            Assert.StartsWith(Path.Combine(root,".najm-backups"),
                backup,StringComparison.OrdinalIgnoreCase);
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public void Cannot_Read_Or_Write_Path_Outside_Opened_Template()
    {
        var parent=Root();
        try
        {
            var root=Path.Combine(parent,"site");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root,"login.html"),"valid");
            var outside=Path.Combine(parent,"secret.txt");
            File.WriteAllText(outside,"private");
            var workspace=HotspotTemplateWorkspace.Open(root);
            Assert.ThrowsAny<ArgumentException>(()=>workspace.ReadText("../secret.txt"));
            Assert.ThrowsAny<ArgumentException>(()=>workspace.SaveText("../secret.txt","hack"));
            Assert.ThrowsAny<ArgumentException>(()=>workspace.ReadText(outside));
            Assert.Equal("private",File.ReadAllText(outside));
        }
        finally {Directory.Delete(parent,true);}
    }

    [Fact]
    public void Zip_Export_Preserves_All_Nested_Assets_But_Not_Internal_Backups()
    {
        var parent=Root();
        try
        {
            var root=Path.Combine(parent,"site");
            Directory.CreateDirectory(Path.Combine(root,"css"));
            Directory.CreateDirectory(Path.Combine(root,"images"));
            File.WriteAllText(Path.Combine(root,"login.html"),"$(username)");
            File.WriteAllText(Path.Combine(root,"css","style.css"),"body{color:#fff}");
            File.WriteAllBytes(Path.Combine(root,"images","logo.webp"),[1,2,3,4]);
            var workspace=HotspotTemplateWorkspace.Open(root);
            workspace.SaveText("login.html","new $(username)");
            var target=Path.Combine(parent,"export.zip");
            var total=workspace.ExportZip(target);
            Assert.Equal(3,total);
            using var archive=ZipFile.OpenRead(target);
            var names=archive.Entries.Select(x=>x.FullName).OrderBy(x=>x).ToArray();
            Assert.Equal(["css/style.css","images/logo.webp","login.html"],names);
            var entry=archive.GetEntry("login.html")!;
            using var reader=new StreamReader(entry.Open());
            Assert.Equal("new $(username)",reader.ReadToEnd());
            Assert.DoesNotContain(names,x=>x.Contains(".najm-backups"));
        }
        finally {Directory.Delete(parent,true);}
    }

    [Fact]
    public void Refuses_To_Overwrite_External_Changes_After_Workspace_Open()
    {
        var root=Root();
        try
        {
            var login=Path.Combine(root,"login.html");
            File.WriteAllText(login,"original");
            var workspace=HotspotTemplateWorkspace.Open(root);
            File.WriteAllText(login,"edited outside NajmCard");
            Assert.ThrowsAny<InvalidOperationException>(()=>
                workspace.SaveText("login.html","unwanted overwrite"));
            Assert.Equal("edited outside NajmCard",File.ReadAllText(login));
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public void Rejects_Binary_Text_Editing_And_Export_Inside_Source_Tree()
    {
        var parent=Root();
        try
        {
            var root=Path.Combine(parent,"site");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root,"login.html"),"valid");
            File.WriteAllBytes(Path.Combine(root,"image.png"),[0,1,2]);
            var workspace=HotspotTemplateWorkspace.Open(root);
            Assert.ThrowsAny<InvalidOperationException>(()=>workspace.ReadText("image.png"));
            Assert.ThrowsAny<InvalidOperationException>(()=>workspace.SaveText("image.png","bad"));
            Assert.ThrowsAny<ArgumentException>(()=>
                workspace.ExportZip(Path.Combine(root,"export.zip")));
        }
        finally {Directory.Delete(parent,true);}
    }
}
