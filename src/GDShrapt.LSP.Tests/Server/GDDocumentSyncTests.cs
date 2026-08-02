using GDShrapt.Abstractions;
using GDShrapt.Semantics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;

namespace GDShrapt.LSP.Tests;

/// <summary>
/// Incremental document sync: GDDocumentManager.ApplyChanges splices {range, text} edits into the
/// buffer. Covers LF/CRLF, multi-line, out-of-range, and full-replace cases (B3 edge cases).
/// </summary>
[TestClass]
public class GDDocumentSyncTests
{
    private static GDDocumentManager NewManager()
    {
        var baseDir = System.IO.Directory.GetCurrentDirectory();
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        var projPath = System.IO.Path.Combine(root, "testproject", "GDShrapt.TestProject");
        var project = new GDScriptProject(new GDDefaultProjectContext(projPath), new GDScriptProjectOptions());
        return new GDDocumentManager(project);
    }

    private static string Apply(string initial, params GDTextDocumentContentChangeEvent[] changes)
    {
        var dm = NewManager();
        var uri = GDDocumentManager.PathToUri("/virtual/sync_test.gd");
        dm.OpenDocument(uri, initial, 1);
        dm.ApplyChanges(uri, changes, 2);
        return dm.GetDocument(uri)!.Content;
    }

    private static GDTextDocumentContentChangeEvent Change(int sl, int sc, int el, int ec, string text)
        => new() { Range = new GDLspRange(sl, sc, el, ec), Text = text };

    [TestMethod]
    public void Incremental_ReplaceWithinLine()
        => Apply("line0\nline1\nline2", Change(1, 0, 1, 5, "XXX")).Should().Be("line0\nXXX\nline2");

    [TestMethod]
    public void Incremental_InsertAtPosition()
        => Apply("abcdef", Change(0, 3, 0, 3, "-")).Should().Be("abc-def");

    [TestMethod]
    public void Incremental_DeleteAcrossLines()
        => Apply("abcXY\nZZDEF", Change(0, 3, 1, 2, "")).Should().Be("abcDEF");

    [TestMethod]
    public void Incremental_CRLF_ReplaceSecondLine()
        => Apply("a\r\nbb\r\nc", Change(1, 0, 1, 2, "XX")).Should().Be("a\r\nXX\r\nc");

    [TestMethod]
    public void Incremental_MultipleChangesAppliedInOrder()
        => Apply("aa\nbb", Change(0, 0, 0, 2, "AAAA"), Change(1, 0, 1, 2, "BB")).Should().Be("AAAA\nBB");

    [TestMethod]
    public void Incremental_FullReplace_WhenRangeNull()
        => Apply("old", new GDTextDocumentContentChangeEvent { Range = null, Text = "new" }).Should().Be("new");

    [TestMethod]
    public void Incremental_CharacterBeyondLineLength_ClampsToLineEnd()
        => Apply("ab\ncd", Change(0, 99, 0, 99, "Z")).Should().Be("abZ\ncd");

    [TestMethod]
    public void Incremental_AppendAtEndOfDocument()
        => Apply("abc", Change(0, 3, 0, 3, "def")).Should().Be("abcdef");

    [TestMethod]
    public void Incremental_MultibyteLine_OffsetsAlignToUtf16()
        // "a😀b": the emoji is a surrogate pair (2 UTF-16 units), so 'b' is at character 3 in both
        // C# strings and LSP positions. Replacing (0,3)-(0,4) must hit exactly the 'b'.
        => Apply("a😀b\nx", Change(0, 3, 0, 4, "Z")).Should().Be("a😀Z\nx");
}
