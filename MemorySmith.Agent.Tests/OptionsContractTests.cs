namespace MemorySmith.Agent.Tests;

using WebUI.Blazor.Logging;
using WebUI.Blazor.Options;

[TestFixture]
public sealed class OptionsContractTests
{
    [Test]
    public void SafetyOptions_IsSealedRecord_AndSupportsWithExpression()
    {
        var options = new SafetyOptions { AllowDestructiveCommands = false };
        var updated = options with { AllowDestructiveCommands = true };

        Assert.Multiple(() =>
        {
            Assert.That(typeof(SafetyOptions).IsSealed, Is.True);
            Assert.That(typeof(SafetyOptions).GetMethod("<Clone>$"), Is.Not.Null);
            Assert.That(updated.AllowDestructiveCommands, Is.True);
        });
    }

    [Test]
    public void ChatLoggingOptions_IsSealedRecord_AndSupportsWithExpression()
    {
        var options = new ChatLoggingOptions();
        var updated = options with { Enabled = false };

        Assert.Multiple(() =>
        {
            Assert.That(typeof(ChatLoggingOptions).IsSealed, Is.True);
            Assert.That(typeof(ChatLoggingOptions).GetMethod("<Clone>$"), Is.Not.Null);
            Assert.That(updated.Enabled, Is.False);
        });
    }
}