using GDShrapt.Abstractions;
using GDShrapt.Reader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GDShrapt.Converter.Planning.Definitions;

public sealed class PrivateFieldRule : IGDConversionRule
{
    public string Name => "private-field";
    public int Priority => 0;
    public List<IGDConversionRuleMetadata> Metadata { get; set; } = [];

    public bool CanConvert(GDConversionNodeContext context) => context.Node is GDVariableDeclaration vd
        && context.Parent is GDClassMembersList { Parent: GDClassDeclaration or GDInnerClassDeclaration }
        && !vd.IsConstant
        && vd.Identifier.Sequence.StartsWith('_')
        && vd.FirstAccessorDeclarationNode == null
        && vd.SecondAccessorDeclarationNode == null;

    public GDConversionNodeResult Convert(GDConversionNodeContext context)
    {
        var declaration = (GDVariableDeclaration)context.Node!;
        var propertyName = declaration.Identifier.Sequence;

        var flowType = context.GetVariableTypeAt(propertyName, context.Node);
        var type = flowType?.DeclaredType ?? flowType?.CurrentType.EffectiveType;
        if ((type == null || type.IsVariant) && declaration.Type != null)
            type = GDSemanticType.FromTypeNode(declaration.Type);

        var supportsDoublePrecision = context.Solution.DefaultProject.SupportsDoublePrecision;
        var csTypeName = GDCSharpTypeMapper.GetTypeName(type, supportsDoublePrecision, context, declaration);
        if (csTypeName == null)
            return GDConversionNodeResult.Unmapped;

        var isReadOnly = declaration.Initializer != null
            && !HasInitAssignment(declaration, context)
            && context.SemanticModel is { } semanticModel
            && semanticModel.GetReferencesTo(propertyName).All(reference => !reference.IsWrite);
        var namingModifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (declaration.IsStatic)
            namingModifiers.Add("static");
        else
            namingModifiers.Add("instance");
        if (declaration.IsConstant)
            namingModifiers.Add("const");
        if (isReadOnly)
            namingModifiers.Add("readonly");
        var newName = context.Solution.DefaultFormatter.Name(
            propertyName,
            new GDConversionNamingContext("field", "private", namingModifiers));

        var (hasInitAssignment, initializerAssignment) =
            GetInitAssignment(context, declaration, newName, type, supportsDoublePrecision);
        if (hasInitAssignment && initializerAssignment == null)
            return GDConversionNodeResult.Unmapped;

        var modifiers = declaration.IsStatic ? "static " : string.Empty;
        var initializer = declaration.Initializer == null
            ? null
            : context.ChildMappings
                .FirstOrDefault(mapping => ReferenceEquals(mapping.Syntax, declaration.Initializer))?
                .Result.CSharpSyntax as ExpressionSyntax
                ?? GDCSharpExpressionConverter.Convert(
                    declaration.Initializer,
                    context,
                    expectedReal: type?.DisplayName == "float",
                    expectedType: type);
        if (declaration.Initializer != null && initializer == null)
            return GDConversionNodeResult.Unmapped;

        var initializerText = initializer == null
            ? string.Empty
            : $" = {initializer.NormalizeWhitespace().ToFullString()}";
        var syntax = SyntaxFactory.ParseMemberDeclaration(
            $"private {modifiers}{(isReadOnly ? "readonly " : string.Empty)}{csTypeName} {newName}{initializerText};");
        if (syntax is not FieldDeclarationSyntax || syntax.ContainsDiagnostics)
            return GDConversionNodeResult.Unmapped;

        var contributions = context.Contributions.AsEnumerable();
        if (initializerAssignment != null)
            contributions = contributions.Append(initializerAssignment);
        return GDConversionNodeResult.Converted(syntax, contributions: contributions);
    }

    private static bool HasInitAssignment(GDVariableDeclaration declaration, GDConversionNodeContext context)
    {
        if (context.Parent?.Parent is not GDNode containingClass)
            return false;
        var initMethod = containingClass.Nodes.OfType<GDClassMembersList>().FirstOrDefault()?
            .OfType<GDClassMember>().OfType<GDMethodDeclaration>()
            .SingleOrDefault(method => method.Identifier.Sequence == "_init");
        return initMethod?.AllNodes.OfType<GDDualOperatorExpression>().Any(expression =>
            expression.OperatorType == GDDualOperatorType.Assignment &&
            expression.LeftExpression is GDIdentifierExpression left &&
            left.Identifier.Sequence == declaration.Identifier.Sequence) == true;
    }

    private static (bool Found, GDConversionConstructorContribution? Contribution) GetInitAssignment(
        GDConversionNodeContext context,
        GDVariableDeclaration declaration,
        string csharpFieldName,
        GDSemanticType? fieldType,
        bool supportsDoublePrecision)
    {
        if (context.Parent?.Parent is not GDNode containingClass)
            return (false, null);

        var classMembers = containingClass.Nodes.OfType<GDClassMembersList>().FirstOrDefault();
        var initMethod = classMembers?
            .OfType<GDClassMember>()
            .OfType<GDMethodDeclaration>()
            .SingleOrDefault(method => method.Identifier.Sequence == "_init");
        if (initMethod == null)
            return (false, null);

        var assignments = initMethod.AllNodes
            .OfType<GDDualOperatorExpression>()
            .Where(expression =>
                expression.OperatorType == GDDualOperatorType.Assignment &&
                expression.LeftExpression is GDIdentifierExpression left &&
                left.Identifier.Sequence == declaration.Identifier.Sequence)
            .ToArray();
        if (assignments.Length != 1)
            return (assignments.Length > 0, null);

        var right = assignments[0].RightExpression;
        ParameterSyntax? parameterSyntax = null;
        ExpressionSyntax? rightSyntax = null;
        if (right is GDIdentifierExpression rightIdentifier &&
            initMethod.Parameters.OfType<GDParameterDeclaration>()
                .FirstOrDefault(parameter => parameter.Identifier.Sequence == rightIdentifier.Identifier.Sequence) is { } parameter)
        {
            var parameterType = parameter.Type == null
                ? fieldType
                : GDSemanticType.FromTypeNode(parameter.Type);
            var typeName = GDCSharpTypeMapper.GetTypeName(parameterType, supportsDoublePrecision, context, parameter);
            if (typeName == null)
                return (true, null);

            var parameterName = NameHelper.ToCamelCase(parameter.Identifier.Sequence.TrimStart('_'));
            parameterSyntax = SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameterName))
                .WithType(SyntaxFactory.ParseTypeName(typeName));
            if (parameter.DefaultValue != null)
            {
                var defaultValue = GDCSharpExpressionConverter.Convert(
                    parameter.DefaultValue,
                    context,
                    expectedReal: parameterType?.DisplayName == "float",
                    expectedType: parameterType);
                if (defaultValue == null)
                    return (true, null);
                parameterSyntax = parameterSyntax.WithDefault(
                    SyntaxFactory.EqualsValueClause(defaultValue));
            }
            rightSyntax = SyntaxFactory.IdentifierName(parameterName);
        }
        else
        {
            rightSyntax = GDCSharpExpressionConverter.Convert(
                right,
                context,
                expectedReal: fieldType?.DisplayName == "float",
                expectedType: fieldType);
        }

        if (rightSyntax == null)
            return (true, null);
        var assignment = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ThisExpression(),
                    SyntaxFactory.IdentifierName(csharpFieldName)),
                rightSyntax));
        return (true, new GDConversionConstructorContribution(parameterSyntax, assignment));
    }
}
