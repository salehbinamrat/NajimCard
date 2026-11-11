using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class VoucherGeneratorTests
{
    [Fact]
    public void GeneratesUniqueNumbersAndKeepsPasswordRelation()
    {
        var options = new VoucherGenerationOptions(350, "NC-", 6, VoucherGenerator.Digits, true, "default");
        var result = VoucherGenerator.Generate(options);
        Assert.Equal(350, result.Count);
        Assert.Equal(350, result.Select(x => x.Username).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(result, x => { Assert.StartsWith("NC-", x.Username); Assert.Equal(x.Username, x.Password); });
    }

    [Fact]
    public void AvoidsReservedRouterNumbers()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NC-0000", "NC-9999" };
        var cards = VoucherGenerator.Generate(new(60, "NC-", 4, VoucherGenerator.Digits, false, "medium"), existing);
        Assert.All(cards, c => Assert.DoesNotContain(c.Username, existing));
    }

    [Theory]
    [InlineData(0, 6, "12345678")]
    [InlineData(10, 2, "12345678")]
    [InlineData(50001, 6, "12345678")]
    [InlineData(30, 5, "00000")]
    public void InvalidParametersFailWithoutProducingAnyCards(int count, int length, string charset)
    {
        Assert.ThrowsAny<ArgumentException>(() => VoucherGenerator.Generate(new(count, "NC-", length, charset, false, "default")));
    }

    [Fact]
    public void ExhaustedSpaceFailsBeforeGeneration()
    {
        Assert.Throws<ArgumentException>(() => VoucherGenerator.Generate(new(100, "NC-", 4, "01", false, "default")));
    }

    [Fact]
    public void MixedCaseUsernameAlphabet_CannotOverestimateUniqueRouterNames()
    {
        // SQLite voucher names use COLLATE NOCASE: a/A are the same token.
        // The old calculation incorrectly claimed 3^4 distinct numbers.
        Assert.ThrowsAny<ArgumentException>(()=>VoucherGenerator.Generate(
            new(17,"NC-",4,"aA1",true,"default")));
        Assert.ThrowsAny<ArgumentException>(()=>VoucherGenerator.Generate(
            new(2,"NC-",4,"aA",true,"default")));
    }

    [Fact]
    public void GeneratedMixedCaseAlphabet_IsUniqueUnderSqliteCaseInsensitivity()
    {
        var vouchers=VoucherGenerator.Generate(
            new(40,"NC-",4,"aA01",true,"default"));
        Assert.Equal(40,vouchers.Count);
        Assert.Equal(vouchers.Count,vouchers.Select(x=>x.Username)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(vouchers,x=>Assert.Equal(x.Username,x.Password));
    }

    [Fact]
    public void ReservedNames_ExhaustingActualAlphabet_AreRejectedBeforeGeneration()
    {
        // Only 16 distinct 4-character credentials exist in {0,a}.
        // Uppercase A in the router's historical users is equivalent to a.
        var occupied=Enumerable.Range(0,15)
            .Select(i=>"NC-"+Convert.ToString(i,2).PadLeft(4,'0')
                .Replace('1',i%2==0?'A':'a'))
            .ToHashSet(StringComparer.Ordinal);
        Assert.ThrowsAny<ArgumentException>(()=>VoucherGenerator.Generate(
            new(2,"NC-",4,"aA0",false,"default"),occupied));
    }

    [Fact]
    public void DifferentRouterPrefixes_DoNotConsumeTheCurrentNumberSpace()
    {
        var unrelated=Enumerable.Range(0,20)
            .Select(i=>"OLD-"+i.ToString("D4"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cards=VoucherGenerator.Generate(
            new(10,"NC-",4,"aA01",false,"default",
                PasswordLength:1,PasswordCharacters:"aA"),unrelated);
        Assert.Equal(10,cards.Count);
        Assert.Equal(10,cards.Select(x=>x.Username)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(cards,x=>Assert.Contains(x.Password,new[]{"a","A"}));
    }


    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Username_Only_Mode_Never_Generates_A_Hidden_Matching_Password(bool samePassword)
    {
        // Hadramout code_only always returns password="", independent of the
        // otherwise selected matching/separate-password sub-option.
        var options=new VoucherGenerationOptions(
            8,"",8,VoucherGenerator.Digits,samePassword,"gold",
            PasswordLength:0,PasswordCharacters:"INVALID-CHARS!",
            UsernameOnly:true);
        var sample=VoucherGenerator.PreviewSample(options);
        Assert.NotEmpty(sample.Username);
        Assert.Equal("",sample.Password);
        var cards=VoucherGenerator.Generate(options);
        Assert.Equal(8,cards.Count);
        Assert.All(cards,card=>
        {
            Assert.NotEmpty(card.Username);
            Assert.Equal("",card.Password);
        });
    }

    [Fact]
    public void All_Three_Credential_Modes_Are_Independent()
    {
        VoucherGenerationOptions opts=new(5,"",8,VoucherGenerator.Digits,true,"gold");
        var usernameOnly=VoucherGenerator.Generate(opts with{UsernameOnly=true});
        var samePassword=VoucherGenerator.Generate(opts with{UsernameOnly=false});
        var separatePassword=VoucherGenerator.Generate(opts with
        {
            UsernameOnly=false,PasswordEqualsUsername=false,
            PasswordLength=6,PasswordCharacters="abcdef"
        });
        Assert.All(usernameOnly,card=>Assert.Empty(card.Password));
        Assert.All(samePassword,card=>Assert.Equal(card.Username,card.Password));
        Assert.All(separatePassword,card=>
        {
            Assert.Equal(6,card.Password.Length);
            Assert.NotEqual(card.Username,card.Password);
        });
    }

}
