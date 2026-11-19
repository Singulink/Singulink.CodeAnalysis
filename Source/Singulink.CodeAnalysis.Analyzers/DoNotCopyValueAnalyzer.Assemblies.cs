using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Singulink.CodeAnalysis.Analyzers;

/// <content>
/// The check of the references of a compilation: an assembly that was not built with the analyzer and that references an assembly which
/// declares a non-copyable type (SCA1025), and the 'trusted' lines of the list files that vouch for one.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    // The assembly of the attributes package. An assembly that references it can mark a struct, also where the compilation lacks it.
    private const string AttributesAssemblyName = "Singulink.CodeAnalysis";

    /// <summary>
    /// Reports every assembly that the compilation references, that was not built with the analyzer, that itself references an assembly
    /// which declares a non-copyable type, and that no 'trusted' line vouches for: its code can copy a value of that type behind a type of
    /// checked code or of the framework, where no call shows it. Then reports the 'trusted' lines that have no effect.
    /// </summary>
    /// <remarks>
    /// Code can name a type only where its assembly references the assembly that declares the type, or one that forwards it there, so
    /// the references of each assembly are what is looked at. The types are read only in the assemblies that can declare a marked struct:
    /// see <see cref="FindDeclaringAssemblies"/>. The work is done once, at the end of the compilation.
    /// </remarks>
    private static void AnalyzeUncheckedReferences(
        CompilationAnalysisContext context, SafeMemberList list, IReadOnlyList<(AdditionalText File, SourceText Text)> listFiles)
    {
        var compilation = context.Compilation;
        var referenced = compilation.SourceModule.ReferencedAssemblySymbols;
        var declaring = FindDeclaringAssemblies(compilation, referenced, list);
        var usesNonCopyableTypes = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);

        if (declaring.Count > 0)
        {
            foreach (var assembly in referenced)
            {
                if (SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly) ||
                    FindDeclaringReference(assembly, declaring) is not { } found ||
                    AnalyzedAssemblies.IsAnalyzed(assembly))
                {
                    continue;
                }

                usesNonCopyableTypes.Add(assembly);

                if (!list.IsTrusted(assembly))
                    context.ReportDiagnostic(Diagnostic.Create(Rules.UncheckedAssembly, Location.None, assembly.Name, found.Assembly.Name, found.Type));
            }
        }

        foreach (var (file, text) in listFiles)
        {
            var entries = new List<SafeListEntry>();
            SafeMemberList.Parse(text, entries, errors: null);

            foreach (var entry in entries)
            {
                if (entry.Marker is not SafeListMarker.Trusted)
                    continue;

                string name = SafeMemberList.GetAssemblyName(entry);

                if (GetWhyTrustedHasNoEffect(name, compilation, referenced, usesNonCopyableTypes) is { } why)
                {
                    var location = Location.Create(file.Path, entry.Line.Span, text.Lines.GetLinePositionSpan(entry.Line.Span));
                    context.ReportDiagnostic(Diagnostic.Create(Rules.SafeListTrustedHasNoEffect, location, name, why));
                }
            }
        }
    }

    private static string? GetWhyTrustedHasNoEffect(
        string name, Compilation compilation, IEnumerable<IAssemblySymbol> referenced, HashSet<IAssemblySymbol> usesNonCopyableTypes)
    {
        if (string.Equals(name, compilation.Assembly.Name, StringComparison.OrdinalIgnoreCase))
            return "it is the assembly of the compilation, which is checked code";

        var named = referenced.Where(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();

        if (named.Count is 0)
            return "the compilation references no assembly of that name";

        if (named.Any(usesNonCopyableTypes.Contains))
            return null;

        return named.All(AnalyzedAssemblies.IsAnalyzed)
            ? "the assembly was built with the analyzer, so it is checked code"
            : "the assembly references no assembly that declares a non-copyable type";
    }

    // The first assembly that an assembly references which declares a non-copyable type or forwards one, with that type.
    private static (IAssemblySymbol Assembly, INamedTypeSymbol Type)? FindDeclaringReference(
        IAssemblySymbol assembly, Dictionary<IAssemblySymbol, INamedTypeSymbol> declaring)
    {
        foreach (var module in assembly.Modules)
        {
            foreach (var reference in module.ReferencedAssemblySymbols)
            {
                if (!SymbolEqualityComparer.Default.Equals(reference, assembly) && declaring.TryGetValue(reference, out var type))
                    return (reference, type);
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the assemblies of the compilation that declare a non-copyable type, the compilation's own included, each with one such type,
    /// and the assemblies that forward one there. A type counts that a [NonCopyable] mark makes non-copyable, or a 'noncopyable' line of a
    /// list file where the type is of checked code, and that no 'copyable' line takes out. The built-in non-copyable types do not: every
    /// assembly references the framework.
    /// </summary>
    /// <remarks>
    /// A struct that a list line names is found by its ID, and its assembly is not read. The types are read only in an assembly that can
    /// mark a struct: one that declares a class named NonCopyableAttribute, of any namespace as a referenced struct is read, or one that
    /// references such an assembly or the attributes package. An assembly of the framework is none of them, and so is not read: what is
    /// asked of it is the names of its types and its references, and whether it forwards one of the types that were found.
    /// </remarks>
    private static Dictionary<IAssemblySymbol, INamedTypeSymbol> FindDeclaringAssemblies(
        Compilation compilation, IEnumerable<IAssemblySymbol> referenced, SafeMemberList list)
    {
        var result = new Dictionary<IAssemblySymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var types = new List<INamedTypeSymbol>();

        void Add(INamedTypeSymbol type)
        {
            if (list.IsCopyable(type) || type.GetDocumentationCommentId() is not { } id || SafeMemberList.IsBuiltInNonCopyable(id))
                return;

            types.Add(type);

            // One type is named in the message, the same in every build.
            if (!result.TryGetValue(type.ContainingAssembly, out var named) ||
                string.CompareOrdinal(type.ToDisplayString(), named.ToDisplayString()) < 0)
            {
                result[type.ContainingAssembly] = type;
            }
        }

        // A struct that only a list line makes non-copyable counts where its assembly is checked code. A type that unchecked code
        // declares, of the framework or of a third party, is that code's own business: otherwise a line for a struct of the framework
        // would report every assembly of the framework.
        foreach (string id in list.UserNonCopyableIds)
        {
            foreach (var symbol in DocumentationCommentId.GetSymbolsForDeclarationId(id, compilation))
            {
                if (symbol is INamedTypeSymbol { TypeKind: TypeKind.Struct } type && AnalyzedAssemblies.IsOfAnalyzedAssembly(type))
                    Add(type);
            }
        }

        var assemblies = new List<IAssemblySymbol> { compilation.Assembly };
        assemblies.AddRange(referenced.Where(a => !SymbolEqualityComparer.Default.Equals(a, compilation.Assembly)));

        var markers = new HashSet<IAssemblySymbol>(assemblies.Where(DeclaresMark), SymbolEqualityComparer.Default);

        foreach (var assembly in assemblies)
        {
            if (markers.Contains(assembly) || ReferencesMark(assembly, markers))
                AddMarkedStructs(assembly.GlobalNamespace, !SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly), Add);
        }

        // A type that has been moved to another assembly is named through the one that forwards it.
        if (types.Count > 0)
        {
            foreach (var assembly in referenced)
            {
                if (result.ContainsKey(assembly))
                    continue;

                foreach (var type in types)
                {
                    if (assembly.ResolveForwardedType(GetTopLevelMetadataName(type)) is { } forwarded &&
                        SymbolEqualityComparer.Default.Equals(forwarded, GetTopLevelType(type)))
                    {
                        result[assembly] = type;
                        break;
                    }
                }
            }
        }

        return result;
    }

    // Whether an assembly declares a class that marks a struct of another assembly: NonCopyableAttribute of any namespace, or NonCopyable
    // with no suffix. The names of its types are what is asked.
    private static bool DeclaresMark(IAssemblySymbol assembly) =>
        assembly.Name is AttributesAssemblyName ||
        assembly.TypeNames.Contains(NonCopyableTypes.NonCopyableAttributeName) ||
        assembly.TypeNames.Contains(NonCopyableTypes.NonCopyableNameWithNoSuffix);

    private static bool ReferencesMark(IAssemblySymbol assembly, HashSet<IAssemblySymbol> markers)
    {
        foreach (var module in assembly.Modules)
        {
            foreach (var reference in module.ReferencedAssemblySymbols)
            {
                if (reference.Name is AttributesAssemblyName || markers.Contains(reference))
                    return true;
            }
        }

        return false;
    }

    private static void AddMarkedStructs(INamespaceOrTypeSymbol container, bool isOfAnotherAssembly, Action<INamedTypeSymbol> add)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceOrTypeSymbol inner)
                AddMarkedStructs(inner, isOfAnotherAssembly, add);

            if (member is INamedTypeSymbol { TypeKind: TypeKind.Struct } type &&
                type.GetAttributes().Any(a => NonCopyableTypes.IsNonCopyableMark(a.AttributeClass, ofAnyNamespace: isOfAnotherAssembly)))
            {
                add(type);
            }
        }
    }

    private static INamedTypeSymbol GetTopLevelType(INamedTypeSymbol type)
    {
        while (type.ContainingType is { } containing)
            type = containing;

        return type;
    }

    private static string GetTopLevelMetadataName(INamedTypeSymbol type)
    {
        var top = GetTopLevelType(type);
        return top.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." + top.MetadataName : top.MetadataName;
    }
}
