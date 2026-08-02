using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace GDShrapt.Reader.Tests
{
    /// <summary>
    /// Tests for parsing for and while loops.
    /// </summary>
    [TestClass]
    public class LoopStatementParsingTests
    {
        [TestMethod]
        public void ParseForStatement_WithArrayCollection()
        {
            var reader = new GDScriptReader();

            var code = @"for x in [5, 7, 11]:
    print(x)";

            var statement = reader.ParseStatement(code);

            Assert.IsNotNull(statement);
            Assert.IsInstanceOfType(statement, typeof(GDForStatement));

            var forStatement = (GDForStatement)statement;

            Assert.AreEqual("x", forStatement.Variable?.Sequence);
            Assert.IsInstanceOfType(forStatement.Collection, typeof(GDArrayInitializerExpression));
            Assert.AreEqual("[5, 7, 11]", forStatement.Collection.ToString());

            Assert.AreEqual(1, forStatement.Statements.Count);
            Assert.IsInstanceOfType(forStatement.Statements[0], typeof(GDExpressionStatement));

            AssertHelper.CompareCodeStrings(code, statement.ToString());
            AssertHelper.NoInvalidTokens(statement);
        }

        [TestMethod]
        public void ParseForStatement_WithRangeCollection()
        {
            var reader = new GDScriptReader();

            var code = @"for i in range(2, 8, 2):
    print(i)";

            var statement = reader.ParseStatement(code);

            Assert.IsNotNull(statement);
            Assert.IsInstanceOfType(statement, typeof(GDForStatement));

            var forStatement = (GDForStatement)statement;

            Assert.AreEqual("i", forStatement.Variable?.Sequence);
            Assert.IsInstanceOfType(forStatement.Collection, typeof(GDCallExpression));
            Assert.AreEqual("range(2, 8, 2)", forStatement.Collection.ToString());

            Assert.AreEqual(1, forStatement.Statements.Count);
            Assert.IsInstanceOfType(forStatement.Statements[0], typeof(GDExpressionStatement));

            AssertHelper.CompareCodeStrings(code, statement.ToString());
            AssertHelper.NoInvalidTokens(statement);
        }

        [TestMethod]
        public void ParseForStatement_WithTypedVariable()
        {
            var reader = new GDScriptReader();

            var code = "for x: int in range(10):\n\tprint(x)";

            var statement = reader.ParseStatement(code);

            Assert.IsNotNull(statement);
            Assert.IsInstanceOfType(statement, typeof(GDForStatement));

            var forStatement = (GDForStatement)statement;

            Assert.AreEqual("x", forStatement.Variable?.Sequence);
            Assert.IsNotNull(forStatement.VariableType);
            Assert.AreEqual("int", forStatement.VariableType.ToString());
            Assert.IsNotNull(forStatement.TypeColon);
            Assert.IsInstanceOfType(forStatement.Collection, typeof(GDCallExpression));
            Assert.AreEqual("range(10)", forStatement.Collection.ToString());

            Assert.AreEqual(1, forStatement.Statements.Count);

            AssertHelper.CompareCodeStrings(code, statement.ToString());
            AssertHelper.NoInvalidTokens(statement);
        }

        [TestMethod]
        public void ParseForStatement_WithTypedVariable_String()
        {
            var reader = new GDScriptReader();

            var code = "for path: String in files:\n\tprint(path)";

            var statement = reader.ParseStatement(code);

            Assert.IsNotNull(statement);
            Assert.IsInstanceOfType(statement, typeof(GDForStatement));

            var forStatement = (GDForStatement)statement;

            Assert.AreEqual("path", forStatement.Variable?.Sequence);
            Assert.IsNotNull(forStatement.VariableType);
            Assert.AreEqual("String", forStatement.VariableType.ToString());
            Assert.IsInstanceOfType(forStatement.Collection, typeof(GDIdentifierExpression));
            Assert.AreEqual("files", forStatement.Collection.ToString());

            AssertHelper.CompareCodeStrings(code, statement.ToString());
            AssertHelper.NoInvalidTokens(statement);
        }

        [TestMethod]
        public void ParseForStatement_WithTypedVariable_Dictionary()
        {
            var reader = new GDScriptReader();

            var code = "for item: Dictionary in list:\n\tpass";

            var statement = reader.ParseStatement(code);

            Assert.IsNotNull(statement);
            Assert.IsInstanceOfType(statement, typeof(GDForStatement));

            var forStatement = (GDForStatement)statement;

            Assert.AreEqual("item", forStatement.Variable?.Sequence);
            Assert.IsNotNull(forStatement.VariableType);
            Assert.AreEqual("Dictionary", forStatement.VariableType.ToString());

            AssertHelper.CompareCodeStrings(code, statement.ToString());
            AssertHelper.NoInvalidTokens(statement);
        }

        [TestMethod]
        public void ParseForStatement_WithTypedVariable_ArrayGeneric()
        {
            var reader = new GDScriptReader();

            var code = "for item: Array[int] in data:\n\tpass";

            var statement = reader.ParseStatement(code);

            Assert.IsNotNull(statement);
            Assert.IsInstanceOfType(statement, typeof(GDForStatement));

            var forStatement = (GDForStatement)statement;

            Assert.AreEqual("item", forStatement.Variable?.Sequence);
            Assert.IsNotNull(forStatement.VariableType);
            Assert.AreEqual("Array[int]", forStatement.VariableType.ToString());

            AssertHelper.CompareCodeStrings(code, statement.ToString());
            AssertHelper.NoInvalidTokens(statement);
        }

        [TestMethod]
        public void ParseForStatement_WithTypedVariable_QualifiedType()
        {
            var reader = new GDScriptReader();

            var code = "for t: MyClass.MyEnum in some_array:\n\tprint(t)";

            var statement = reader.ParseStatement(code);

            Assert.IsNotNull(statement);
            Assert.IsInstanceOfType(statement, typeof(GDForStatement));

            var forStatement = (GDForStatement)statement;

            Assert.AreEqual("t", forStatement.Variable?.Sequence);
            Assert.IsNotNull(forStatement.TypeColon);
            Assert.IsInstanceOfType(forStatement.VariableType, typeof(GDSubTypeNode));

            var subType = (GDSubTypeNode)forStatement.VariableType;

            Assert.IsInstanceOfType(subType.OverType, typeof(GDSingleTypeNode));
            Assert.AreEqual("MyClass", ((GDSingleTypeNode)subType.OverType).Type?.Sequence);
            Assert.IsNotNull(subType.Point);
            Assert.AreEqual("MyEnum", subType.Type?.Sequence);
            Assert.AreEqual("MyClass.MyEnum", subType.BuildName());
            Assert.AreEqual("MyClass.MyEnum", subType.ToString());

            Assert.IsNotNull(forStatement.InKeyword);
            Assert.IsInstanceOfType(forStatement.Collection, typeof(GDIdentifierExpression));
            Assert.AreEqual("some_array", forStatement.Collection.ToString());
            Assert.AreEqual(1, forStatement.Statements.Count);

            AssertHelper.CompareCodeStrings(code, statement.ToString());
            AssertHelper.NoInvalidTokens(statement);
        }

        [TestMethod]
        public void ParseForStatement_WithTypedVariable_QualifiedType_ExactRoundTrip()
        {
            var reader = new GDScriptReader();

            var code = "for  t:   MyClass.MyEnum    in   some_array:\n\tpass";

            var statement = reader.ParseStatement(code);

            Assert.IsInstanceOfType(statement, typeof(GDForStatement));
            Assert.IsInstanceOfType(((GDForStatement)statement).VariableType, typeof(GDSubTypeNode));
            Assert.IsNotNull(((GDForStatement)statement).InKeyword);

            Assert.AreEqual(code, statement.ToString());
            AssertHelper.NoInvalidTokens(statement);
        }

        [TestMethod]
        public void ParseForStatement_SimpleType_InKeywordNotConsumedAsType()
        {
            var reader = new GDScriptReader();

            var code = "for i: int in arr:\n\tpass";

            var statement = reader.ParseStatement(code);

            Assert.IsInstanceOfType(statement, typeof(GDForStatement));

            var forStatement = (GDForStatement)statement;

            Assert.IsInstanceOfType(forStatement.VariableType, typeof(GDSingleTypeNode));
            Assert.AreEqual("int", forStatement.VariableType.ToString());
            Assert.IsNotNull(forStatement.InKeyword);
            Assert.AreEqual("in", forStatement.InKeyword.ToString());
            Assert.AreEqual("arr", forStatement.Collection?.ToString());

            Assert.AreEqual(code, statement.ToString());
            AssertHelper.NoInvalidTokens(statement);
        }

        [TestMethod]
        public void ParseForStatement_WithTypedVariable_ArrayOfQualifiedType()
        {
            var reader = new GDScriptReader();

            var code = "for t: Array[MyClass.MyEnum] in data:\n\tpass";

            var statement = reader.ParseStatement(code);

            Assert.IsInstanceOfType(statement, typeof(GDForStatement));

            var forStatement = (GDForStatement)statement;

            Assert.IsInstanceOfType(forStatement.VariableType, typeof(GDArrayTypeNode));
            Assert.IsInstanceOfType(((GDArrayTypeNode)forStatement.VariableType).InnerType, typeof(GDSubTypeNode));
            Assert.AreEqual("Array[MyClass.MyEnum]", forStatement.VariableType.ToString());
            Assert.IsNotNull(forStatement.InKeyword);

            Assert.AreEqual(code, statement.ToString());
            AssertHelper.NoInvalidTokens(statement);
        }

        [TestMethod]
        public void ParseForStatement_WithTypedVariable_DictionaryGeneric()
        {
            var reader = new GDScriptReader();

            var code = "for kv: Dictionary[String, int] in data:\n\tpass";

            var statement = reader.ParseStatement(code);

            Assert.IsInstanceOfType(statement, typeof(GDForStatement));

            var forStatement = (GDForStatement)statement;

            Assert.IsInstanceOfType(forStatement.VariableType, typeof(GDDictionaryTypeNode));
            Assert.AreEqual("Dictionary[String, int]", forStatement.VariableType.ToString());
            Assert.IsNotNull(forStatement.InKeyword);

            Assert.AreEqual(code, statement.ToString());
            AssertHelper.NoInvalidTokens(statement);
        }

        [TestMethod]
        public void ParseForStatement_WithTypedVariable_ArrayGeneric_SpaceBeforeBracket()
        {
            var reader = new GDScriptReader();

            var code = "for item: Array [int] in data:\n\tpass";

            var statement = reader.ParseStatement(code);

            Assert.IsInstanceOfType(statement, typeof(GDForStatement));
            Assert.IsNotNull(((GDForStatement)statement).InKeyword);

            Assert.AreEqual(code, statement.ToString());
            AssertHelper.NoInvalidTokens(statement);
        }

        [TestMethod]
        public void ParseForStatement_WithTypedVariable_BareArrayThenIn()
        {
            var reader = new GDScriptReader();

            var code = "for item: Array in data:\n\tpass";

            var statement = reader.ParseStatement(code);

            Assert.IsInstanceOfType(statement, typeof(GDForStatement));

            var forStatement = (GDForStatement)statement;

            Assert.IsInstanceOfType(forStatement.VariableType, typeof(GDSingleTypeNode));
            Assert.AreEqual("Array", forStatement.VariableType.ToString());
            Assert.IsNotNull(forStatement.InKeyword);
            Assert.AreEqual("data", forStatement.Collection?.ToString());

            Assert.AreEqual(code, statement.ToString());
            AssertHelper.NoInvalidTokens(statement);
        }

        [TestMethod]
        public void ParseForStatement_NoTypeAnnotation_StillWorks()
        {
            var reader = new GDScriptReader();

            var code = "for i in arr:\n\tpass";

            var statement = reader.ParseStatement(code);

            Assert.IsInstanceOfType(statement, typeof(GDForStatement));

            var forStatement = (GDForStatement)statement;

            Assert.IsNull(forStatement.TypeColon);
            Assert.IsNull(forStatement.VariableType);
            Assert.IsNotNull(forStatement.InKeyword);
            Assert.AreEqual("arr", forStatement.Collection?.ToString());

            Assert.AreEqual(code, statement.ToString());
            AssertHelper.NoInvalidTokens(statement);
        }

        [TestMethod]
        public void ParseForStatement_TypeAnnotationFollowedByNewLine_PreservesTypeText()
        {
            var reader = new GDScriptReader();

            var code = "for t: MyClass.MyEnum\n\tpass";

            var statement = reader.ParseStatement(code);

            Assert.IsInstanceOfType(statement, typeof(GDForStatement));
            Assert.IsNotNull(((GDForStatement)statement).VariableType);

            Assert.AreEqual(code, statement.ToString());
        }

        [TestMethod]
        public void ParseForStatement_QualifiedType_BodyStatementsNestedCorrectly()
        {
            var reader = new GDScriptReader();

            var code = "func bar():\n\tfor t: MyClass.MyEnum in items:\n\t\tprint(t)\n\treturn\n";

            var declaration = reader.ParseFileContent(code);

            var method = declaration.Methods.First();
            Assert.AreEqual(2, method.Statements.Count);

            var forStatement = (GDForStatement)method.Statements[0];
            Assert.IsInstanceOfType(forStatement.VariableType, typeof(GDSubTypeNode));
            Assert.AreEqual(1, forStatement.Statements.Count);

            AssertHelper.CompareCodeStrings(code, declaration.ToString());
            AssertHelper.NoInvalidTokens(declaration);
        }

        [TestMethod]
        public void ParseWhileStatement_WithBoolCondition()
        {
            var reader = new GDScriptReader();

            var code = @"while true:
    print(a)";

            var statement = reader.ParseStatement(code);

            Assert.IsNotNull(statement);
            Assert.IsInstanceOfType(statement, typeof(GDWhileStatement));

            var whileStatement = (GDWhileStatement)statement;

            Assert.IsInstanceOfType(whileStatement.Condition, typeof(GDBoolExpression));
            Assert.AreEqual("true", whileStatement.Condition.ToString());

            Assert.AreEqual(1, whileStatement.Statements.Count);
            Assert.IsInstanceOfType(whileStatement.Statements[0], typeof(GDExpressionStatement));

            AssertHelper.CompareCodeStrings(code, statement.ToString());
            AssertHelper.NoInvalidTokens(statement);
        }

        [TestMethod]
        public void ParseForStatement_BodyStatementsNestedCorrectly()
        {
            var reader = new GDScriptReader();

            var code = "func bar() -> int:\n\tvar total: int = 0\n\tfor i: int in range(10):\n\t\ttotal += i\n\treturn total\n";

            var classDecl = reader.ParseFileContent(code);
            var method = classDecl.Members.OfType<GDMethodDeclaration>().First();

            // Method body should have exactly 3 statements: var, for, return
            Assert.AreEqual(3, method.Statements.Count,
                $"Expected 3 statements in method body, got {method.Statements.Count}: " +
                string.Join(", ", method.Statements.Select(s => s.GetType().Name)));

            Assert.IsInstanceOfType(method.Statements[0], typeof(GDVariableDeclarationStatement));
            Assert.IsInstanceOfType(method.Statements[1], typeof(GDForStatement));
            Assert.IsInstanceOfType(method.Statements[2], typeof(GDExpressionStatement)); // return

            var forStatement = (GDForStatement)method.Statements[1];

            // The body of the for loop should contain "total += i"
            Assert.AreEqual(1, forStatement.Statements.Count,
                $"Expected 1 statement in for body, got {forStatement.Statements.Count}");
            Assert.IsInstanceOfType(forStatement.Statements[0], typeof(GDExpressionStatement));

            AssertHelper.CompareCodeStrings(code, classDecl.ToString());
            AssertHelper.NoInvalidTokens(classDecl);
        }

        [TestMethod]
        public void ParseForStatement_BodyStatementsNestedCorrectly_Spaces()
        {
            var reader = new GDScriptReader();

            var code = "func bar() -> int:\n    var total: int = 0\n    for i: int in range(10):\n        total += i\n    return total\n";

            var classDecl = reader.ParseFileContent(code);
            var method = classDecl.Members.OfType<GDMethodDeclaration>().First();

            Assert.AreEqual(3, method.Statements.Count,
                $"Expected 3 statements in method body, got {method.Statements.Count}: " +
                string.Join(", ", method.Statements.Select(s => s.GetType().Name)));

            Assert.IsInstanceOfType(method.Statements[0], typeof(GDVariableDeclarationStatement));
            Assert.IsInstanceOfType(method.Statements[1], typeof(GDForStatement));
            Assert.IsInstanceOfType(method.Statements[2], typeof(GDExpressionStatement));

            var forStatement = (GDForStatement)method.Statements[1];

            Assert.AreEqual(1, forStatement.Statements.Count,
                $"Expected 1 statement in for body, got {forStatement.Statements.Count}");

            AssertHelper.NoInvalidTokens(classDecl);
        }

        [TestMethod]
        public void ParseForStatement_MultipleBodyStatements()
        {
            var reader = new GDScriptReader();

            var code = "func test():\n\tfor i in range(10):\n\t\tprint(i)\n\t\ttotal += i\n\tprint(\"done\")\n";

            var classDecl = reader.ParseFileContent(code);
            var method = classDecl.Members.OfType<GDMethodDeclaration>().First();

            Assert.AreEqual(2, method.Statements.Count,
                $"Expected 2 statements in method body, got {method.Statements.Count}: " +
                string.Join(", ", method.Statements.Select(s => s.GetType().Name)));

            Assert.IsInstanceOfType(method.Statements[0], typeof(GDForStatement));
            Assert.IsInstanceOfType(method.Statements[1], typeof(GDExpressionStatement));

            var forStatement = (GDForStatement)method.Statements[0];

            Assert.AreEqual(2, forStatement.Statements.Count,
                $"Expected 2 statements in for body, got {forStatement.Statements.Count}");

            AssertHelper.CompareCodeStrings(code, classDecl.ToString());
            AssertHelper.NoInvalidTokens(classDecl);
        }

        [TestMethod]
        public void ParseForStatement_ViaParseStatements_BodyNestedCorrectly()
        {
            var reader = new GDScriptReader();

            var code = "for i: int in range(10):\n\ttotal += i\nreturn total\n";

            var statements = reader.ParseStatements(code);

            Assert.AreEqual(2, statements.Count,
                $"Expected 2 top-level statements, got {statements.Count}: " +
                string.Join(", ", statements.Select(s => s.GetType().Name)));

            Assert.IsInstanceOfType(statements[0], typeof(GDForStatement));

            var forStatement = (GDForStatement)statements[0];

            Assert.AreEqual(1, forStatement.Statements.Count,
                $"Expected 1 statement in for body, got {forStatement.Statements.Count}");

            AssertHelper.NoInvalidTokens(forStatement);
        }

        [TestMethod]
        public void ParseForStatement_ViaParseStatementsList_BodyNestedCorrectly()
        {
            var reader = new GDScriptReader();

            var code = "for i: int in range(10):\n\ttotal += i\nreturn total\n";

            var stmtList = reader.ParseStatementsList(code);

            Assert.AreEqual(2, stmtList.Count,
                $"Expected 2 top-level statements, got {stmtList.Count}: " +
                string.Join(", ", stmtList.Select(s => s.GetType().Name)));

            Assert.IsInstanceOfType(stmtList[0], typeof(GDForStatement));

            var forStatement = (GDForStatement)stmtList[0];

            Assert.AreEqual(1, forStatement.Statements.Count,
                $"Expected 1 statement in for body, got {forStatement.Statements.Count}");

            AssertHelper.NoInvalidTokens(stmtList);
        }

        [TestMethod]
        public void ParseWhileStatement_WithComplexCondition()
        {
            var reader = new GDScriptReader();

            var code = @"while a.b == null and b.c == null:
    print(a)
    print(b)";

            var statement = reader.ParseStatement(code);

            Assert.IsNotNull(statement);
            Assert.IsInstanceOfType(statement, typeof(GDWhileStatement));

            var whileStatement = (GDWhileStatement)statement;

            Assert.IsInstanceOfType(whileStatement.Condition, typeof(GDDualOperatorExpression));
            Assert.AreEqual("a.b == null and b.c == null", whileStatement.Condition.ToString());

            Assert.AreEqual(2, whileStatement.Statements.Count);
            Assert.IsInstanceOfType(whileStatement.Statements[0], typeof(GDExpressionStatement));
            Assert.IsInstanceOfType(whileStatement.Statements[1], typeof(GDExpressionStatement));

            AssertHelper.CompareCodeStrings(code, statement.ToString());
            AssertHelper.NoInvalidTokens(statement);
        }
    }
}
