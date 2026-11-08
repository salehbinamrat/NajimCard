using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class VoucherGenerationParityTests
{
    [Fact]
    public void Different_Username_And_Password_Alphabet_And_Length_Are_Respected()
    {
        var cards=VoucherGenerator.Generate(new VoucherGenerationOptions(
            180,"",10,VoucherGenerator.Digits,false,"1000RY",
            PasswordLength:6,PasswordCharacters:"abcDEF"));
        Assert.Equal(180,cards.Count);
        Assert.All(cards,c=>
        {
            Assert.Equal(10,c.Username.Length);
            Assert.All(c.Username, ch=>Assert.Contains(ch,VoucherGenerator.Digits));
            Assert.Equal(6,c.Password.Length);
            Assert.All(c.Password,ch=>Assert.Contains(ch,"abcDEF"));
            Assert.NotEqual(c.Username,c.Password);
        });
    }

    [Fact]
    public void Declared_Total_Length_Accounts_For_Prefix_When_Requested()
    {
        var cards=VoucherGenerator.Generate(new VoucherGenerationOptions(
            25,"NC",8,VoucherGenerator.Digits,true,"p",LengthIncludesPrefix:true));
        Assert.All(cards,c=>
        {
            Assert.StartsWith("NC",c.Username);
            Assert.Equal(8,c.Username.Length);
            Assert.Equal(c.Username,c.Password);
        });
    }

    [Fact]
    public void Exhausted_Prefix_Suffix_Space_Fails_Before_Loop()
    {
        Assert.ThrowsAny<ArgumentException>(()=>VoucherGenerator.Generate(new VoucherGenerationOptions(
            17,"AB",6,"01",true,"p",LengthIncludesPrefix:true)));
    }

    [Theory]
    [InlineData(0,"abc")]
    [InlineData(65,"abc")]
    [InlineData(6,"a b")]
    public void Invalid_Password_Length_Or_Characters_Are_Rejected(int length,string alphabet)
    {
        Assert.ThrowsAny<ArgumentException>(()=>VoucherGenerator.Generate(
            new VoucherGenerationOptions(2,"",8,VoucherGenerator.Digits,false,"p",
                PasswordLength:length,PasswordCharacters:alphabet)));
    }
}
