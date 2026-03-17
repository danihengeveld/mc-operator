using McOperator.Webhooks;
using TUnit.Assertions.Extensions;

namespace McOperator.Tests;

/// <summary>
/// Tests for the TryParseMemory and IsValidK8sQuantity helpers in the validation webhook.
/// </summary>
public class MemoryParsingTests
{
    [Test]
    [Arguments("512m", true, 512L)]
    [Arguments("512M", true, 512L)]
    [Arguments("1G", true, 1024L)]
    [Arguments("1g", true, 1024L)]
    [Arguments("2G", true, 2048L)]
    [Arguments("4096M", true, 4096L)]
    public async Task TryParseMemory_ValidValues_ParsesCorrectly(string value, bool expectedResult, long expectedMb)
    {
        var result = MinecraftServerValidationWebhook.TryParseMemory(value, out var megabytes);

        await Assert.That(result).IsEqualTo(expectedResult);
        await Assert.That(megabytes).IsEqualTo(expectedMb);
    }

    [Test]
    [Arguments("not-memory")]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("-1G")]
    [Arguments("0G")]
    [Arguments("abc")]
    public async Task TryParseMemory_InvalidValues_ReturnsFalse(string value)
    {
        var result = MinecraftServerValidationWebhook.TryParseMemory(value, out _);

        await Assert.That(result).IsFalse();
    }

    [Test]
    [Arguments("500m")]
    [Arguments("1")]
    [Arguments("2.5")]
    [Arguments("10Gi")]
    [Arguments("1Ki")]
    [Arguments("2Mi")]
    [Arguments("1G")]
    [Arguments("1M")]
    [Arguments("1k")]
    public async Task IsValidK8sQuantity_ValidValues_ReturnsTrue(string value)
    {
        var result = MinecraftServerValidationWebhook.IsValidK8sQuantity(value);

        await Assert.That(result).IsTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("invalid")]
    [Arguments("not-a-quantity")]
    [Arguments(null)]
    public async Task IsValidK8sQuantity_InvalidValues_ReturnsFalse(string? value)
    {
        var result = MinecraftServerValidationWebhook.IsValidK8sQuantity(value);

        await Assert.That(result).IsFalse();
    }
}
