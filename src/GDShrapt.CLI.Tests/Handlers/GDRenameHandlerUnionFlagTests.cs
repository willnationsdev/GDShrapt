using GDShrapt.CLI.Core;
using GDShrapt.Semantics;

namespace GDShrapt.CLI.Tests;

/// <summary>
/// Guards the union/shared-reference promotion flag plumbing on the base rename handler. The handler
/// property must round-trip to <see cref="GDRenameService.PromoteFullyCoveredUnionReferences"/> so the
/// CLI <c>--no-union-strict</c> option and the LSP/Plugin can control the behavior.
/// </summary>
[TestClass]
public class GDRenameHandlerUnionFlagTests
{
    private string? _tempProjectPath;

    [TestCleanup]
    public void Cleanup()
    {
        if (_tempProjectPath != null)
            TestProjectHelper.DeleteTempProject(_tempProjectPath);
    }

    [TestMethod]
    public void PromoteFullyCoveredUnionReferences_DefaultsTrue_AndIsSettable()
    {
        _tempProjectPath = TestProjectHelper.CreateTempProject(
            ("a.gd", "class_name A\nextends Node\n\nfunc go() -> void:\n\tpass\n"));

        using var project = GDShrapt.Semantics.GDProjectLoader.LoadProject(_tempProjectPath);
        var handler = new GDRenameHandler(project);

        handler.PromoteFullyCoveredUnionReferences.Should().BeTrue("the restored behavior is on by default");

        handler.PromoteFullyCoveredUnionReferences = false;
        handler.PromoteFullyCoveredUnionReferences.Should().BeFalse("the flag must round-trip to the service");

        handler.PromoteFullyCoveredUnionReferences = true;
        handler.PromoteFullyCoveredUnionReferences.Should().BeTrue();
    }
}
