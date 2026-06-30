namespace MemorySmith.Agent.Tests;

using global::Agent.Core;
using global::Agent.Planning;
using global::Agent.Planning.Llm;

/// <summary>
/// Sprint 56 (TSK-0278): Tests for ParseEvaluationResult and ExtractJson.
/// These are critical parsing utilities used by the replanning path that
/// previously had zero test coverage and silently swallowed parse failures.
/// </summary>

[TestFixture]
[Description("Sprint 56: LlmEvaluatorImpl.ParseEvaluationResult and ExtractJson tests.")]
public sealed class LlmEvaluatorImplParseTests
{
    // ── ParseEvaluationResult — valid JSON ──────────────────────────────────

    [Test]
    public void ParseEvaluationResult_ValidReplanTrue_ReturnsSuccess()
    {
        var result = LlmEvaluatorImpl.ParseEvaluationResult("{\"replan\": true, \"reason\": \"stuck\"}");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ShouldReplan, Is.True);
            Assert.That(result.Reason, Is.EqualTo("stuck"));
            Assert.That(result.FailureReason, Is.Null);
        });
    }

    [Test]
    public void ParseEvaluationResult_ValidReplanFalse_ReturnsSuccess()
    {
        var result = LlmEvaluatorImpl.ParseEvaluationResult("{\"replan\": false}");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ShouldReplan, Is.False);
        });
    }

    [Test]
    public void ParseEvaluationResult_WithSuggestion_ReturnsSuggestion()
    {
        var result = LlmEvaluatorImpl.ParseEvaluationResult(
            "{\"replan\": true, \"suggestion\": \"skip block #9\"}");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ShouldReplan, Is.True);
            Assert.That(result.Suggestion, Is.EqualTo("skip block #9"));
        });
    }

    [Test]
    public void ParseEvaluationResult_ExtraProperties_ReturnsSuccess()
    {
        var result = LlmEvaluatorImpl.ParseEvaluationResult(
            "{\"replan\": true, \"extra\": \"data\", \"nested\": {\"a\": 1}}");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ShouldReplan, Is.True);
        });
    }

    // ── ParseEvaluationResult — failure modes ───────────────────────────────

    [Test]
    public void ParseEvaluationResult_EmptyString_ReturnsParseFailure()
    {
        var result = LlmEvaluatorImpl.ParseEvaluationResult("");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo("ParseFailure"));
            Assert.That(result.ShouldReplan, Is.False);
        });
    }

    [Test]
    public void ParseEvaluationResult_WhitespaceOnly_ReturnsParseFailure()
    {
        var result = LlmEvaluatorImpl.ParseEvaluationResult("   ");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo("ParseFailure"));
        });
    }

    [Test]
    public void ParseEvaluationResult_ProseOnly_ReturnsParseFailure()
    {
        var result = LlmEvaluatorImpl.ParseEvaluationResult("sure, sounds good");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo("ParseFailure"));
            Assert.That(result.ShouldReplan, Is.False);
        });
    }

    [Test]
    public void ParseEvaluationResult_TruncatedJson_ReturnsParseFailure()
    {
        // Truncated JSON — no closing brace — cannot be parsed.
        var result = LlmEvaluatorImpl.ParseEvaluationResult("{ \"replan\": true");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo("ParseFailure"));
        });
    }

    [Test]
    public void ParseEvaluationResult_MalformedBraces_ReturnsParseFailure()
    {
        var result = LlmEvaluatorImpl.ParseEvaluationResult("{replan: true");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo("ParseFailure"));
        });
    }

    [Test]
    public void ParseEvaluationResult_TextBeforeAndAfterJson_ReturnsSuccess()
    {
        var result = LlmEvaluatorImpl.ParseEvaluationResult(
            "Here is my response: {\"replan\": true} hope that helps");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ShouldReplan, Is.True);
        });
    }

    // ── ExtractJson ─────────────────────────────────────────────────────────

    [Test]
    public void ExtractJson_NormalJson_ReturnsJson()
    {
        var result = LlmEvaluatorImpl.ExtractJson("{\"replan\": true}");

        Assert.That(result, Is.EqualTo("{\"replan\": true}"));
    }

    [Test]
    public void ExtractJson_TextAroundJson_ExtractsJson()
    {
        var result = LlmEvaluatorImpl.ExtractJson("prefix {\"replan\": false} suffix");

        Assert.That(result, Is.EqualTo("{\"replan\": false}"));
    }

    [Test]
    public void ExtractJson_NoJson_ReturnsNull()
    {
        var result = LlmEvaluatorImpl.ExtractJson("no json here");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractJson_EmptyString_ReturnsNull()
    {
        var result = LlmEvaluatorImpl.ExtractJson("");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractJson_NestedBraces_ExtractsOutermost()
    {
        var result = LlmEvaluatorImpl.ExtractJson(
            "{\"replan\": true, \"nested\": {\"a\": 1}}");

        Assert.That(result, Is.EqualTo("{\"replan\": true, \"nested\": {\"a\": 1}}"));
    }
}
