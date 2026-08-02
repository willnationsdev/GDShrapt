using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace GDShrapt.Reader.Tests
{
    /// <summary>
    /// Tests for parsing local (method-body) const declarations.
    /// </summary>
    [TestClass]
    public class LocalConstParsingTests
    {
        [TestMethod]
        public void ParseLocalConst_NoAnnotation()
        {
            var reader = new GDScriptReader();

            var code = "func test():\n\tconst A = 1";

            var declaration = reader.ParseFileContent(code);
            var method = declaration.Methods.First();

            Assert.AreEqual(1, method.Statements.Count);

            var statement = method.Statements[0] as GDVariableDeclarationStatement;

            Assert.IsNotNull(statement);
            Assert.IsTrue(statement.IsConstant);
            Assert.IsNotNull(statement.ConstKeyword);
            Assert.IsNull(statement.VarKeyword);
            Assert.AreEqual("A", statement.Identifier?.Sequence);
            Assert.IsNull(statement.Colon);
            Assert.IsNull(statement.Type);
            Assert.IsNotNull(statement.Assign);
            Assert.AreEqual("1", statement.Initializer?.ToString());

            AssertHelper.CompareCodeStrings(code, declaration.ToString());
            AssertHelper.NoInvalidTokens(declaration);
        }

        [TestMethod]
        public void ParseLocalConst_InferredAssign()
        {
            var reader = new GDScriptReader();

            var code = "func test():\n\tconst MAX := 10\n\treturn MAX";

            var declaration = reader.ParseFileContent(code);
            var method = declaration.Methods.First();

            Assert.AreEqual(2, method.Statements.Count);

            var statement = method.Statements[0] as GDVariableDeclarationStatement;

            Assert.IsNotNull(statement);
            Assert.IsTrue(statement.IsConstant);
            Assert.IsNotNull(statement.Colon);
            Assert.IsNull(statement.Type);
            Assert.IsNotNull(statement.Assign);
            Assert.AreEqual("MAX", statement.Identifier?.Sequence);

            AssertHelper.CompareCodeStrings(code, declaration.ToString());
            AssertHelper.NoInvalidTokens(declaration);
        }

        [TestMethod]
        public void ParseLocalConst_ExplicitType()
        {
            var reader = new GDScriptReader();

            var code = "func test():\n\tconst LABEL: String = \"hello\"";

            var declaration = reader.ParseFileContent(code);
            var method = declaration.Methods.First();

            var statement = method.Statements[0] as GDVariableDeclarationStatement;

            Assert.IsNotNull(statement);
            Assert.IsTrue(statement.IsConstant);
            Assert.IsNotNull(statement.Colon);
            Assert.AreEqual("String", statement.Type?.ToString());
            Assert.IsNotNull(statement.Assign);
            Assert.AreEqual("LABEL", statement.Identifier?.Sequence);

            AssertHelper.CompareCodeStrings(code, declaration.ToString());
            AssertHelper.NoInvalidTokens(declaration);
        }

        [TestMethod]
        public void ParseLocalConst_QualifiedType()
        {
            var reader = new GDScriptReader();

            var code = "func test():\n\tconst KIND: MyClass.MyEnum = 0";

            var declaration = reader.ParseFileContent(code);
            var statement = (GDVariableDeclarationStatement)declaration.Methods.First().Statements[0];

            Assert.IsTrue(statement.IsConstant);
            Assert.IsInstanceOfType(statement.Type, typeof(GDSubTypeNode));

            AssertHelper.CompareCodeStrings(code, declaration.ToString());
            AssertHelper.NoInvalidTokens(declaration);
        }

        [TestMethod]
        public void ParseLocalVar_IsNotConstant()
        {
            var reader = new GDScriptReader();

            var code = "func test():\n\tvar x := 10";

            var declaration = reader.ParseFileContent(code);
            var statement = declaration.Methods.First().Statements[0] as GDVariableDeclarationStatement;

            Assert.IsNotNull(statement);
            Assert.IsFalse(statement.IsConstant);
            Assert.IsNull(statement.ConstKeyword);
            Assert.IsNotNull(statement.VarKeyword);
            Assert.AreEqual("x", statement.Identifier?.Sequence);

            AssertHelper.CompareCodeStrings(code, declaration.ToString());
            AssertHelper.NoInvalidTokens(declaration);
        }

        [TestMethod]
        public void ParseLocalConst_IdentifierStartingWithConst_NotSplit()
        {
            var reader = new GDScriptReader();

            var code = "func test():\n\tconst_value = 1\n\tconstant = 2";

            var declaration = reader.ParseFileContent(code);
            var method = declaration.Methods.First();

            Assert.AreEqual(2, method.Statements.Count);
            Assert.IsInstanceOfType(method.Statements[0], typeof(GDExpressionStatement));
            Assert.IsInstanceOfType(method.Statements[1], typeof(GDExpressionStatement));
            Assert.AreEqual("const_value = 1", method.Statements[0].ToString().Trim());
            Assert.AreEqual("constant = 2", method.Statements[1].ToString().Trim());

            AssertHelper.CompareCodeStrings(code, declaration.ToString());
            AssertHelper.NoInvalidTokens(declaration);
        }

        [TestMethod]
        public void ParseStatement_IdentifierCons_NotTreatedAsKeywordFragment()
        {
            var reader = new GDScriptReader();

            var code = "func test():\n\tcons = 5";

            var declaration = reader.ParseFileContent(code);
            var method = declaration.Methods.First();

            Assert.AreEqual(1, method.Statements.Count);
            Assert.IsInstanceOfType(method.Statements[0], typeof(GDExpressionStatement));

            AssertHelper.CompareCodeStrings(code, declaration.ToString());
            AssertHelper.NoInvalidTokens(declaration);
        }

        [TestMethod]
        public void ParseLocalConst_InsideIfElifElse()
        {
            var reader = new GDScriptReader();

            var code = "func test(a):\n\tif a:\n\t\tconst A = 1\n\telif a:\n\t\tconst B = 2\n\telse:\n\t\tconst C = 3";

            var declaration = reader.ParseFileContent(code);

            var constants = declaration.AllNodes
                .OfType<GDVariableDeclarationStatement>()
                .Where(x => x.IsConstant)
                .ToList();

            Assert.AreEqual(3, constants.Count);

            AssertHelper.CompareCodeStrings(code, declaration.ToString());
            AssertHelper.NoInvalidTokens(declaration);
        }

        [TestMethod]
        public void ParseLocalConst_InsideForAndWhile()
        {
            var reader = new GDScriptReader();

            var code = "func test(items):\n\tfor i in items:\n\t\tconst A = 1\n\twhile true:\n\t\tconst B = 2";

            var declaration = reader.ParseFileContent(code);

            var constants = declaration.AllNodes
                .OfType<GDVariableDeclarationStatement>()
                .Where(x => x.IsConstant)
                .ToList();

            Assert.AreEqual(2, constants.Count);

            AssertHelper.CompareCodeStrings(code, declaration.ToString());
            AssertHelper.NoInvalidTokens(declaration);
        }

        [TestMethod]
        public void ParseLocalConst_InsideMatchCase()
        {
            var reader = new GDScriptReader();

            var code = "func test(a):\n\tmatch a:\n\t\t1:\n\t\t\tconst A = 1\n\t\t_:\n\t\t\tconst B = 2";

            var declaration = reader.ParseFileContent(code);

            var constants = declaration.AllNodes
                .OfType<GDVariableDeclarationStatement>()
                .Where(x => x.IsConstant)
                .ToList();

            Assert.AreEqual(2, constants.Count);

            AssertHelper.CompareCodeStrings(code, declaration.ToString());
            AssertHelper.NoInvalidTokens(declaration);
        }

        [TestMethod]
        public void ParseLocalConst_InsideLambda()
        {
            var reader = new GDScriptReader();

            var code = "func test():\n\tvar f = func():\n\t\tconst A = 1\n\t\treturn A";

            var declaration = reader.ParseFileContent(code);

            var constants = declaration.AllNodes
                .OfType<GDVariableDeclarationStatement>()
                .Where(x => x.IsConstant)
                .ToList();

            Assert.AreEqual(1, constants.Count);

            AssertHelper.CompareCodeStrings(code, declaration.ToString());
            AssertHelper.NoInvalidTokens(declaration);
        }

        [TestMethod]
        public void ParseLocalConst_WithTrailingComment()
        {
            var reader = new GDScriptReader();

            var code = "func test():\n\tconst A = 1  # note";

            var declaration = reader.ParseFileContent(code);
            var statement = (GDVariableDeclarationStatement)declaration.Methods.First().Statements[0];

            Assert.IsTrue(statement.IsConstant);

            Assert.AreEqual(code, declaration.ToString());
            AssertHelper.NoInvalidTokens(declaration);
        }

        [TestMethod]
        public void ParseLocalConst_WithMultiLineSplit()
        {
            var reader = new GDScriptReader();

            var code = "func test():\n\tconst A = \\\n\t\t1";

            var declaration = reader.ParseFileContent(code);
            var statement = (GDVariableDeclarationStatement)declaration.Methods.First().Statements[0];

            Assert.IsTrue(statement.IsConstant);
            Assert.IsNotNull(statement.Initializer);

            Assert.AreEqual(code, declaration.ToString());
            AssertHelper.NoInvalidTokens(declaration);
        }

        [TestMethod]
        public void ParseLocalConst_CarriageReturnLineEndings()
        {
            var reader = new GDScriptReader();

            var code = "func test():\r\n\tconst A = 1\r\n\treturn A\r\n";

            var declaration = reader.ParseFileContent(code);

            var statement = (GDVariableDeclarationStatement)declaration.Methods.First().Statements[0];
            Assert.IsTrue(statement.IsConstant);

            Assert.AreEqual(code, declaration.ToOriginalString());
            AssertHelper.NoInvalidTokens(declaration);
        }

        [TestMethod]
        public void ParseLocalConst_WithoutInitializer_DoesNotThrow()
        {
            var reader = new GDScriptReader();

            var code = "func test():\n\tconst A\n\treturn 1";

            var declaration = reader.ParseFileContent(code);

            Assert.IsNotNull(declaration);
            Assert.AreEqual(code, declaration.ToString());
        }

        [TestMethod]
        public void ParseLocalConst_FormSlots_AreStable()
        {
            var reader = new GDScriptReader();

            var code = "func test():\n\tconst A: int = 1";

            var declaration = reader.ParseFileContent(code);
            var statement = (GDVariableDeclarationStatement)declaration.Methods.First().Statements[0];

            Assert.IsNotNull(statement.TypedForm.Token0);
            Assert.IsNull(statement.TypedForm.Token1);
            Assert.IsNotNull(statement.TypedForm.Token2);
            Assert.IsNotNull(statement.TypedForm.Token3);
            Assert.IsNotNull(statement.TypedForm.Token4);
            Assert.IsNotNull(statement.TypedForm.Token5);
            Assert.IsNotNull(statement.TypedForm.Token6);
        }

        [TestMethod]
        public void ParseLocalConst_Clone_RoundTrips()
        {
            var reader = new GDScriptReader();

            var code = "func test():\n\tconst A: int = 1";

            var declaration = reader.ParseFileContent(code);
            var clone = (GDClassDeclaration)declaration.Clone();

            Assert.AreEqual(code, clone.ToString());

            var statement = (GDVariableDeclarationStatement)clone.Methods.First().Statements[0];
            Assert.IsTrue(statement.IsConstant);
        }
    }
}
