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
}
