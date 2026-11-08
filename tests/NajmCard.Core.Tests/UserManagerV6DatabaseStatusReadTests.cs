using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// Source-grounded Hadramout database_maintenance_status adaptation.
/// This boundary must never request maintenance, optimization, or reboot.
/// </summary>
public sealed class UserManagerV6DatabaseStatusReadTests
{
    private sealed class Spy : IRouterCommandClient
    {
        public List<string> Commands=[];
        public string? Failure;
        public IReadOnlyList<IReadOnlyDictionary<string,string>> Data=[
            new Dictionary<string,string>
            {
                ["db-size"]="23.5MiB",
                ["free-disk-space"]="512MiB",
                ["internal-secret"]="must not be returned"
            }
        ];
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            Assert.Equal("/tool/user-manager/database/print",command);
            Assert.Null(attributes);
            if(Failure is not null) throw new RouterOsTrapException(Failure);
            return Task.FromResult(Data);
        }
    }

    [Fact]
    public async Task Reads_RouterOS6_Database_Only_And_Exposes_No_Unrelated_Fields()
    {
        var spy=new Spy();
        var status=await new RouterGateway(spy).ReadUserManagerV6DatabaseStatusAsync();
        Assert.True(status.Available);
        Assert.Equal("23.5MiB",status.DbSize);
        Assert.Equal("512MiB",status.FreeSpace);
        Assert.DoesNotContain("must not be returned",
            status.Diagnostic+status.DbSize+status.FreeSpace);
        Assert.Equal(["/tool/user-manager/database/print"],spy.Commands);
    }

    [Fact]
    public async Task Missing_Fields_Return_Unknown_Not_Invented_Zero()
    {
        var spy=new Spy { Data=[new Dictionary<string,string>()] };
        var status=await new RouterGateway(spy).ReadUserManagerV6DatabaseStatusAsync();
        Assert.True(status.Available);
        Assert.Equal("غير معروف",status.DbSize);
        Assert.Equal("غير معروف",status.FreeSpace);
    }

    [Fact]
    public async Task Empty_Result_Does_Not_Claim_UserManager_Is_Installed()
    {
        var spy=new Spy {Data=[]};
        var status=await new RouterGateway(spy).ReadUserManagerV6DatabaseStatusAsync();
        Assert.False(status.Available);
        Assert.Single(spy.Commands);
    }

    [Fact]
    public async Task RouterOS6_Trap_Reports_Unavailable_Without_Sending_Any_Write()
    {
        var spy=new Spy {Failure="no such command (credential:secret)"};
        var status=await new RouterGateway(spy).ReadUserManagerV6DatabaseStatusAsync();
        Assert.False(status.Available);
        Assert.DoesNotContain("secret",status.Diagnostic);
        Assert.Equal(["/tool/user-manager/database/print"],spy.Commands);
    }
}
