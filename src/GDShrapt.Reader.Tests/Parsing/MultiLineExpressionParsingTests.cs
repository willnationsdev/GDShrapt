using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace GDShrapt.Reader.Tests
{
    [TestClass]
    public class MultiLineExpressionParsingTests
    {
        private GDScriptReader _reader;

        [TestInitialize]
        public void Setup()
        {
            _reader = new GDScriptReader();
        }

        #region Multi-line assert argument counting

        [TestMethod]
        public void Assert_MultiLineStringFormatOperator_TwoArguments()
        {
            // % operator on continuation line should be part of the 2nd argument, not a new argument
            var code = "func test():\n\tassert(cond, \"msg %s\"\n\t\t% [a, b])\n";
            var tree = _reader.ParseFileContent(code);

            var method = tree.Methods.First();
            var stmt = method.Statements.First() as GDExpressionStatement;
            stmt.Should().NotBeNull();

            var call = stmt!.Expression as GDCallExpression;
            call.Should().NotBeNull("assert should be parsed as a call expression");

            var argCount = call!.Parameters?.Count ?? 0;
            argCount.Should().Be(2, "assert(cond, \"msg\" % [a, b]) should have exactly 2 arguments");
        }

        [TestMethod]
        public void Assert_MultiLineStringConcatenation_TwoArguments()
        {
            // + operator at end of line, string on next line
            var code = "func test():\n\tassert(cond, \"Current\" +\n\t\t\"more text\")\n";
            var tree = _reader.ParseFileContent(code);

            var method = tree.Methods.First();
            var stmt = method.Statements.First() as GDExpressionStatement;
            stmt.Should().NotBeNull();

            var call = stmt!.Expression as GDCallExpression;
            call.Should().NotBeNull("assert should be parsed as a call expression");

            var argCount = call!.Parameters?.Count ?? 0;
            argCount.Should().Be(2, "assert(cond, \"Current\" + \"more text\") should have exactly 2 arguments");
        }

        [TestMethod]
        public void Assert_SingleLine_TwoArguments()
        {
            // Baseline: single-line assert works correctly
            var code = "func test():\n\tassert(cond, \"msg %s\" % [a, b])\n";
            var tree = _reader.ParseFileContent(code);

            var method = tree.Methods.First();
            var stmt = method.Statements.First() as GDExpressionStatement;
            stmt.Should().NotBeNull();

            var call = stmt!.Expression as GDCallExpression;
            call.Should().NotBeNull();

            var argCount = call!.Parameters?.Count ?? 0;
            argCount.Should().Be(2, "single-line assert should have exactly 2 arguments");
        }

        #endregion

        #region Multi-line lambda patterns

        [TestMethod]
        public void Lambda_AsFilterArgument_ParsesCorrectly()
        {
            var code = @"extends Node

func _ready():
	var result = items.filter(
		func filter_fn(item):
			return item.is_active
	)
";
            var tree = _reader.ParseFileContent(code);

            var invalidTokens = tree.AllInvalidTokens.ToList();
            invalidTokens.Should().BeEmpty("lambda as filter argument should not produce invalid tokens");
        }

        [TestMethod]
        public void Lambda_InsideConnect_ParsesCorrectly()
        {
            var code = @"extends Node

func _ready():
	some_signal.connect(
		func callback():
			do_thing()
	)
";
            var tree = _reader.ParseFileContent(code);

            var invalidTokens = tree.AllInvalidTokens.ToList();
            invalidTokens.Should().BeEmpty("lambda inside connect should not produce invalid tokens");
        }

        #endregion

        #region Parenthesized lambda with .bind()

        [TestMethod]
        public void Lambda_ParenthesizedWithBind_SingleLine_ParsesCorrectly()
        {
            var code = "extends Node\n\nfunc test():\n\tsig.connect((func cb(): do_thing()).bind(x))\n";
            var tree = _reader.ParseFileContent(code);

            var invalidTokens = tree.AllInvalidTokens.ToList();
            invalidTokens.Should().BeEmpty("parenthesized lambda with .bind() should not produce invalid tokens");
        }

        [TestMethod]
        public void Lambda_ParenthesizedWithBind_MultiLineMultiStatement_ParsesCorrectly()
        {
            // Pattern from godot-open-rpg combat_turn_queue.gd
            var code = @"extends Node

func test():
	sig.connect(
		(func _on_turn_finished(actor: Node) -> void:
				actor.has_acted = true
				next_turn.call_deferred()).bind(next_actor),
			CONNECT_ONE_SHOT
	)
";
            var tree = _reader.ParseFileContent(code);

            var invalidTokens = tree.AllInvalidTokens.ToList();
            invalidTokens.Should().BeEmpty("multi-line parenthesized lambda with .bind() should not produce invalid tokens");
        }

        [TestMethod]
        public void Lambda_ParenthesizedWithBind_SingleStatement_ParsesCorrectly()
        {
            // Pattern from godot-open-rpg active_turn_queue.gd
            var code = @"extends Node

func test():
	battler.health_depleted.connect(
		(func _on_health_depleted(b: Node):
				_cached.erase(b)).bind(battler)
	)
";
            var tree = _reader.ParseFileContent(code);

            var invalidTokens = tree.AllInvalidTokens.ToList();
            invalidTokens.Should().BeEmpty("parenthesized lambda with single-statement body and .bind() should not produce invalid tokens");
        }

        [TestMethod]
        public void Lambda_ParenthesizedWithBind_UnderscorePrefixedMemberCall()
        {
            // Underscore-prefixed identifiers need special handling in GDStatementsResolver
            var code = "extends Node\n\nfunc test():\n\tsig.connect(\n\t\t(func cb(b):\n\t\t\t\t_cached.erase(b)).bind(a)\n\t)\n";
            var tree = _reader.ParseFileContent(code);

            var invalidTokens = tree.AllInvalidTokens.ToList();
            invalidTokens.Should().BeEmpty("underscore-prefixed member call in parenthesized lambda should not produce invalid tokens");
        }

        #endregion

        #region Complex lambda patterns from real projects

        [TestMethod]
        public void Lambda_InlineMapCall_ParsesCorrectly()
        {
            var code = "extends Node\n\nfunc test():\n\tvar result = items.map(func(file):return file.path_join(\"test\"))\n";
            var tree = _reader.ParseFileContent(code);

            var invalidTokens = tree.AllInvalidTokens.ToList();
            invalidTokens.Should().BeEmpty("inline lambda in .map() should not produce invalid tokens");
        }

        [TestMethod]
        public void Lambda_SortCustom_ParsesCorrectly()
        {
            var code = "extends Node\n\nfunc test():\n\titems.sort_custom(func(a,b): return a.x < b.x)\n";
            var tree = _reader.ParseFileContent(code);

            var invalidTokens = tree.AllInvalidTokens.ToList();
            invalidTokens.Should().BeEmpty("lambda in sort_custom should not produce invalid tokens");
        }

        [TestMethod]
        public void Lambda_MultiLineReduce_ParsesCorrectly()
        {
            var code = @"extends Node

func test():
	var result = list.reduce(
		func(accum, value):
			accum[value] = true
			return accum,
		{})
";
            var tree = _reader.ParseFileContent(code);

            var invalidTokens = tree.AllInvalidTokens.ToList();
            invalidTokens.Should().BeEmpty("multi-line lambda in .reduce() should not produce invalid tokens");
        }

        [TestMethod]
        public void Lambda_SignalConnectWithIfBody_ParsesCorrectly()
        {
            var code = "extends Node\n\nfunc test():\n\tvisibility_changed.connect(func(): if !visible: close())\n";
            var tree = _reader.ParseFileContent(code);

            var invalidTokens = tree.AllInvalidTokens.ToList();
            invalidTokens.Should().BeEmpty("lambda with if body in .connect() should not produce invalid tokens");
        }

        [TestMethod]
        public void Lambda_MultiLineConnectWithNestedIf_ParsesCorrectly()
        {
            var code = @"extends Node

func test():
	tween.finished.connect(func():
		if container.has_meta(""target""):
			copy_setup(container)
	)
";
            var tree = _reader.ParseFileContent(code);

            var invalidTokens = tree.AllInvalidTokens.ToList();
            invalidTokens.Should().BeEmpty("multi-line lambda with nested if should not produce invalid tokens");
        }

        [TestMethod]
        public void Lambda_MultiLineSortCustom_ParsesCorrectly()
        {
            var code = @"extends Node

func test():
	keys.sort_custom(
		func(x, y):
			return x.length() < y.length()
	)
";
            var tree = _reader.ParseFileContent(code);

            var invalidTokens = tree.AllInvalidTokens.ToList();
            invalidTokens.Should().BeEmpty("multi-line lambda in sort_custom should not produce invalid tokens");
        }

        #endregion

        #region Parenthesized lambda with empty else body

        [TestMethod]
        public void Lambda_ParenthesizedWithBind_EmptyElseBranch_ParsesCorrectly()
        {
            // Pattern from godot-open-rpg combat_ai_random.gd
            // else: has empty body, ) closes the parenthesized lambda
            var code = @"extends Node

func test():
	sig.connect(
		(func cb(source, battlers) -> void:
			if not flag:
				if source.actions.is_empty():
					return
				if not targets.is_empty():
					flag = true
				else:
					).bind(battler, battler_list)
	)
";
            var tree = _reader.ParseFileContent(code);

            var invalidTokens = tree.AllInvalidTokens.ToList();
            invalidTokens.Should().BeEmpty("empty else body in parenthesized lambda should not produce invalid tokens");
        }

        [TestMethod]
        public void Lambda_ParenthesizedWithBind_SimpleEmptyElse_ParsesCorrectly()
        {
            // Minimal reproduction: else: with ) closing lambda
            var code = "extends Node\n\nfunc test():\n\tsig.connect(\n\t\t(func cb():\n\t\t\tif cond:\n\t\t\t\tdo_thing()\n\t\t\telse:\n\t\t\t\t).bind(x)\n\t)\n";
            var tree = _reader.ParseFileContent(code);

            var invalidTokens = tree.AllInvalidTokens.ToList();
            invalidTokens.Should().BeEmpty("minimal empty else with closing paren should not produce invalid tokens");
        }

        #endregion

        #region Multi-line ternary if expressions (Issue #26)

        [TestMethod]
        public void Ternary_ParenthesizedMultiLine_PreservesFalseExpression()
        {
            var code = "var values = []\nvar x: int = (\n\tvalues.back()\n\tif values.size() > 0\n\telse 0\n)\n";
            var tree = _reader.ParseFileContent(code);

            var variable = tree.Variables.Single(x => x.Identifier.ToString() == "x");
            var bracket = variable.Initializer as GDBracketExpression;
            bracket.Should().NotBeNull("the initializer is parenthesized");

            var ternary = bracket!.InnerExpression as GDIfExpression;
            ternary.Should().NotBeNull("the parenthesized expression should contain a ternary if expression");

            ternary!.TrueExpression.Should().NotBeNull();
            ternary.TrueExpression!.ToString().Should().Be("values.back()");
            ternary.IfKeyword.Should().NotBeNull();
            ternary.Condition.Should().NotBeNull();
            ternary.Condition!.ToString().Should().Be("values.size() > 0");
            ternary.ElseKeyword.Should().NotBeNull();
            ternary.FalseExpression.Should().NotBeNull("the expression after `else` should be parsed");
            ternary.FalseExpression!.ToString().Should().Be("0");

            tree.ToString().Should().Be(code, "multi-line ternary must round-trip exactly");
            tree.AllInvalidTokens.Should().BeEmpty();
        }

        [TestMethod]
        public void Ternary_MultiLineInsideCallArguments_DoesNotThrow()
        {
            var code = "func f(c, a, b):\n\tprint(\n\t\ta\n\t\tif c\n\t\telse b\n\t)\n";

            GDClassDeclaration tree = null;
            var act = () => tree = _reader.ParseFileContent(code);

            act.Should().NotThrow("the parser must degrade to invalid tokens, never throw");

            var ternary = tree!.AllNodes.OfType<GDIfExpression>().SingleOrDefault();
            ternary.Should().NotBeNull();
            ternary!.TrueExpression!.ToString().Should().Be("a");
            ternary.Condition!.ToString().Should().Be("c");
            ternary.FalseExpression!.ToString().Should().Be("b");

            tree.ToString().Should().Be(code);
            tree.AllInvalidTokens.Should().BeEmpty();
        }

        [TestMethod]
        public void Ternary_MultiLineInsideArrayInitializer_DoesNotThrow()
        {
            var code = "func f(c, x, y):\n\tvar a = [\n\t\tx\n\t\tif c\n\t\telse y\n\t]\n";

            GDClassDeclaration tree = null;
            var act = () => tree = _reader.ParseFileContent(code);

            act.Should().NotThrow();

            var ternary = tree!.AllNodes.OfType<GDIfExpression>().SingleOrDefault();
            ternary.Should().NotBeNull();
            ternary!.FalseExpression!.ToString().Should().Be("y");

            tree.ToString().Should().Be(code);
            tree.AllInvalidTokens.Should().BeEmpty();
        }

        [TestMethod]
        public void Ternary_MultiLineWithCommentBeforeIf_PreservesComment()
        {
            var code = "var c = true\nvar x = (\n\t1  # why\n\tif c\n\telse 2\n)\n";
            var tree = _reader.ParseFileContent(code);

            var ternary = tree.AllNodes.OfType<GDIfExpression>().SingleOrDefault();
            ternary.Should().NotBeNull();
            ternary!.FalseExpression!.ToString().Should().Be("2");

            tree.ToString().Should().Be(code, "the comment before `if` must survive the unwrap");
            tree.AllInvalidTokens.Should().BeEmpty();
        }

        [TestMethod]
        public void Ternary_MultiLineWithBackslashContinuation_PreservesFalseExpression()
        {
            var code = "var c = true\nvar x = 1 \\\n\tif c \\\n\telse 2\n";
            var tree = _reader.ParseFileContent(code);

            var ternary = tree.AllNodes.OfType<GDIfExpression>().SingleOrDefault();
            ternary.Should().NotBeNull();
            ternary!.TrueExpression!.ToString().Should().Be("1");
            ternary.FalseExpression!.ToString().Should().Be("2");

            tree.ToString().Should().Be(code);
            tree.AllInvalidTokens.Should().BeEmpty();
        }

        [TestMethod]
        public void Ternary_MultiLineNested_ParsesBothLevels()
        {
            var code = "var c = true\nvar d = false\nvar x = (\n\t1\n\tif c\n\telse (\n\t\t2\n\t\tif d\n\t\telse 3\n\t)\n)\n";
            var tree = _reader.ParseFileContent(code);

            tree.AllNodes.OfType<GDIfExpression>().Count().Should().Be(2);

            tree.ToString().Should().Be(code);
            tree.AllInvalidTokens.Should().BeEmpty();
        }

        [TestMethod]
        public void Ternary_MultiLineFalseBranchContinues_ParsesFullExpression()
        {
            var code = "var c = true\nvar x = (\n\t1\n\tif c\n\telse 2\n\t\t+ 3\n)\n";
            var tree = _reader.ParseFileContent(code);

            var ternary = tree.AllNodes.OfType<GDIfExpression>().SingleOrDefault();
            ternary.Should().NotBeNull();
            ternary!.FalseExpression.Should().BeOfType<GDDualOperatorExpression>();

            tree.ToString().Should().Be(code);
            tree.AllInvalidTokens.Should().BeEmpty();
        }

        [TestMethod]
        public void MultiLine_InOperatorOnContinuationLine_NotInterceptedByIfProbe()
        {
            var code = "var y = []\nvar r = (\n\t1\n\tin y\n)\n";
            var tree = _reader.ParseFileContent(code);

            tree.AllNodes.OfType<GDIfExpression>().Should().BeEmpty("`in` must not be parsed as a ternary `if`");

            tree.ToString().Should().Be(code);
        }

        [TestMethod]
        public void MultiLine_IsOperatorOnContinuationLine_NotInterceptedByIfProbe()
        {
            var code = "var y = null\nvar r = (\n\ty\n\tis Node\n)\n";
            var tree = _reader.ParseFileContent(code);

            tree.AllNodes.OfType<GDIfExpression>().Should().BeEmpty("`is` must not be parsed as a ternary `if`");

            tree.ToString().Should().Be(code);
        }

        [TestMethod]
        public void Ternary_ParenthesizedMultiLine_CarriageReturnLineEndings()
        {
            var code = "var values = []\r\nvar x: int = (\r\n\tvalues.back()\r\n\tif values.size() > 0\r\n\telse 0\r\n)\r\n";
            var tree = _reader.ParseFileContent(code);

            var ternary = tree.AllNodes.OfType<GDIfExpression>().SingleOrDefault();
            ternary.Should().NotBeNull();
            ternary!.FalseExpression!.ToString().Should().Be("0");

            tree.ToOriginalString().Should().Be(code);
            tree.AllInvalidTokens.Should().BeEmpty();
        }

        #endregion

        #region String format % on continuation line (Issue #17)

        [TestMethod]
        public void StringFormat_PercentOnContinuationLine_NoException()
        {
            var code = "class_name T\nextends RefCounted\n\nfunc foo() -> void:\n\tprint(\"hello %s %s\"\n\t\t% [\"world\", \"!\"])\n";
            var tree = _reader.ParseFileContent(code);

            var invalidTokens = tree.AllInvalidTokens.ToList();
            invalidTokens.Should().BeEmpty("string format % on continuation line should not produce invalid tokens or exceptions");

            var method = tree.Methods.First();
            method.Should().NotBeNull();

            var stmt = method.Statements.First() as GDExpressionStatement;
            stmt.Should().NotBeNull();

            var call = stmt!.Expression as GDCallExpression;
            call.Should().NotBeNull("print should be parsed as a call expression");
            call!.Parameters.Count.Should().Be(1, "print(\"hello %s %s\" % [...]) should have exactly 1 argument");
        }

        [TestMethod]
        public void StringFormat_PercentOnContinuationLine_IssueRepro_NoException()
        {
            // Issue #17 verbatim: no space after the comma inside the array
            var code = "class_name T\nextends RefCounted\n\nfunc foo() -> void:\n\tprint(\"hello %s %s\"\n\t\t% [\"world\",\"!\"])\n";

            GDClassDeclaration tree = null;
            var act = () => tree = _reader.ParseFileContent(code);

            act.Should().NotThrow("the parser must never throw out of ParseFileContent");

            tree!.AllInvalidTokens.Should().BeEmpty();
            tree.ToString().Should().Be(code);

            var call = (tree.Methods.First().Statements.First() as GDExpressionStatement)!.Expression as GDCallExpression;
            call.Should().NotBeNull();
            call!.Parameters.Count.Should().Be(1);
        }

        #endregion
    }
}
