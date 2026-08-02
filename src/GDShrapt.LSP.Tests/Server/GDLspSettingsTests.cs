using System.Text.Json;
using GDShrapt.LSP;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;

namespace GDShrapt.LSP.Tests;

[TestClass]
public class GDLspSettingsTests
{
    [TestMethod]
    public void FromInitializationOptions_Null_ReturnsDefaults()
    {
        var s = GDLspSettings.FromInitializationOptions(null);

        s.SyntaxDebounceMs.Should().Be(300);
        s.SemanticDebounceMs.Should().Be(800);
        s.OperationTimeoutMs.Should().Be(0);
    }

    [TestMethod]
    public void FromInitializationOptions_ParsesProvidedValues()
    {
        using var doc = JsonDocument.Parse("{\"syntaxDebounceMs\":100,\"semanticDebounceMs\":500,\"operationTimeoutMs\":2000}");
        var s = GDLspSettings.FromInitializationOptions(doc.RootElement);

        s.SyntaxDebounceMs.Should().Be(100);
        s.SemanticDebounceMs.Should().Be(500);
        s.OperationTimeoutMs.Should().Be(2000);
    }

    [TestMethod]
    public void FromInitializationOptions_NegativeOrMissing_KeepsDefaults()
    {
        using var doc = JsonDocument.Parse("{\"syntaxDebounceMs\":-5}");
        var s = GDLspSettings.FromInitializationOptions(doc.RootElement);

        s.SyntaxDebounceMs.Should().Be(300, "negative values are ignored");
        s.SemanticDebounceMs.Should().Be(800, "missing values fall back to the default");
    }

    [TestMethod]
    public void FromInitializationOptions_AcceptsStringNumbers()
    {
        using var doc = JsonDocument.Parse("{\"operationTimeoutMs\":\"1500\"}");
        var s = GDLspSettings.FromInitializationOptions(doc.RootElement);

        s.OperationTimeoutMs.Should().Be(1500);
    }

    [TestMethod]
    public void FromInitializationOptions_NonObject_ReturnsDefaults()
    {
        using var doc = JsonDocument.Parse("\"not-an-object\"");
        var s = GDLspSettings.FromInitializationOptions(doc.RootElement);

        s.SyntaxDebounceMs.Should().Be(300);
    }
}
