using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// Regression tests at the real RouterGateway -> RouterOS command contract.
/// They do not simulate success by returning dummy JS/frontend responses.
/// </summary>
public sealed class UserManagerV6IssuingTests
{
    private static Voucher Card(string name = "001239") =>
        new(name, "pAsS123", "gold", "batch-a", DateTimeOffset.UtcNow);

    private sealed class RouterSpy : IRouterCommandClient, IRouterReceiptClient, IRouterQueryClient
    {
        public readonly List<(string Command, Dictionary<string, string> Args)> Calls = [];
        public string? AddReturnId { get; set; } = "*1F";
        public string? TrapCommand { get; set; }
        public string? DisconnectCommand { get; set; }
        public int TrapCount { get; set; }
        public int DisconnectCount { get; set; }
        public readonly List<(string Command,string Id)> ReadsById = [];
        public bool MissingVerifiedRecord { get; set; }
        public bool WrongVerifiedProfile { get; set; }
        public bool WrongVerifiedUsername { get; set; }
        public bool FailVerificationRead { get; set; }
        public bool DelayedProfileVisibility { get; set; }


        private Task<RouterCommandReply> Respond(string command, IReadOnlyDictionary<string,string>? attributes)
        {
            Calls.Add((command, new Dictionary<string,string>(attributes ??
                new Dictionary<string,string>())));
            if (command == DisconnectCommand && ++DisconnectCount == 1)
                throw new IOException("Connection dropped after write");
            if (command == TrapCommand && ++TrapCount == 1)
                throw new RouterOsTrapException("RouterOS rejects requested profile");

            var done = new Dictionary<string,string>();
            if (command == "/tool/user-manager/user/add" && AddReturnId is not null)
                done["ret"] = AddReturnId;
            return Task.FromResult(new RouterCommandReply([], done));
        }
        public async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command, IReadOnlyDictionary<string,string>? attributes = null,
            CancellationToken cancellationToken = default) =>
            (await Respond(command, attributes)).Records;

        public Task<RouterCommandReply> CommandWithReceiptAsync(
            string command, IReadOnlyDictionary<string,string>? attributes = null,
            CancellationToken cancellationToken = default) => Respond(command, attributes);

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
            string command,string id,IReadOnlyDictionary<string,string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            ReadsById.Add((command,id));
            if(FailVerificationRead)throw new IOException("Router readback failed");
            if(MissingVerifiedRecord)
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
            var profile=WrongVerifiedProfile || (DelayedProfileVisibility && ReadsById.Count==1)
                ? "other" : "gold";
            IReadOnlyList<IReadOnlyDictionary<string,string>> result=
            [
                new Dictionary<string,string>
                {
                    [".id"]=id,
                    ["username"]=WrongVerifiedUsername?"unexpected":"001239",
                    ["actual-profile"]=profile,
                    ["customer"]="admin"
                }
            ];
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task Issue_Uses_V6_User_Add_Returned_Id_Then_Activate_Profile_With_Id_Not_Name()
    {
        var spy = new RouterSpy();
        var result = await new RouterGateway(spy).CreateAndActivateUserManagerV6Async(Card(), "admin");
        Assert.True(result.Success);
        Assert.False(result.UncertainOutcome);
        Assert.Equal("*1F", result.UserId);
        Assert.Equal(["/tool/user-manager/user/add",
            "/tool/user-manager/user/create-and-activate-profile"], spy.Calls.Select(x=>x.Command));
        var add = spy.Calls[0].Args;
        Assert.Equal("001239", add["username"]);
        Assert.Equal("pAsS123", add["password"]);
        Assert.Equal("admin", add["customer"]);
        Assert.False(add.ContainsKey("name"));
        var activate = spy.Calls[1].Args;
        Assert.Equal("*1F", activate["numbers"]);
        Assert.Equal("gold", activate["profile"]);
        Assert.Equal("admin", activate["customer"]);
        Assert.Single(spy.ReadsById);
        Assert.Equal("/tool/user-manager/user/print",spy.ReadsById[0].Command);
        Assert.Equal("*1F",spy.ReadsById[0].Id);
    }

    [Fact]
    public async Task Acknowledged_Profile_Without_Live_Assignment_Must_Not_Be_Printed()
    {
        var spy=new RouterSpy{WrongVerifiedProfile=true};
        var result=await new RouterGateway(spy).CreateAndActivateUserManagerV6Async(Card(),"admin");
        Assert.False(result.Success);
        Assert.True(result.UncertainOutcome);
        Assert.Equal(2,spy.Calls.Count); // no second write or destructive rollback
        Assert.Equal(3,spy.ReadsById.Count);
    }

    [Fact]
    public async Task Missing_Router_User_After_Acknowledgement_Is_Uncertain()
    {
        var spy=new RouterSpy{MissingVerifiedRecord=true};
        var result=await new RouterGateway(spy).CreateAndActivateUserManagerV6Async(Card(),"admin");
        Assert.False(result.Success);
        Assert.True(result.UncertainOutcome);
        Assert.Equal(2,spy.Calls.Count);
    }

    [Fact]
    public async Task Wrong_Router_Username_Must_Not_Mark_Voucher_As_Sent()
    {
        var spy=new RouterSpy{WrongVerifiedUsername=true};
        var result=await new RouterGateway(spy).CreateAndActivateUserManagerV6Async(Card(),"admin");
        Assert.False(result.Success);
        Assert.True(result.UncertainOutcome);
        Assert.Equal(2,spy.Calls.Count);
    }

    [Fact]
    public async Task Profile_Visible_On_Second_Read_Is_Confirmed_Without_Rewriting()
    {
        var spy=new RouterSpy{DelayedProfileVisibility=true};
        var result=await new RouterGateway(spy).CreateAndActivateUserManagerV6Async(Card(),"admin");
        Assert.True(result.Success);
        Assert.Equal(2,spy.Calls.Count);
        Assert.Equal(2,spy.ReadsById.Count);
    }

    [Fact]
    public async Task Lost_Readback_Leaves_Voucher_Uncertain_Without_Replaying()
    {
        var spy=new RouterSpy{FailVerificationRead=true};
        var result=await new RouterGateway(spy).CreateAndActivateUserManagerV6Async(Card(),"admin");
        Assert.False(result.Success);
        Assert.True(result.UncertainOutcome);
        Assert.Equal(2,spy.Calls.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("001239")]
    [InlineData("*A, *B")]
    [InlineData("*INVALID")]
    public async Task Missing_Or_Invalid_Add_Receipt_Is_Uncertain_And_Never_Attaches_Package(string? receipt)
    {
        var spy = new RouterSpy { AddReturnId = receipt };
        var result = await new RouterGateway(spy).CreateAndActivateUserManagerV6Async(Card(), "admin");
        Assert.False(result.Success);
        Assert.True(result.UncertainOutcome);
        Assert.Single(spy.Calls);
        Assert.Equal("/tool/user-manager/user/add", spy.Calls[0].Command);
    }

    [Fact]
    public async Task Explicit_Profile_Rejection_Removes_Only_Just_Created_User()
    {
        var spy = new RouterSpy { TrapCommand = "/tool/user-manager/user/create-and-activate-profile" };
        var result = await new RouterGateway(spy).CreateAndActivateUserManagerV6Async(Card(), "admin");
        Assert.False(result.Success);
        Assert.False(result.UncertainOutcome);
        Assert.Equal(3, spy.Calls.Count);
        Assert.Equal("/tool/user-manager/user/remove", spy.Calls[2].Command);
        Assert.Equal("*1F", spy.Calls[2].Args[".id"]);
    }

    [Fact]
    public async Task Profile_Write_Connection_Drop_Is_Uncertain_And_Never_Retries_Or_Removes()
    {
        var spy = new RouterSpy { DisconnectCommand = "/tool/user-manager/user/create-and-activate-profile" };
        var result = await new RouterGateway(spy).CreateAndActivateUserManagerV6Async(Card(), "admin");
        Assert.False(result.Success);
        Assert.True(result.UncertainOutcome);
        Assert.Equal(2, spy.Calls.Count);
    }

    [Fact]
    public async Task Batch_Stops_After_Uncertain_Outcome_Instead_Of_Replaying_Later_Cards()
    {
        var spy = new RouterSpy { DisconnectCommand = "/tool/user-manager/user/create-and-activate-profile" };
        var batch = new[] { Card("1001"), Card("1002"), Card("1003") };
        var result = await new RouterGateway(spy).UploadUserManagerV6BatchAsync(batch, "admin");
        Assert.True(result.UncertainOutcome);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(2, spy.Calls.Count); // add + ambiguous profile command
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Missing_Customer_Rejects_Without_Router_Write(string customer)
    {
        var spy = new RouterSpy();
        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await new RouterGateway(spy).CreateAndActivateUserManagerV6Async(Card(), customer));
        Assert.Empty(spy.Calls);
    }

    [Fact]
    public async Task Duplicates_In_Batch_Reject_Before_Any_Write()
    {
        var spy = new RouterSpy();
        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await new RouterGateway(spy).UploadUserManagerV6BatchAsync(
                [Card("a"), Card("A")], "admin"));
        Assert.Empty(spy.Calls);
    }

    [Fact]
    public void Parser_Extracts_Return_Id_From_A_Real_RouterOs_Done_Sentence()
    {
        var reply = RouterOsReplyDecoder.Decode(
            [
                ["!re", "=.id=*1", "=username=client"],
                ["!done", "=ret=*1F"]
            ]);
        Assert.Equal("*1F", reply.Done["ret"]);
        Assert.Equal("client", Assert.Single(reply.Records)["username"]);
    }
}
