using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Singulink.CodeAnalysis.Analyzers;

/// <content>
/// The checks of a class whose objects a class that the symbol list marks 'clones' clones with MemberwiseClone, in code of its own that
/// the analyzer does not read: every field of a class of the compilation that holds a non-copyable value, and every binding of a
/// non-copyable type to a type parameter of such a class in a base list, is copied by that clone (SCA1009). The bindings in an object
/// creation and the uses of an object whose binding no declaration of the compilation judged are checked by the walker.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    /// <summary>
    /// Reports, for a class of the compilation that derives from a class that the symbol list marks 'clones', every instance field that
    /// holds a non-copyable value, on the field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What holds a non-copyable value is what the check of a call of MemberwiseClone counts: an instance field whose type is
    /// non-copyable, a struct or a tuple that holds one included, and not a reference to a container, which the clone shares. A field of
    /// a marked type parameter counts, and its message says that the warning may be suppressed only where the class's own code never
    /// clones its objects: a project that binds a non-copyable type gets its warning where it hands such an object to the cloning code. A class that is listed itself is not affected by its own line: the line says what happens to the classes that
    /// derive from it.
    /// </para>
    /// <para>
    /// A field gets its warning whether or not a call of MemberwiseClone in the compilation also gets one for it: the call and the clone
    /// of the base class are two routes, with two answers.
    /// </para>
    /// </remarks>
    private static void AnalyzeCloningBases(SymbolAnalysisContext context, NonCopyableTypes types)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.TypeKind is not TypeKind.Class)
            return;

        if (types.FindCloningBase(type) is { } cloning)
        {
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (field is not { IsStatic: false, RefKind: RefKind.None } || !types.IsNonCopyableType(field.Type))
                    continue;

                var rule = NonCopyableTypes.MentionsTypeParameter(field.Type) ? Rules.MemberwiseCloneOfTypeParameterThroughListedBase : Rules.MemberwiseCloneThroughListedBase;
                object?[] arguments = [type, cloning, DisplayField(field), field.Type];
                WithoutNullableAnnotations(arguments);
                context.ReportDiagnostic(Diagnostic.Create(rule, GetFieldLocation(field), arguments));
            }
        }
    }

    /// <summary>
    /// Reports the base class of a class of the compilation where it binds a non-copyable type to a type parameter of a class whose objects
    /// a class that the symbol list marks 'clones' clones, on the entry of the base list that names it: in a partial class, the part
    /// whose first entry is the base class.
    /// </summary>
    private static void AnalyzeCloningBinding(SyntaxNodeAnalysisContext context, NonCopyableTypes types)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;

        if (declaration.BaseList?.Types is not [var first, ..] ||
            context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not INamedTypeSymbol { BaseType: { } baseType } ||
            SymbolEqualityComparer.Default.Equals(baseType, baseType.OriginalDefinition) ||
            !SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(first.Type, context.CancellationToken).Symbol, baseType) ||
            types.FindCloningBinding(baseType) is not { } binding)
        {
            return;
        }

        object?[] arguments = [binding.Type, binding.Bound, binding.Cloning];
        WithoutNullableAnnotations(arguments);
        context.ReportDiagnostic(Diagnostic.Create(Rules.CloningBinding, first.Type.GetLocation(), arguments));
    }

    // A field as a message names it: "field 'Guard'" by the property that it backs, and "parameter 'g'" for the field that holds a
    // captured parameter of a primary constructor, which the compiler names '<g>P'.
    private static string DisplayField(IFieldSymbol field)
    {
        if (field.AssociatedSymbol is { } associated)
            return $"field '{associated.Name}'";

        if (GetCapturedParameterName(field) is { } parameter)
            return $"parameter '{parameter}'";

        return $"field '{field.Name}'";
    }

    private static string? GetCapturedParameterName(IFieldSymbol field) =>
        field is { IsImplicitlyDeclared: true, Name: { Length: > 3 } name } && name.StartsWith("<", StringComparison.Ordinal) && name.EndsWith(">P", StringComparison.Ordinal)
            ? name[1..^2]
            : null;

    // Where the warning for a field sits: the field, or the property that it backs. The field that holds a captured parameter of a
    // primary constructor has the parameter as its place. The class if none of them has a place in the source.
    private static Location GetFieldLocation(IFieldSymbol field)
    {
        var definition = field.OriginalDefinition;

        if (definition.AssociatedSymbol?.Locations.FirstOrDefault(static l => l.IsInSource) is { } property)
            return property;

        if (definition.Locations.FirstOrDefault(static l => l.IsInSource) is { } own)
            return own;

        return definition.ContainingType.Locations.FirstOrDefault(static l => l.IsInSource) ?? Location.None;
    }
}
