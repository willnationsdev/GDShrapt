using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GDShrapt.Linter;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GDShrapt.Linter.Tests
{
    [TestClass]
    public class GDInvalidInputActionRuleTests
    {
        private const string FlyCode = "func _process(delta):\n\tif Input.is_action_pressed(\"fly\"):\n\t\tpass\n";
        private const string JumpCode = "func _process(delta):\n\tif Input.is_action_pressed(\"jump\"):\n\t\tpass\n";
        private const string BuiltInCode = "func _process(delta):\n\tif Input.is_action_pressed(\"ui_accept\"):\n\t\tpass\n";

        private static GDLinter LinterWith(IReadOnlyCollection<string> projectActions)
        {
            var options = new GDLinterOptions
            {
                WarnInvalidInputAction = true,
                ProjectInputActions = projectActions
            };
            return new GDLinter(options);
        }

        [TestMethod]
        public void UnknownAction_WithProjectActions_ReportsGDL246()
        {
            var result = LinterWith(new[] { "jump" }).LintCode(FlyCode);

            result.Issues.Any(i => i.RuleId == "GDL246").Should().BeTrue(
                "an action not present in project.godot should be flagged");
        }

        [TestMethod]
        public void KnownProjectAction_NoGDL246()
        {
            var result = LinterWith(new[] { "jump" }).LintCode(JumpCode);

            result.Issues.Any(i => i.RuleId == "GDL246").Should().BeFalse(
                "an action defined in project.godot should not be flagged");
        }

        [TestMethod]
        public void BuiltInAction_NoGDL246()
        {
            var result = LinterWith(new[] { "jump" }).LintCode(BuiltInCode);

            result.Issues.Any(i => i.RuleId == "GDL246").Should().BeFalse(
                "built-in actions are always valid");
        }

        [TestMethod]
        public void NoProjectContext_NoGDL246()
        {
            // ProjectInputActions is null → no project context → accept unknown names.
            var options = new GDLinterOptions { WarnInvalidInputAction = true };
            var result = new GDLinter(options).LintCode(FlyCode);

            result.Issues.Any(i => i.RuleId == "GDL246").Should().BeFalse(
                "without project context, unknown actions are accepted (no false positives)");
        }
    }
}
