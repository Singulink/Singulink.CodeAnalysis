using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// Tells whether a compilation uses the updated memory safety rules, and whether a place in code is in an unsafe context under them.
/// Under the current rules the analyzer does not ask where a report is.
/// </summary>
/// <remarks>
/// <para>
/// The analyzer is built against a compiler that has neither the updated rules nor the <c>unsafe(expr)</c> expression, so both are found
/// at run time, in the compiler that hosts the analyzer.</para>
/// <para>
/// The rules: an <c>unsafe</c> block and an <c>unsafe(expr)</c> expression are unsafe contexts, and so is the <c>: base(...)</c> or
/// <c>: this(...)</c> initializer of an <c>unsafe</c> constructor. No other modifier makes anything unsafe. From C# 13 on the body of an
/// iterator is a safe context, so a local function that is an iterator is safe inside an unsafe block, and an <c>unsafe</c> block in it
/// is unsafe again.</para>
/// </remarks>
internal static class UnsafeContext
{
    /// <summary>
    /// The feature flag that turns the updated memory safety rules on in a compiler that has no property for them.
    /// </summary>
    private const string UpdatedRulesFeature = "updated-memory-safety-rules";

    // The kind of 'unsafe(expr)', which the compiler that the analyzer is built against does not have. The name is looked up in the
    // SyntaxKind of the compiler that runs the analyzer, which has it if it can parse the expression.
    private static readonly int UnsafeExpressionKind = Enum.TryParse<SyntaxKind>("UnsafeExpression", out var kind) ? (int)kind : -1;

    // IModuleSymbol.MemorySafetyRulesVersion, of an enum with Version1 = 1 and Version2 = 2, of a compiler that has the updated rules.
    private static readonly PropertyInfo? ModuleRulesVersion = typeof(IModuleSymbol).GetProperty("MemorySafetyRulesVersion");

    /// <summary>
    /// Gets a value indicating whether a compilation uses the updated memory safety rules. A compiler with the property of the module
    /// says so there; an older one that knows the rules has only the feature flag. A compiler that does not know the rules at all takes
    /// the flag and applies the current rules, so the flag counts only where the compiler has <c>unsafe(expr)</c>, which came with
    /// the rules. The version of the options of the compilation is not read: it was seen to stay 1 where the module's is 2.
    /// </summary>
    public static bool UsesUpdatedRules(Compilation compilation)
    {
        if (ModuleRulesVersion?.GetValue(compilation.SourceModule) is { } version)
            return Convert.ToInt32(version, System.Globalization.CultureInfo.InvariantCulture) >= 2;

        return UnsafeExpressionKind >= 0 && compilation.SyntaxTrees.FirstOrDefault()?.Options.Features.ContainsKey(UpdatedRulesFeature) is true;
    }

    /// <summary>
    /// Gets a value indicating whether a node is code in an unsafe context under the updated rules: inside a body, an initializer or the
    /// arguments of a base constructor, and not at a declaration, which has no unsafe context.
    /// </summary>
    /// <param name="node">The node.</param>
    public static bool ContainsCode(SyntaxNode node) => IsInCode(node) && Contains(node);

    /// <summary>
    /// Gets a value indicating whether a node is in an unsafe context under the updated rules.
    /// </summary>
    /// <param name="node">The node.</param>
    public static bool Contains(SyntaxNode node)
    {
        bool iteratorBodiesAreSafe = node.SyntaxTree.Options is CSharpParseOptions { LanguageVersion: >= LanguageVersion.CSharp13 };
        SyntaxNode? child = null;

        for (var current = node; current is not null; child = current, current = current.Parent)
        {
            if (current.IsKind(SyntaxKind.UnsafeStatement) || current.RawKind == UnsafeExpressionKind)
                return true;

            switch (current)
            {
                case LocalFunctionStatementSyntax function when iteratorBodiesAreSafe && IsIteratorBody(child, function.Body):
                    return false;

                // A constructor is never an iterator, and nothing around a member makes it unsafe.
                case ConstructorDeclarationSyntax constructor:
                    return HasUnsafe(constructor.Modifiers) && child == constructor.Initializer;

                case MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    // Whether a node is inside code and not at the declaration of a member or a type. A local function or a lambda is declared in code,
    // so its parameters are code as well. The default value of a parameter and the value of an enum member are parts of a declaration.
    private static bool IsInCode(SyntaxNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case BlockSyntax or ArrowExpressionClauseSyntax or ConstructorInitializerSyntax or AnonymousFunctionExpressionSyntax or GlobalStatementSyntax:
                case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax or PropertyDeclarationSyntax }:
                case ArgumentListSyntax { Parent: PrimaryConstructorBaseTypeSyntax }:
                    return true;

                case MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    private static bool HasUnsafe(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.UnsafeKeyword);

    // Whether the node that the walk came from is the body of a function that is an iterator. An expression body cannot hold 'yield'.
    private static bool IsIteratorBody(SyntaxNode? child, BlockSyntax? body) =>
        body is not null && child == body && body.DescendantNodes(static node => node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .Any(static node => node is YieldStatementSyntax);
}
