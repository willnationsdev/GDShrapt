using GDShrapt.Reader;
using GDShrapt.Abstractions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GDShrapt.Converter.Planning;

internal static class GDCSharpExpressionConverter
{
    private static readonly HashSet<string> RealNumberConstructors =
    [
        "Vector2", "Vector3", "Vector4", "Color", "Rect2", "Transform2D", "Transform3D", "Basis",
        "Quaternion", "Plane", "AABB", "Projection"
    ];

    public static ExpressionSyntax? Convert(
        GDExpression? expression,
        GDConversionNodeContext context,
        bool expectedReal = false,
        GDSemanticType? expectedType = null,
        GDConversionRealPrecision? realPrecision = null)
    {
        if (expression == null)
            return null;

        return expression switch
        {
            GDNumberExpression number => ConvertNumber(
                number,
                context,
                realPrecision ?? (expectedReal ? GDConversionRealPrecision.Godot : GDConversionRealPrecision.None)),
            GDStringExpression text => ConvertString(text.String),
            GDRawStringExpression text => ConvertString(text.String),
            GDStringNameExpression stringName => ConvertStringName(stringName, context, expectedType),
            GDBoolExpression boolean when boolean.Value.HasValue =>
                boolean.Value.Value ? SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression) :
                    SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression),
            GDIdentifierExpression identifier => ConvertIdentifier(identifier),
            GDBracketExpression bracket => Convert(bracket.InnerExpression, context, expectedReal, expectedType, realPrecision),
            GDSingleOperatorExpression unary => ConvertUnary(unary, context, expectedReal, expectedType, realPrecision),
            GDDualOperatorExpression binary => ConvertBinary(binary, context, expectedReal, expectedType, realPrecision),
            GDMemberOperatorExpression member => ConvertMember(member, context),
            GDCallExpression call => ConvertCall(call, context, expectedType),
            GDArrayInitializerExpression array => ConvertArray(array, context, expectedType),
            GDDictionaryInitializerExpression dictionary => ConvertDictionary(dictionary, context, expectedType),
            GDNodePathExpression nodePath => ConvertNodePath(nodePath, context, expectedType),
            _ => null
        };
    }

    private static ExpressionSyntax? ConvertString(GDStringNode? value)
    {
        if (value == null || value.Parts.Any(part => part is not GDStringPart))
            return null;
        return SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(value.Sequence));
    }

    private static ExpressionSyntax? ConvertStringName(
        GDStringNameExpression value,
        GDConversionNodeContext context,
        GDSemanticType? expectedType)
    {
        var literal = ConvertString(value.String);
        return literal == null
            ? null
            : SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName(
                    context.GetNativeTypeName("StringName", expectedType, value)?.TypeName ??
                    GDCSharpTypeMapper.ResolveTypeName("Godot", "StringName", context)))
                .WithArgumentList(SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(literal))));
    }

    private static ExpressionSyntax ConvertIdentifier(GDIdentifierExpression identifier)
        => identifier.Identifier.Sequence switch
        {
            "null" => SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression),
            "self" => SyntaxFactory.ThisExpression(),
            "super" => SyntaxFactory.BaseExpression(),
            _ => SyntaxFactory.IdentifierName(identifier.Identifier.Sequence)
        };

    private static ExpressionSyntax? ConvertUnary(
        GDSingleOperatorExpression unary,
        GDConversionNodeContext context,
        bool expectedReal,
        GDSemanticType? expectedType,
        GDConversionRealPrecision? realPrecision)
    {
        var target = Convert(unary.TargetExpression, context, expectedReal, expectedType, realPrecision);
        if (target == null)
            return null;

        var kind = unary.OperatorType switch
        {
            GDSingleOperatorType.Negate => SyntaxKind.UnaryMinusExpression,
            GDSingleOperatorType.Not or GDSingleOperatorType.Not2 => SyntaxKind.LogicalNotExpression,
            GDSingleOperatorType.BitwiseNegate => SyntaxKind.BitwiseNotExpression,
            _ => SyntaxKind.None
        };
        return kind == SyntaxKind.None ? null : SyntaxFactory.PrefixUnaryExpression(kind, target);
    }

    private static ExpressionSyntax? ConvertBinary(
        GDDualOperatorExpression binary,
        GDConversionNodeContext context,
        bool expectedReal,
        GDSemanticType? expectedType,
        GDConversionRealPrecision? realPrecision)
    {
        var left = Convert(binary.LeftExpression, context, expectedReal, expectedType, realPrecision);
        var right = Convert(binary.RightExpression, context, expectedReal, expectedType, realPrecision);
        if (left == null || right == null)
            return null;

        var kind = binary.OperatorType switch
        {
            GDDualOperatorType.MoreThan => SyntaxKind.GreaterThanExpression,
            GDDualOperatorType.LessThan => SyntaxKind.LessThanExpression,
            GDDualOperatorType.Assignment => SyntaxKind.SimpleAssignmentExpression,
            GDDualOperatorType.Subtraction => SyntaxKind.SubtractExpression,
            GDDualOperatorType.Division => SyntaxKind.DivideExpression,
            GDDualOperatorType.Multiply => SyntaxKind.MultiplyExpression,
            GDDualOperatorType.Addition => SyntaxKind.AddExpression,
            GDDualOperatorType.AddAndAssign => SyntaxKind.AddAssignmentExpression,
            GDDualOperatorType.NotEqual => SyntaxKind.NotEqualsExpression,
            GDDualOperatorType.MultiplyAndAssign => SyntaxKind.MultiplyAssignmentExpression,
            GDDualOperatorType.SubtractAndAssign => SyntaxKind.SubtractAssignmentExpression,
            GDDualOperatorType.LessThanOrEqual => SyntaxKind.LessThanOrEqualExpression,
            GDDualOperatorType.MoreThanOrEqual => SyntaxKind.GreaterThanOrEqualExpression,
            GDDualOperatorType.Equal => SyntaxKind.EqualsExpression,
            GDDualOperatorType.DivideAndAssign => SyntaxKind.DivideAssignmentExpression,
            GDDualOperatorType.Or or GDDualOperatorType.Or2 => SyntaxKind.LogicalOrExpression,
            GDDualOperatorType.And or GDDualOperatorType.And2 => SyntaxKind.LogicalAndExpression,
            GDDualOperatorType.As => SyntaxKind.AsExpression,
            GDDualOperatorType.BitShiftLeft => SyntaxKind.LeftShiftExpression,
            GDDualOperatorType.BitShiftRight => SyntaxKind.RightShiftExpression,
            GDDualOperatorType.Mod => SyntaxKind.ModuloExpression,
            GDDualOperatorType.Xor => SyntaxKind.ExclusiveOrExpression,
            GDDualOperatorType.BitwiseOr => SyntaxKind.BitwiseOrExpression,
            GDDualOperatorType.BitwiseAnd => SyntaxKind.BitwiseAndExpression,
            _ => SyntaxKind.None
        };
        return kind == SyntaxKind.None ? null : SyntaxFactory.BinaryExpression(kind, left, right);
    }

    private static ExpressionSyntax? ConvertNumber(
        GDNumberExpression number,
        GDConversionNodeContext context,
        GDConversionRealPrecision precision)
    {
        var source = number.Number.Sequence;
        if (precision == GDConversionRealPrecision.None)
            return SyntaxFactory.ParseExpression(source);

        if (!source.Contains('.') && !source.Contains('e') && !source.Contains('E'))
            source += ".0";
        var useSinglePrecision = precision == GDConversionRealPrecision.Single ||
            precision == GDConversionRealPrecision.Godot && !context.Solution.DefaultProject.SupportsDoublePrecision;
        if (useSinglePrecision && !source.EndsWith('f') && !source.EndsWith('F'))
            source += "f";
        return SyntaxFactory.ParseExpression(source);
    }

    private static ExpressionSyntax? ConvertMember(GDMemberOperatorExpression member, GDConversionNodeContext context)
    {
        var caller = Convert(member.CallerExpression, context);
        return caller == null
            ? null
            : SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                caller,
                SyntaxFactory.IdentifierName(member.Identifier.Sequence));
    }

    private static ExpressionSyntax? ConvertCall(
        GDCallExpression call,
        GDConversionNodeContext context,
        GDSemanticType? expectedType)
    {
        string? typeName = null;
        var realArguments = false;
        GDConversionRealPrecision? argumentPrecision = null;
        if (call.CallerExpression is GDIdentifierExpression identifier &&
            IsGodotConstructor(identifier.Identifier.Sequence))
        {
            typeName = identifier.Identifier.Sequence;
            realArguments = RealNumberConstructors.Contains(typeName);
            var nativeType = context.GetNativeTypeName(typeName, expectedType, call);
            if (nativeType != null)
            {
                typeName = nativeType.TypeName;
                argumentPrecision = nativeType.ArgumentPrecision;
                realArguments = argumentPrecision == GDConversionRealPrecision.Godot;
            }
            else
            {
                typeName = ResolveConstructorName(typeName, context);
            }
        }
        else if (call.CallerExpression is GDMemberOperatorExpression { Identifier.Sequence: "new" } factoryCall)
        {
            if (factoryCall.CallerExpression is GDIdentifierExpression factoryIdentifier &&
                IsKnownGodotObjectType(factoryIdentifier.Identifier.Sequence))
            {
                typeName = GDCSharpTypeMapper.ResolveTypeName(
                    "Godot",
                    factoryIdentifier.Identifier.Sequence,
                    context);
            }
            else
            {
                var caller = Convert(factoryCall.CallerExpression, context);
                if (caller != null)
                    typeName = caller.ToString();
            }
        }

        if (typeName == null)
            return null;

        var arguments = new List<ArgumentSyntax>();
        foreach (var argument in call.Parameters)
        {
            var converted = Convert(
                argument,
                context,
                realArguments,
                realPrecision: argumentPrecision);
            if (converted == null)
                return null;
            arguments.Add(SyntaxFactory.Argument(converted));
        }

        return SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName(typeName))
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)));
    }

    private static ExpressionSyntax? ConvertArray(
        GDArrayInitializerExpression array,
        GDConversionNodeContext context,
        GDSemanticType? expectedType)
    {
        var values = new List<ExpressionSyntax>();
        foreach (var value in array.Values)
        {
            var converted = Convert(value, context);
            if (converted == null)
                return null;
            values.Add(converted);
        }

        var typeName = expectedType == null
            ? context.GetNativeTypeName("Array", null, array)?.TypeName ??
              GDCSharpTypeMapper.ResolveTypeName("Godot.Collections", "Array", context)
            : GDCSharpTypeMapper.GetTypeName(
                expectedType,
                context.Solution.DefaultProject.SupportsDoublePrecision,
                context,
                array) ?? GDCSharpTypeMapper.ResolveTypeName("Godot.Collections", "Array", context);
        return SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName(typeName))
            .WithInitializer(SyntaxFactory.InitializerExpression(
                SyntaxKind.CollectionInitializerExpression,
                SyntaxFactory.SeparatedList<ExpressionSyntax>(values)));
    }

    private static ExpressionSyntax? ConvertDictionary(
        GDDictionaryInitializerExpression dictionary,
        GDConversionNodeContext context,
        GDSemanticType? expectedType)
    {
        var entries = new List<ExpressionSyntax>();
        foreach (var entry in dictionary.KeyValues)
        {
            var key = Convert(entry.Key, context);
            var value = Convert(entry.Value, context);
            if (key == null || value == null)
                return null;
            entries.Add(SyntaxFactory.InitializerExpression(
                SyntaxKind.ComplexElementInitializerExpression,
                SyntaxFactory.SeparatedList<ExpressionSyntax>([key, value])));
        }

        var typeName = expectedType == null
            ? context.GetNativeTypeName("Dictionary", null, dictionary)?.TypeName ??
              GDCSharpTypeMapper.ResolveTypeName("Godot.Collections", "Dictionary", context)
            : GDCSharpTypeMapper.GetTypeName(
                expectedType,
                context.Solution.DefaultProject.SupportsDoublePrecision,
                context,
                dictionary) ?? GDCSharpTypeMapper.ResolveTypeName("Godot.Collections", "Dictionary", context);
        return SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName(typeName))
            .WithInitializer(SyntaxFactory.InitializerExpression(
                SyntaxKind.CollectionInitializerExpression,
                SyntaxFactory.SeparatedList(entries)));
    }

    private static ExpressionSyntax? ConvertNodePath(
        GDNodePathExpression nodePath,
        GDConversionNodeContext context,
        GDSemanticType? expectedType)
    {
        var pathText = nodePath.Path.ToString();
        if (string.IsNullOrWhiteSpace(pathText))
            return null;
        var pathLiteral = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(pathText));
        var typeName = context.GetNativeTypeName("NodePath", expectedType, nodePath)?.TypeName ??
            GDCSharpTypeMapper.ResolveTypeName("Godot", "NodePath", context);
        return SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName(typeName))
            .WithArgumentList(SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(pathLiteral))));
    }

    private static bool IsGodotConstructor(string typeName)
        => RealNumberConstructors.Contains(typeName) ||
           typeName is "StringName" or "NodePath" or "Rect2" or "Transform2D" or "Transform3D" or
               "Color" or "RID" or "Callable" or "Signal";

    private static string ResolveConstructorName(string typeName, GDConversionNodeContext context)
        => typeName is "StringName" or "NodePath" or "Vector2" or "Vector2I" or "Vector3" or "Vector3I" or
            "Vector4" or "Vector4I" or "Color" or "Rect2" or "Rect2I" or "Transform2D" or "Transform3D" or
            "Basis" or "Quaternion" or "Plane" or "AABB" or "Projection" or "RID" or "Callable" or "Signal"
            ? GDCSharpTypeMapper.ResolveTypeName("Godot", typeName, context)
            : typeName;

    private static bool IsKnownGodotObjectType(string typeName)
        => GDCSharpTypeMapper.IsGodotTypeName(typeName);
}
