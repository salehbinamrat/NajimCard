using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class VoucherLivePreviewTests
{
    [Theory]
    [InlineData("0123456789", "", 8, true)]
    [InlineData("ABCDEFGHJKLMNPQRSTUVWXYZ", "AB", 10, true)]
    [InlineData("0123456789ABCDEFGHJKLMNPQRSTUVWXYZ", "N-", 12, false)]
    public void Preview_UsesExactGeneratorAlphabetLengthAndPrefix(
        string characters, string prefix, int length, bool includesPrefix)
    {
        var options = new VoucherGenerationOptions(
            20, prefix, length, characters, true, "معاينة",
            LengthIncludesPrefix: includesPrefix);
        var sample = VoucherGenerator.PreviewSample(options);
        Assert.StartsWith(prefix, sample.Username);
        Assert.Equal(length + (includesPrefix ? 0 : prefix.Length), sample.Username.Length);
        Assert.All(sample.Username[prefix.Length..], c => Assert.Contains(c, characters));
        Assert.Equal(sample.Username, sample.Password);
    }

    [Fact]
    public void Preview_DifferentPasswordMatchesConfiguredLengthAndAlphabet()
    {
        var options = new VoucherGenerationOptions(
            4, "X", 8, VoucherGenerator.Digits, false, "معاينة",
            PasswordLength: 5, PasswordCharacters: "abcd",
            LengthIncludesPrefix: true);
        var sample = VoucherGenerator.PreviewSample(options);
        Assert.Equal(8, sample.Username.Length);
        Assert.StartsWith("X", sample.Username);
        Assert.Equal(5, sample.Password.Length);
        Assert.All(sample.Password, c => Assert.Contains(c, "abcd"));
        Assert.NotEqual(sample.Username, sample.Password);
    }

    [Fact]
    public void Preview_RejectsSameInvalidBatchConfigurationAsGenerator()
    {
        // One possible name cannot fulfil a request for multiple unique names.
        var impossible = new VoucherGenerationOptions(
            2, "ABCD", 4, "0123456789", true, "معاينة",
            LengthIncludesPrefix: true);
        Assert.Throws<ArgumentException>(() => VoucherGenerator.PreviewSample(impossible));
        Assert.Throws<ArgumentException>(() => VoucherGenerator.Generate(impossible));
    }

    [Fact]
    public void Preview_DoesNotReturnAnIssuableVoucherOrPersistedBatch()
    {
        var options = new VoucherGenerationOptions(
            2, "", 8, VoucherGenerator.Digits, false, "معاينة",
            PasswordLength: 6, PasswordCharacters: "abcd");
        var sample = VoucherGenerator.PreviewSample(options);
        Assert.IsType<(string Username, string Password)>(sample);
        Assert.NotEmpty(sample.Username);
        Assert.NotEmpty(sample.Password);
        // This API has no VoucherStore/RouterGateway dependency or dispatch path.
    }
}
