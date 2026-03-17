using FluentAssertions;
using McOperator.Webhooks;
using Xunit;

namespace McOperator.Tests;

/// <summary>
/// Tests for the TryParseMemory and IsValidK8sQuantity helpers in the validation webhook.
/// </summary>
public class MemoryParsingTests
{
    [Theory]
    [InlineData("512m", true, 512)]
    [InlineData("512M", true, 512)]
    [InlineData("1G", true, 1024)]
    [InlineData("1g", true, 1024)]
    [InlineData("2G", true, 2048)]
    [InlineData("4096M", true, 4096)]
    public void TryParseMemory_ValidValues_ParsesCorrectly(string value, bool expectedResult, long expectedMb)
    {
        var result = MinecraftServerValidationWebhook.TryParseMemory(value, out var megabytes);

        result.Should().Be(expectedResult);
        megabytes.Should().Be(expectedMb);
    }

    [Theory]
    [InlineData("not-memory")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-1G")]
    [InlineData("0G")]
    [InlineData("abc")]
    public void TryParseMemory_InvalidValues_ReturnsFalse(string value)
    {
        var result = MinecraftServerValidationWebhook.TryParseMemory(value, out _);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("500m")]
    [InlineData("1")]
    [InlineData("2.5")]
    [InlineData("10Gi")]
    [InlineData("1Ki")]
    [InlineData("2Mi")]
    [InlineData("1G")]
    [InlineData("1M")]
    [InlineData("1k")]
    public void IsValidK8sQuantity_ValidValues_ReturnsTrue(string value)
    {
        var result = MinecraftServerValidationWebhook.IsValidK8sQuantity(value);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("not-a-quantity")]
    [InlineData(null)]
    public void IsValidK8sQuantity_InvalidValues_ReturnsFalse(string? value)
    {
        var result = MinecraftServerValidationWebhook.IsValidK8sQuantity(value);

        result.Should().BeFalse();
    }
}
