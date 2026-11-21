using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Singulink.CodeAnalysis.Analyzers;

/// <content>
/// The check of the references of a compilation: an assembly that was not built with the analyzer and that references an assembly which
/// declares a non-copyable type, or an assembly of checked code whose members let other code get at a non-copyable value (SCA1025), and
/// the 'trusted' lines of the list files that vouch for one, or for an assembly whose class is the base class of a class of the
/// compilation that holds a non-copyable value.
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
        CompilationAnalysisContext context,
        SafeMemberList list,
        NonCopyableTypes types,
        IReadOnlyList<(AdditionalText File, SourceText Text)> listFiles,
        ConcurrentDictionary<IAssemblySymbol, bool> trustedForBase)
    {
        var compilation = context.Compilation;
        var referenced = compilation.SourceModule.ReferencedAssemblySymbols;
        var declaring = FindDeclaringAssemblies(compilation, referenced, list);
        var usesNonCopyableTypes = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);

        // A 'trusted' line also has an effect for an assembly whose class is the base class of a class of the compilation that holds a
        // non-copyable value: it answers the warning for a base class of which nothing else says whether it clones itself.
        var hasEffect = new HashSet<IAssemblySymbol>(trustedForBase.Keys, SymbolEqualityComparer.Default);

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

        // The reference to a declaring assembly says nothing of the built-in non-copyable types, which the framework declares, nor of
        // what is bound to a marked type parameter of a generic class: an assembly needs no reference of its own to copy either. What it
        // needs is a way to the value, and the ways that no call shows start at a member of checked code that lets other code get at one.
        // So an assembly that references an assembly of checked code with such a member is reported as well.
        var exposing = new Dictionary<IAssemblySymbol, (ISymbol Member, ITypeSymbol Type)?>(SymbolEqualityComparer.Default);

        foreach (var assembly in referenced)
        {
            if (SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly) ||
                AnalyzedAssemblies.IsAnalyzed(assembly) ||
                usesNonCopyableTypes.Contains(assembly))
            {
                continue;
            }

            foreach (var reference in assembly.Modules.SelectMany(static m => m.ReferencedAssemblySymbols))
            {
                // Code of the framework and of a third party that was not built with the analyzer is that code's own business, as its
                // types are.
                if (SymbolEqualityComparer.Default.Equals(reference, assembly) || !AnalyzedAssemblies.IsAnalyzed(reference))
                    continue;

                // The internal members of a referenced assembly are not imported, so what they let out is not seen here. An assembly
                // that checked code names as a friend can use them, and gets the warning whatever that code holds.
                if (IsFriend(reference, assembly))
                {
                    usesNonCopyableTypes.Add(assembly);

                    if (!list.IsTrusted(assembly))
                        context.ReportDiagnostic(Diagnostic.Create(Rules.UncheckedFriendAssembly, Location.None, assembly.Name, reference.Name));

                    break;
                }

                if (!exposing.TryGetValue(reference, out var exposed))
                    exposing[reference] = exposed = FindExposedValue(reference, types);

                if (exposed is not { } found)
                    continue;

                usesNonCopyableTypes.Add(assembly);

                if (!list.IsTrusted(assembly))
                {
                    object?[] arguments = [assembly.Name, reference.Name, Display(found.Member), found.Type];
                    WithoutNullableAnnotations(arguments);
                    context.ReportDiagnostic(Diagnostic.Create(Rules.UncheckedAssemblyOfExposedValue, Location.None, arguments));
                }

                break;
            }
        }

        hasEffect.UnionWith(usesNonCopyableTypes);

        foreach (var (file, text) in listFiles)
        {
            var entries = new List<SafeListEntry>();
            SafeMemberList.Parse(text, entries, errors: null);

            foreach (var entry in entries)
            {
                if (entry.Marker is not SafeListMarker.Trusted)
                    continue;

                string name = SafeMemberList.GetAssemblyName(entry);

                if (GetWhyTrustedHasNoEffect(name, compilation, referenced, hasEffect) is { } why)
                {
                    var location = Location.Create(file.Path, entry.Line.Span, text.Lines.GetLinePositionSpan(entry.Line.Span));
                    context.ReportDiagnostic(Diagnostic.Create(Rules.SafeListTrustedHasNoEffect, location, name, why));
                }
            }
        }
    }

    private static string? GetWhyTrustedHasNoEffect(
        string name, Compilation compilation, IEnumerable<IAssemblySymbol> referenced, HashSet<IAssemblySymbol> hasEffect)
    {
        if (string.Equals(name, compilation.Assembly.Name, StringComparison.OrdinalIgnoreCase))
            return "it is the assembly of the compilation, which is checked code";

        var named = referenced.Where(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();

        if (named.Count is 0)
            return "the compilation references no assembly of that name";

        if (named.Any(hasEffect.Contains))
            return null;

        return named.All(AnalyzedAssemblies.IsAnalyzed)
            ? "the assembly was built with the analyzer, so it is checked code"
            : "the assembly references no assembly that declares a non-copyable type or that lets a non-copyable value out, and the " +
              "line answers no warning for a field behind a class of the assembly";
    }

    /// <summary>
    /// Gets a member of an assembly of checked code that lets code of another assembly get at a non-copyable value which checked code
    /// holds, with the type that carries the value: the first one by its display, so that every build names the same. Null if the
    /// assembly has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Other code gets at a value that no call hands to it in three ways. It reads a field. It calls a member that returns a reference
    /// to the value, or a container or another object that carries it: a value that a member of checked code returns by value is a new
    /// one. Or it is the code behind a member that checked code calls: an implementation of an interface member, an override, or the
    /// method of a delegate, which is given a reference or a container. A value that is passed by value is the callee's from then on.
    /// </para>
    /// <para>
    /// A member counts where every assembly can reach it: public or protected, in a type that is so in all its containing types. An
    /// internal member is not imported where its assembly is referenced, so it is not seen here: see <see cref="IsFriend"/>. A type
    /// counts through its base class and its interfaces as well, as 'class Locks : List&lt;SpinLock&gt;' does. What a type carries is
    /// what the classifier says: a non-copyable type, a type parameter that is marked, and a type that mentions either.
    /// </para>
    /// </remarks>
    private static (ISymbol Member, ITypeSymbol Type)? FindExposedValue(IAssemblySymbol assembly, NonCopyableTypes types)
    {
        (ISymbol Member, ITypeSymbol Type, string Display)? best = null;
        Visit(assembly.GlobalNamespace);
        return best is { } found ? (found.Member, found.Type) : null;

        void Visit(INamespaceOrTypeSymbol container)
        {
            foreach (var member in container.GetMembers())
            {
                if (member is INamespaceSymbol ns)
                    Visit(ns);
                else if (member is INamedTypeSymbol type && IsReachable(type))
                    VisitType(type);
            }
        }

        void VisitType(INamedTypeSymbol type)
        {
            if (type.BaseType is { } baseType && types.Carries(baseType))
                Add(type, baseType);

            foreach (var implemented in type.Interfaces)
            {
                if (types.Carries(implemented))
                    Add(type, implemented);
            }

            // The method of a delegate is the code of whoever made the delegate.
            if (type.DelegateInvokeMethod is { } invoke)
                AddGiven(type, invoke);

            foreach (var member in type.GetMembers())
            {
                if (member is INamedTypeSymbol nested)
                {
                    if (IsReachable(nested))
                        VisitType(nested);

                    continue;
                }

                if (!IsReachable(member))
                    continue;

                switch (member)
                {
                    case IFieldSymbol { IsConst: false } field when types.Carries(field.Type):
                        Add(field, field.Type);
                        break;

                    case IPropertySymbol property:
                        if (HandsOut(property.Type, property.ReturnsByRef || property.ReturnsByRefReadonly))
                            Add(property, property.Type);

                        break;

                    case IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.UserDefinedOperator or MethodKind.Conversion } method:
                        if (HandsOut(method.ReturnType, method.ReturnsByRef || method.ReturnsByRefReadonly))
                            Add(method, method.ReturnType);

                        if (CanBeImplementedElsewhere(method))
                            AddGiven(method, method);

                        break;
                }
            }
        }

        // What a member returns: a reference to a value, or by value something that carries one and is not itself the value.
        bool HandsOut(ITypeSymbol type, bool byReference) => types.Carries(type) && (byReference || !types.IsNonCopyableType(type));

        // What the code behind a member is given: a reference to a value, or a container. A value by value is a move.
        void AddGiven(ISymbol member, IMethodSymbol method)
        {
            foreach (var parameter in method.Parameters)
            {
                if (HandsOut(parameter.Type, parameter.RefKind is not RefKind.None))
                {
                    Add(member, parameter.Type);
                    return;
                }
            }
        }

        void Add(ISymbol member, ITypeSymbol type)
        {
            string display = member.ToDisplayString();

            if (best is null || string.CompareOrdinal(display, best.Value.Display) < 0)
                best = (member, type, display);
        }

        static bool CanBeImplementedElsewhere(IMethodSymbol method) =>
            method.ContainingType is { TypeKind: TypeKind.Interface } ||
            ((method.IsVirtual || method.IsAbstract || method.IsOverride) && !method.IsSealed && method.ContainingType is { IsSealed: false });

        static bool IsReachable(ISymbol symbol) =>
            symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal;
    }

    // Whether an assembly names another one in an InternalsVisibleTo attribute, by its simple name in any case: the attribute's argument is
    // the name, which a public key can follow after a comma.
    private static bool IsFriend(IAssemblySymbol assembly, IAssemblySymbol friend)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "InternalsVisibleToAttribute" } attributeClass &&
                NonCopyableTypes.IsInNamespace(attributeClass, "System.Runtime.CompilerServices") &&
                attribute.ConstructorArguments is [{ Value: string name }, ..])
            {
                int comma = name.IndexOf(',');

                if (string.Equals((comma < 0 ? name : name[..comma]).Trim(), friend.Name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
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
