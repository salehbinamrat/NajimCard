using System.Net;
using System.Net.Sockets;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>Protects the real TCP RouterOS command boundary, not only a UI mock.</summary>
public sealed class RouterOsCommandSafetyTests
{
    [Theory]
    [InlineData("/ip/firewall/filter/remove")]
    [InlineData("/ip/address/add")]
    [InlineData("/system/script/add")]
    [InlineData("/execute")]
    [InlineData("/system/reset-configuration")]
    [InlineData("/tool/fetch")]
    [InlineData("/ip/hotspot/user/remove; /system/reboot")]
    public void Dangerous_Or_Unlisted_Write_Paths_Are_Rejected(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            RouterOsCommandPolicy.Validate(path, new Dictionary<string,string>
            {
                [".id"] = "*1"
            }));
    }

    [Theory]
    [InlineData("numbers", "*A,*B")]
    [InlineData("numbers", "123")]
    [InlineData("numbers", "admin")]
    public void UserManager_Profile_Activation_Requires_One_Router_Internal_Id(string field,string value)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            RouterOsCommandPolicy.Validate(
                "/tool/user-manager/user/create-and-activate-profile",
                new Dictionary<string,string> { [field] = value, ["customer"]="admin", ["profile"]="gold" }));
    }

    [Theory]
    [InlineData("file","backup.rsc")]
    [InlineData("follow","yes")]
    [InlineData("interval","1s")]
    public void RouterOS_Print_May_Not_Write_Files_Or_Keep_Live_Jobs(string key,string value)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            RouterOsCommandPolicy.Validate("/ip/hotspot/user/print",
                new Dictionary<string,string> { [key] = value }));
    }

    [Theory]
    [InlineData("on-login")]
    [InlineData("on-logout")]
    [InlineData("source")]
    public void Rejects_RouterOS_Script_Properties_From_Any_Mutation(string key)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            RouterOsCommandPolicy.Validate("/ip/hotspot/user/add",
                new Dictionary<string,string> { ["name"] = "voucher", [key] = ":put 1" }));
    }

    [Theory]
    [InlineData("*1,*2")]
    [InlineData("client")]
    [InlineData("*Z")]
    public void Single_User_Remove_Must_Use_Exactly_One_Valid_Internal_Id(string id)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            RouterOsCommandPolicy.Validate("/tool/user-manager/user/remove",
                new Dictionary<string,string> { [".id"] = id }));
    }

    [Fact]
    public void Allows_Supported_UserManager_RouterOS6_And_ReadOnly_Menus()
    {
        RouterOsCommandPolicy.Validate("/system/resource/print", null);
        RouterOsCommandPolicy.Validate("/tool/user-manager/user/print",
            new Dictionary<string,string> { [".proplist"] = ".id,username,actual-profile,disabled" });
        RouterOsCommandPolicy.Validate("/tool/user-manager/user/add",
            new Dictionary<string,string> { ["username"]="001234",["password"]="P1",["customer"]="admin" });
        RouterOsCommandPolicy.Validate("/tool/user-manager/user/create-and-activate-profile",
            new Dictionary<string,string> { ["numbers"]="*A",["customer"]="admin",["profile"]="gold" });
        RouterOsCommandPolicy.Validate("/tool/user-manager/user/set",
            new Dictionary<string,string> { [".id"]="*A",["disabled"]="yes" });
        RouterOsCommandPolicy.Validate("/tool/user-manager/user/remove",
            new Dictionary<string,string> { [".id"]="*A" });
    }

    [Fact]
    public async Task Disallowed_Router_Command_Never_Reaches_Live_TCP_Connection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port=((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var server=Task.Run(async () =>
        {
            using var remote=await listener.AcceptTcpClientAsync(deadline.Token);
            using var stream=remote.GetStream();
            Assert.Equal("/login",
                (await RouterOsProtocol.ReadSentenceAsync(stream, deadline.Token))[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream, ["!done"], deadline.Token);
            var first=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/system/resource/print",first[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!re","=cpu-load=30"],deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);
        },deadline.Token);
        await using (var client=await RouterOsClient.ConnectAsync(
            new RouterEndpoint("127.0.0.1",port,"admin","pass"),deadline.Token))
        {
            await Assert.ThrowsAnyAsync<ArgumentException>(
                async ()=>await client.CommandAsync("/ip/firewall/filter/remove",
                    new Dictionary<string,string>{[".id"]="*A"},deadline.Token));
            var rows=await client.CommandAsync("/system/resource/print",
                cancellationToken:deadline.Token);
            Assert.Equal("30",Assert.Single(rows)["cpu-load"]);
        }
        await server;
    }
}
