using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// The members that are known to be safe to use with a non-copyable type bound to a type parameter that is not marked, and the types that are
/// views, non-copyable or erasable: the list that ships in the analyzer, merged with the additional files named <see cref="FileName"/> of a
/// compilation.
/// </summary>
/// <remarks>
/// Entries are looked up by the documentation comment ID of a symbol's original definition, so an entry that names something the compilation does
/// not reference is never asked for and needs no resolving.
/// </remarks>
internal sealed class SafeMemberList
{
    /// <summary>
    /// The name of the additional files that are safe lists.
    /// </summary>
    public const string FileName = "NonCopyableSymbols.txt";

    private const string ForKeyword = "for";
    private const string ClonesKeyword = "clones";
    private const string BorrowsWhenKeyword = "borrowswhen";
    private const string TrueValue = "true";
    private const string FalseValue = "false";
    private const string CompleteValue = "complete";
    private const string NoneValue = "none";
    private const string AssemblyPrefix = "A:";
    private const string OwnedIsForATask = "the marker 'owned' is for an instance property of a class whose type is 'Task<TResult>'";

    private const string DrainsIsForASequence = "the marker 'drains' is for a method that returns a sequence, an IEnumerable<T> or an IAsyncEnumerable<T>";

    private const string ResultIsForABoolean =
        "the marker 'borrowswhen' is for a method that returns bool, Task<bool> or ValueTask<bool>";

    private const string BorrowsWhenTakesOutcomes =
        "'borrowswhen' takes one or more outcomes, separated by commas, as in 'borrowswhen=false'; the outcomes are 'true' and 'false'";

    private const string BorrowsWhenNeedsAParameter =
        "the marker 'borrowswhen' is for a method with a parameter that it takes by value, of a struct type or of a type parameter that the entry covers";

    private const string BuiltInResourceName = "Singulink.CodeAnalysis.Analyzers." + FileName;

    private static readonly Lazy<Dictionary<string, SafeListEntry>> s_builtInEntries = new(() => ToEntryMap(ParseBuiltIn()));

    // The built-in list without the framework structs that it names as non-copyable, for a project that leaves them out.
    private static readonly Lazy<Dictionary<string, SafeListEntry>> s_builtInEntriesWithoutNonCopyableTypes = new(
        () => ToEntryMap([.. ParseBuiltIn().Where(e => e.Marker is not SafeListMarker.NonCopyable)]));

    // The classes that the built-in list marks 'clones'. They are kept apart from the other entries: the marker only adds warnings, so it
    // is not merged with the other marker of a line for the same type, and nothing takes it away.
    private static readonly Lazy<HashSet<string>> s_builtInClones = new(
        () => new HashSet<string>(ParseBuiltIn().Where(e => e.Marker is SafeListMarker.Clones).Select(e => e.Id), StringComparer.Ordinal));

    // The classes whose line of the built-in list says 'clones=complete': its 'clones' lines name every member of the class that clones
    // its object or hands it to code that can.
    private static readonly Lazy<HashSet<string>> s_builtInComplete = new(
        () => new HashSet<string>(ParseBuiltIn().Where(e => e.IsComplete).Select(e => e.Id), StringComparer.Ordinal));

    private readonly Dictionary<string, SafeListEntry> _entries;

    // The IDs of the structs that a 'copyable' line of a user file takes out, which no 'noncopyable' line of a user file names.
    private readonly HashSet<string> _copyable;

    // The IDs that a 'noncopyable' line of a user file names, and the simple names of the assemblies that a 'trusted' line names.
    private readonly HashSet<string> _userNonCopyable;
    private readonly HashSet<string> _trustedAssemblies;

    // The IDs of the classes that a 'clones' line of the built-in list or of a user file names.
    private readonly HashSet<string> _clones;

    // The IDs of the classes that a 'clones' line of the built-in list or of a user file names with 'clones=complete'.
    private readonly HashSet<string> _complete;

    // The IDs of the classes that a line of a user file names with 'clones=none': neither their own code nor that of a class above them
    // clones an object. It is kept apart from the 'clones' lines, which it never takes away.
    private readonly HashSet<string> _neverClones;

    // Null for a symbol that has no entry.
    private readonly ConcurrentDictionary<ISymbol, SafeListEntry?> _entriesBySymbol = new(SymbolEqualityComparer.Default);

    private SafeMemberList(
        Dictionary<string, SafeListEntry> entries,
        HashSet<string>? copyable = null,
        HashSet<string>? userNonCopyable = null,
        HashSet<string>? trustedAssemblies = null,
        HashSet<string>? clones = null,
        HashSet<string>? complete = null,
        HashSet<string>? neverClones = null)
    {
        _entries = entries;
        _copyable = copyable ?? [];
        _userNonCopyable = userNonCopyable ?? [];
        _trustedAssemblies = trustedAssemblies ?? [];
        _clones = clones ?? s_builtInClones.Value;
        _complete = complete ?? s_builtInComplete.Value;
        _neverClones = neverClones ?? [];
    }

    /// <summary>
    /// Gets a value indicating whether a 'clones' line names a member, by its original definition: the method, the property or the event
    /// clones its object, or the object that it is given, field by field with MemberwiseClone, or hands it to code that can.
    /// </summary>
    public bool IsCloningMember(ISymbol definition) =>
        _clones.Count > 0 && definition.GetDocumentationCommentId() is { } id && id[0] is 'M' or 'P' or 'E' && _clones.Contains(id);

    /// <summary>
    /// Gets a value indicating whether a class has complete member lines, by its original definition: a 'clones' line for it, of the
    /// built-in list or of any user file, says 'clones=complete'. Its 'clones' lines then name every member of the class that clones its
    /// object or hands it to code that can, and a member that no line names is taken to do neither.
    /// </summary>
    public bool IsComplete(INamedTypeSymbol definition) =>
        _complete.Count > 0 && definition.GetDocumentationCommentId() is { } id && _complete.Contains(id);

    /// <summary>
    /// Gets a value indicating whether a 'clones=none' line of a user file names a class, by its original definition: neither the class's
    /// own code nor that of a class above it clones an object with MemberwiseClone. The line answers the question that a class of code
    /// which was not built with the analyzer leaves open, and loses to a 'clones' line for the class or for a class above it.
    /// </summary>
    public bool IsNeverClones(INamedTypeSymbol definition) =>
        _neverClones.Count > 0 && definition.TypeKind is TypeKind.Class && definition.GetDocumentationCommentId() is { } id && _neverClones.Contains(id);

    /// <summary>
    /// Gets a value indicating whether a line says that a member makes no copy where the built-in list, or a 'clones' line of the given
    /// files, says that it clones its object: such a line is left out, and gets a warning in its file.
    /// </summary>
    public static bool ContradictsClones(SafeListEntry entry, IEnumerable<string>? clonesOfFiles = null) =>
        entry.Id[0] is 'M' or 'P' or 'E' &&
        IsForMember(entry.Marker) &&
        (s_builtInClones.Value.Contains(entry.Id) || (clonesOfFiles?.Contains(entry.Id, StringComparer.Ordinal) ?? false));

    /// <summary>
    /// Gets a value indicating whether a 'clones' line names a class, by its original definition: the class's own code clones an object
    /// of it with MemberwiseClone, so an object of a class that derives from it can be copied field by field.
    /// </summary>
    public bool IsClones(INamedTypeSymbol definition) =>
        _clones.Count > 0 && definition.TypeKind is TypeKind.Class && definition.GetDocumentationCommentId() is { } id && _clones.Contains(id);

    /// <summary>
    /// Gets the IDs that a 'noncopyable' line of a user file names, whether the compilation has the type or not.
    /// </summary>
    public IReadOnlyCollection<string> UserNonCopyableIds => _userNonCopyable;

    /// <summary>
    /// Creates the list of a compilation from the built-in list and the given user files. Where two entries for the same ID disagree, the one
    /// that claims less applies, whether they are in two user files or in a user file and the built-in list. So a user file can add entries and
    /// can tighten a built-in one, but cannot loosen it, and the order of the files does not matter.
    /// </summary>
    /// <param name="userFiles">The list files of the project.</param>
    /// <param name="includeBuiltInNonCopyableTypes">False to leave out the entries of the built-in list that name a framework struct as
    /// non-copyable. The entries of the user files that do so still apply.</param>
    /// <param name="compilation">The compilation. A line of a user file that does not fit what it names there is reported for the file
    /// and left out here, like a line that cannot be parsed, and so is a 'copyable' line that has no effect there.</param>
    /// <remarks>
    /// A 'copyable' line is not merged with the other entries for its type: it takes the type out of the non-copyable types and leaves
    /// whatever else the list says of it. It overrides the built-in list, which is what it is for, and loses to a 'noncopyable' line
    /// of a user file, as the stricter line does everywhere in the list.
    /// </remarks>
    public static SafeMemberList Create(IReadOnlyList<SourceText> userFiles, bool includeBuiltInNonCopyableTypes, Compilation compilation)
    {
        var builtInEntries = (includeBuiltInNonCopyableTypes ? s_builtInEntries : s_builtInEntriesWithoutNonCopyableTypes).Value;

        if (userFiles.Count is 0)
            return new SafeMemberList(builtInEntries);

        var parsed = new List<SafeListEntry>();

        foreach (var file in userFiles)
            Parse(file, parsed, errors: null);

        var entries = new Dictionary<string, SafeListEntry>(builtInEntries, StringComparer.Ordinal);
        var copyable = new HashSet<string>(StringComparer.Ordinal);
        var userNonCopyable = new HashSet<string>(StringComparer.Ordinal);
        var trustedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clones = new HashSet<string>(s_builtInClones.Value, StringComparer.Ordinal);

        // A class has complete member lines where any 'clones' line for it says so. A plain 'clones' line says nothing of completeness,
        // and, as with 'clones' itself, nothing takes it away: a member that the lines miss gets a 'clones' line of its own, in any file.
        var complete = new HashSet<string>(s_builtInComplete.Value, StringComparer.Ordinal);
        var neverClones = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in parsed)
        {
            // An assembly is no member or type: its line is kept apart.
            if (entry.Marker is SafeListMarker.Trusted)
            {
                trustedAssemblies.Add(GetAssemblyName(entry));
                continue;
            }

            if (GetErrorInCompilation(entry, compilation) is not null)
                continue;

            // A line that says that a member makes no copy, where a 'clones' line says that it clones its object, vouches for nothing.
            if (ContradictsClones(entry, parsed.Where(e => e.Marker is SafeListMarker.Clones).Select(e => e.Id)))
                continue;

            if (entry.Marker is SafeListMarker.Clones)
            {
                clones.Add(entry.Id);

                if (entry.IsComplete)
                    complete.Add(entry.Id);

                continue;
            }

            // A 'clones=none' line is kept apart as a 'clones' line is. Where a 'clones' line names the class or a class above it, that
            // line applies: the check of a class looks for a class that clones itself first.
            if (entry.Marker is SafeListMarker.NeverClones)
            {
                neverClones.Add(entry.Id);
                continue;
            }

            if (entry.Marker is SafeListMarker.Copyable)
            {
                if (GetWhyCopyableHasNoEffect(entry, compilation) is null)
                    copyable.Add(entry.Id);

                continue;
            }

            if (entry.Marker is SafeListMarker.NonCopyable)
                userNonCopyable.Add(entry.Id);

            // A line that would name a member of the built-in list for more type parameters than that list does is left out.
            if (FindWideningOfBuiltIn(entry) is not null)
                continue;

            entries[entry.Id] = entries.TryGetValue(entry.Id, out var existing) ? Combine(existing, entry) : entry;
        }

        copyable.ExceptWith(userNonCopyable);
        return new SafeMemberList(entries, copyable, userNonCopyable, trustedAssemblies, clones, complete, neverClones);
    }

    /// <summary>
    /// Gets the simple name of the assembly that a 'trusted' line names.
    /// </summary>
    public static string GetAssemblyName(SafeListEntry entry) => entry.Id[AssemblyPrefix.Length..];

    /// <summary>
    /// Gets a value indicating whether a 'trusted' line of a user file vouches for an assembly, by its simple name in any case, as the
    /// runtime binds it.
    /// </summary>
    public bool IsTrusted(IAssemblySymbol assembly) => _trustedAssemblies.Count > 0 && _trustedAssemblies.Contains(assembly.Name);

    /// <summary>
    /// Gets a value indicating whether the built-in list names a struct as non-copyable: one of the built-in non-copyable types,
    /// whether a project includes them or not.
    /// </summary>
    public static bool IsBuiltInNonCopyable(string id) => s_builtInEntries.Value.TryGetValue(id, out var entry) && entry.Marker is SafeListMarker.NonCopyable;

    /// <summary>
    /// Gets the entries of the list that ships in the analyzer.
    /// </summary>
    public static IReadOnlyList<SafeListEntry> ParseBuiltIn(List<SafeListError>? errors = null)
    {
        using var stream = typeof(SafeMemberList).Assembly.GetManifestResourceStream(BuiltInResourceName)
            ?? throw new InvalidOperationException($"Resource '{BuiltInResourceName}' is missing.");

        var entries = new List<SafeListEntry>();
        Parse(SourceText.From(stream), entries, errors);
        return entries;
    }

    /// <summary>
    /// Parses a list file: one documentation comment ID per line, with an optional marker after a semicolon and, for a member, an optional
    /// <c>for=</c> with the names of the type parameters that the entry vouches for. A line that starts with <c>#</c> is a comment, and a
    /// <c>#</c> after white space starts a comment that runs to the end of the line.
    /// </summary>
    /// <remarks>
    /// What can only be told once the member or the type is found in a compilation is left to <see cref="GetErrorInCompilation"/>.
    /// </remarks>
    public static void Parse(SourceText text, List<SafeListEntry> entries, List<SafeListError>? errors)
    {
        foreach (var line in text.Lines)
        {
            string content = RemoveComment(line.ToString()).Trim();

            if (content.Length is 0)
                continue;

            if (TryParseEntry(content, line, out var entry, out string? reason))
                entries.Add(entry);
            else
                errors?.Add(new SafeListError(line, reason));
        }
    }

    /// <summary>
    /// Gets what is wrong with an entry that parses but does not fit what it names in a compilation: a name after <c>for=</c> that is not a type
    /// parameter of the member or of a type that contains it, the marker <c>view</c> or <c>noncopyable</c> on something that is not a struct,
    /// the marker <c>erasable</c> on something that is not a generic class or interface, or the marker <c>owned</c> on something that is
    /// not an instance property of a class whose type is <c>Task&lt;TResult&gt;</c>. Null if nothing is wrong, or if the compilation does
    /// not have what the entry names.
    /// </summary>
    public static string? GetErrorInCompilation(SafeListEntry entry, Compilation compilation)
    {
        if (entry.TypeParameterNames is null && entry.Marker is SafeListMarker.None or SafeListMarker.New or SafeListMarker.Trusted)
            return null;

        var symbols = DocumentationCommentId.GetSymbolsForDeclarationId(entry.Id, compilation);

        if (symbols.IsEmpty)
            return null;

        if (entry.Marker is SafeListMarker.Erasable && !symbols.Any(s => CanBeErasable(s)))
            return "the marker 'erasable' is for a generic class or interface";

        if (entry.Marker is SafeListMarker.Clones && entry.Id[0] is 'T' && !symbols.Any(s => s is INamedTypeSymbol { TypeKind: TypeKind.Class }))
            return "the marker 'clones' is for a class, a method, a property or an event";

        if (entry.Marker is SafeListMarker.NeverClones && !symbols.Any(s => s is INamedTypeSymbol { TypeKind: TypeKind.Class }))
            return "'clones=none' is for a class";

        if (entry.Marker is SafeListMarker.View or SafeListMarker.NonCopyable or SafeListMarker.Copyable &&
            !symbols.Any(s => s is INamedTypeSymbol { TypeKind: TypeKind.Struct }))
        {
            string name = entry.Marker switch { SafeListMarker.View => "view", SafeListMarker.NonCopyable => "noncopyable", _ => "copyable" };
            return $"the marker '{name}' is for a struct";
        }

        if (entry.Marker is SafeListMarker.Owned && !symbols.Any(CanBeOwned))
            return OwnedIsForATask;

        if (entry.Marker is SafeListMarker.Drains && !symbols.Any(ReturnsSequence))
            return DrainsIsForASequence;

        // A method that is not called by its name, such as an operator, has no call that a condition could be made of.
        if (entry.Marker is SafeListMarker.BorrowsWhen)
        {
            if (!symbols.Any(s => s is IMethodSymbol { MethodKind: MethodKind.Ordinary }))
                return "the marker 'borrowswhen' is for a method that is called by its name, not for a constructor, an operator, a conversion or an accessor";

            if (!symbols.Any(s => s is IMethodSymbol { MethodKind: MethodKind.Ordinary, ReturnsByRef: false, ReturnsByRefReadonly: false } method && IsBooleanResult(method.ReturnType)))
                return ResultIsForABoolean;
        }

        foreach (string name in entry.TypeParameterNames ?? [])
        {
            if (!symbols.Any(s => CountTypeParameters(s, name) > 0))
                return $"'{name}' is not a type parameter of the member or of a type that contains it";

            // The entry goes by the name alone, so it would vouch for every type parameter that has it.
            if (!symbols.Any(s => CountTypeParameters(s, name) == 1))
                return $"'{name}' names more than one type parameter of the member and of the types that contain it";
        }

        if (entry.Marker is SafeListMarker.BorrowsWhen &&
            !symbols.Any(s => s is IMethodSymbol method && method.Parameters.Any(p => p.RefKind is RefKind.None && CanBeTaken(p.Type, entry))))
        {
            return BorrowsWhenNeedsAParameter;
        }

        return null;
    }

    /// <summary>
    /// Gets a value indicating whether a member's result can say whether it has taken a value: it is a bool, or a task of a bool that
    /// is awaited, Task&lt;bool&gt; or ValueTask&lt;bool&gt;.
    /// </summary>
    public static bool IsBooleanResult(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_Boolean || IsTaskOfBoolean(type);

    /// <summary>
    /// Gets a value indicating whether a type is Task&lt;bool&gt; or ValueTask&lt;bool&gt;.
    /// </summary>
    public static bool IsTaskOfBoolean(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Task" or "ValueTask", Arity: 1, ContainingType: null, TypeArguments: [{ SpecialType: SpecialType.System_Boolean }] } &&
        NonCopyableTypes.IsInNamespace(type, "System.Threading.Tasks");

    /// <summary>
    /// Gets a value indicating whether a 'borrowswhen' entry covers a parameter of a member, by the type of the parameter in the member's
    /// definition: a type parameter that can be a struct, or a struct, where the entry covers every type parameter that the type
    /// mentions. The value that is passed decides the rest: only a value of a non-copyable type is followed.
    /// </summary>
    public static bool CanBeTaken(ITypeSymbol definitionType, SafeListEntry entry) => definitionType switch {
        ITypeParameterSymbol typeParameter => !typeParameter.IsReferenceType && entry.Covers(typeParameter),
        INamedTypeSymbol { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None } type => CoversEveryTypeParameter(type, entry),
        _ => false,
    };

    private static bool CoversEveryTypeParameter(ITypeSymbol type, SafeListEntry entry)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        return type switch {
            ITypeParameterSymbol typeParameter => entry.Covers(typeParameter),
            IArrayTypeSymbol array => CoversEveryTypeParameter(array.ElementType, entry),
            INamedTypeSymbol named => named.TypeArguments.All(argument => CoversEveryTypeParameter(argument, entry)) &&
                (named.ContainingType is not { } containing || CoversEveryTypeParameter(containing, entry)),
            _ => true,
        };
    }

    /// <summary>
    /// Gets why a 'clones' line of a user file has no effect in a compilation: it names no type there, which would leave every class that
    /// derives from the class with no warning, or it names a class that nothing can derive from and no type can be bound to, a static one or
    /// a sealed one that is not generic. Null if the
    /// line has an effect, and for a line that does not fit what it names, which <see cref="GetErrorInCompilation"/> reports. A line that
    /// repeats one of the built-in list, or names a class whose base class is listed, has its effect already and is not reported, as a
    /// repeated line of any other marker is not.
    /// </summary>
    public static string? GetWhyClonesHasNoEffect(SafeListEntry entry, Compilation compilation, ICollection<string> clonesOfFiles)
    {
        var symbols = DocumentationCommentId.GetSymbolsForDeclarationId(entry.Id, compilation);

        if (entry.Id[0] is 'M' or 'P' or 'E')
            return symbols.IsEmpty ? $"the compilation has no {GetMemberKind(entry.Id)} of that ID" : null;

        var classes = symbols.OfType<INamedTypeSymbol>().Where(s => s.TypeKind is TypeKind.Class).ToList();

        if (classes.Count is 0)
            return symbols.IsEmpty ? "the compilation has no type of that ID" : null;

        if (classes.All(c => c.IsStatic))
            return "the class is static, so no class can derive from it";

        // A sealed generic class can still be bound: a non-copyable type bound to it is cloned with the object. A line that says
        // 'clones=complete' for a sealed class below a class that clones itself speaks for the members of the class: its own members and
        // its overrides are judged by it.
        if (classes.All(c => c.IsSealed && !IsGenericOrInGeneric(c) && !(entry.IsComplete && IsBelowClonesClass(c))))
            return "the class is sealed and not generic, so no class can derive from it and no type can be bound to it";

        return null;

        bool IsBelowClonesClass(INamedTypeSymbol type)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (current.OriginalDefinition.GetDocumentationCommentId() is { } id && (clonesOfFiles.Contains(id) || s_builtInClones.Value.Contains(id)))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Gets why a 'clones=none' line of a user file has no effect in a compilation. Null if the line has an effect, for a line that names
    /// nothing there, which is ignored so that one file can serve several projects, and for a line that does not fit what it names, which
    /// <see cref="GetErrorInCompilation"/> reports.
    /// </summary>
    /// <remarks>
    /// The line answers what is not known of a class: whether code that the analyzer cannot read clones an object of it. So it has no
    /// effect for a class that no class can derive from, and for a class whose code is known: one of the compilation or of an assembly
    /// that was built with the analyzer, which is checked in its own build, and one of the framework, of which the built-in list says
    /// which classes clone themselves. Nor does it where a 'clones' line names the class or a class above it: that line applies.
    /// </remarks>
    /// <param name="entry">The line.</param>
    /// <param name="compilation">The compilation.</param>
    /// <param name="clonesOfFiles">The IDs that the 'clones' lines of the symbol list files of the project name.</param>
    public static string? GetWhyNeverClonesHasNoEffect(SafeListEntry entry, Compilation compilation, ICollection<string> clonesOfFiles)
    {
        var classes = DocumentationCommentId.GetSymbolsForDeclarationId(entry.Id, compilation)
            .OfType<INamedTypeSymbol>()
            .Where(s => s.TypeKind is TypeKind.Class)
            .ToList();

        if (classes.Count is 0)
            return null;

        if (classes.All(c => c.IsStatic || c.IsSealed))
            return "the class is static or sealed, so no class can derive from it";

        if (classes.All(c => SymbolEqualityComparer.Default.Equals(c.ContainingAssembly, compilation.Assembly)))
            return "the class is of the compilation, which is checked code";

        if (classes.All(c => AnalyzedAssemblies.IsOfAnalyzedAssembly(c)))
            return "the class is of an assembly that was built with the analyzer, so it is checked code";

        if (classes.All(c => FrameworkAssemblies.Contains(c.ContainingAssembly)))
            return "the class is of the framework, and the built-in symbol list says which classes of the framework clone themselves";

        foreach (var type in classes)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (current.OriginalDefinition.GetDocumentationCommentId() is { } id && (s_builtInClones.Value.Contains(id) || clonesOfFiles.Contains(id)))
                {
                    return SymbolEqualityComparer.Default.Equals(current, type)
                        ? "a 'clones' line says that the class clones itself, and that line applies"
                        : $"a 'clones' line says that its base class '{current.OriginalDefinition.ToDisplayString()}' clones itself, and that line applies";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the kind of member that an ID names by its prefix: a method, a property or an event.
    /// </summary>
    public static string GetMemberKind(string id) => id[0] switch { 'M' => "method", 'P' => "property", _ => "event" };

    /// <summary>
    /// Gets the ID of the property or the event whose accessor a member line names, as 'M:Contoso.Document.get_Title' or
    /// 'M:Contoso.Document.add_Changed(System.EventHandler)'. Null for a line that names anything else, or nothing in the compilation. A line
    /// for an accessor has no effect, whatever its marker: the analyzer looks up every use of an accessor under its property or its event,
    /// so the line belongs to that member. The method that the compiler makes for a property of an extension block is no accessor, and its
    /// ID is the one that the line takes.
    /// </summary>
    public static string? GetAccessorOwnerId(SafeListEntry entry, Compilation compilation)
    {
        if (entry.Id[0] is not 'M')
            return null;

        var symbols = DocumentationCommentId.GetSymbolsForDeclarationId(entry.Id, compilation);

        if (symbols.IsEmpty || !symbols.All(s => s is IMethodSymbol { AssociatedSymbol: IPropertySymbol or IEventSymbol }))
            return null;

        return ((IMethodSymbol)symbols[0]).AssociatedSymbol!.OriginalDefinition.GetDocumentationCommentId();
    }

    private static bool IsGenericOrInGeneric(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets why a 'copyable' line of a user file has no effect in a compilation: it names no type there, it names a struct that the project
    /// declares itself, whose attribute is what to take off, or it names a struct that nothing makes non-copyable by itself, neither the
    /// built-in list nor an attribute, such as ValueTask&lt;TResult&gt;, which is non-copyable only through what it holds. Null if the line
    /// takes a type out, and for a line that does not fit what it names, which <see cref="GetErrorInCompilation"/> reports.
    /// </summary>
    public static string? GetWhyCopyableHasNoEffect(SafeListEntry entry, Compilation compilation)
    {
        var symbols = DocumentationCommentId.GetSymbolsForDeclarationId(entry.Id, compilation);

        if (symbols.IsEmpty)
            return "the compilation has no type of that ID";

        var structs = symbols.OfType<INamedTypeSymbol>().Where(s => s.TypeKind is TypeKind.Struct).ToList();

        if (structs.Count is 0)
            return null;

        if (structs.All(s => SymbolEqualityComparer.Default.Equals(s.ContainingAssembly, compilation.Assembly)))
            return "the project declares the type itself, so take its [NonCopyable] attribute off instead";

        // The built-in entry counts whether the built-in non-copyable types are on or not: the same file serves both kinds of project.
        bool isNonCopyableByItself = structs.Any(s =>
            (s_builtInEntries.Value.TryGetValue(entry.Id, out var builtIn) && builtIn.Marker is SafeListMarker.NonCopyable) ||
            s.GetAttributes().Any(a => NonCopyableTypes.IsNonCopyableMark(a.AttributeClass, ofAnyNamespace: true)));

        return isNonCopyableByItself
            ? null
            : "nothing makes the type non-copyable by itself, neither the built-in list nor an attribute; a struct that holds a non-copyable value stays non-copyable through it";
    }

    /// <summary>
    /// Gets the entry of a member (a method, a property or an event) by its original definition, or null if the list does not have the member.
    /// </summary>
    public SafeListEntry? GetMember(ISymbol definition) => GetEntry(definition) is { } entry && IsForMember(entry.Marker) ? entry : null;

    /// <summary>
    /// Gets the entry of a member by its documentation comment ID, or null if the list does not have the member.
    /// </summary>
    public SafeListEntry? GetMember(string id) => _entries.TryGetValue(id, out var entry) && IsForMember(entry.Marker) ? entry : null;

    // A 'borrowswhen' entry is an ordinary entry besides: the member is safe to use, as with no marker.
    private static bool IsForMember(SafeListMarker marker) =>
        marker is SafeListMarker.None or SafeListMarker.New or SafeListMarker.Borrows or SafeListMarker.Drains or SafeListMarker.Owned or
            SafeListMarker.BorrowsWhen;

    // The marker 'drains' says that the sequence which the method returns takes each element out of its source as it hands it
    // out. That can be said only of a sequence: a list or an array that a method returns holds its elements. And it is said of
    // a method: a property exposes state, and a container that is read from one is stored whatever the list says.
    private static bool ReturnsSequence(ISymbol symbol) => symbol is IMethodSymbol method && IsSequence(method.ReturnType);

    /// <summary>
    /// Gets a value indicating whether a type is <c>IEnumerable&lt;T&gt;</c> or <c>IAsyncEnumerable&lt;T&gt;</c> itself, and gets
    /// the type of its elements.
    /// </summary>
    public static bool IsSequence(ITypeSymbol? type, [NotNullWhen(true)] out ITypeSymbol? elementType)
    {
        elementType = type is INamedTypeSymbol { TypeArguments: [var element], OriginalDefinition: var definition } &&
            (definition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T ||
                (definition is { Name: "IAsyncEnumerable", Arity: 1, ContainingType: null } && NonCopyableTypes.IsInNamespace(definition, "System.Collections.Generic")))
                    ? element
                    : null;

        return elementType is not null;
    }

    private static bool IsSequence(ITypeSymbol? type) => IsSequence(type, out _);

    // The marker 'owned' says that the task belongs to the object alone. It is not accepted for a property that hands out anything else:
    // what a list holds can be used in place by another holder of the object while the one that made it reads it by value. Nor is it
    // accepted on a property of a struct: a call on a struct local can replace the whole local ('this = other'), and a copy of the struct
    // shares the task, so the task cannot belong to one object alone. A static property and one of an interface have no object that a
    // method could have just made.
    private static bool CanBeOwned(ISymbol symbol) =>
        symbol is IPropertySymbol { IsStatic: false, ContainingType.TypeKind: TypeKind.Class } property && IsTaskOfResult(property.Type);

    private static bool IsTaskOfResult(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "Task", Arity: 1, ContainingType: null } && NonCopyableTypes.IsInNamespace(type, "System.Threading.Tasks");

    /// <summary>
    /// Gets a value indicating whether the list names the type as a view by its original definition.
    /// </summary>
    public bool IsView(INamedTypeSymbol definition) => GetEntry(definition)?.Marker is SafeListMarker.View;

    /// <summary>
    /// Gets a value indicating whether the list names the type as non-copyable by its original definition.
    /// </summary>
    public bool IsNonCopyable(INamedTypeSymbol definition) => GetEntry(definition)?.Marker is SafeListMarker.NonCopyable;

    /// <summary>
    /// Gets a value indicating whether a 'copyable' line of a user file takes the type out of the non-copyable types, by its original
    /// definition, whatever the built-in list or an attribute says of it.
    /// </summary>
    public bool IsCopyable(INamedTypeSymbol definition) =>
        _copyable.Count > 0 && definition.GetDocumentationCommentId() is { } id && _copyable.Contains(id);

    /// <summary>
    /// Gets a value indicating whether the list names the type as erasable by its original definition: a generic class or interface of which no
    /// base type and no interface hands out a value of its type arguments.
    /// </summary>
    public bool IsErasable(INamedTypeSymbol definition) => CanBeErasable(definition) && GetEntry(definition)?.Marker is SafeListMarker.Erasable;

    private static bool CanBeErasable(ISymbol symbol) => symbol is INamedTypeSymbol { IsGenericType: true, TypeKind: TypeKind.Class or TypeKind.Interface };

    private static Dictionary<string, SafeListEntry> ToEntryMap(IReadOnlyList<SafeListEntry> entries)
    {
        var result = new Dictionary<string, SafeListEntry>(entries.Count, StringComparer.Ordinal);

        // A 'clones' line is kept apart: see s_builtInClones. A 'clones=none' line is a line of a user file only.
        foreach (var entry in entries)
        {
            if (entry.Marker is not (SafeListMarker.Clones or SafeListMarker.NeverClones))
                result[entry.Id] = entry;
        }

        return result;
    }

    /// <summary>
    /// Gets the number of type parameters of a member and of the types that contain it that have a name. A member and a containing
    /// type, or two containing types, can have type parameters of the same name.
    /// </summary>
    private static int CountTypeParameters(ISymbol member, string name)
    {
        int count = member is IMethodSymbol method ? method.TypeParameters.Count(p => p.Name == name) : 0;

        for (var type = member.ContainingType; type is not null; type = type.ContainingType)
            count += type.TypeParameters.Count(p => p.Name == name);

        return count;
    }

    /// <summary>
    /// Gets, for a member line of a user file that names a member which the built-in list names for some type parameters only, with
    /// <c>for=</c>, the type parameters that the line would add: all of them for a line with no <c>for=</c>. Combined with the built-in
    /// line it would change nothing, since a line of a file can only claim less, so it is left out and gets a warning. Null for any other
    /// line, one that names only type parameters that the built-in line names included.
    /// </summary>
    public static (string Added, string BuiltIn)? FindWideningOfBuiltIn(SafeListEntry entry)
    {
        if (entry.Id[0] is not ('M' or 'P' or 'E') ||
            entry.Marker is SafeListMarker.Clones or SafeListMarker.NonCopyable or SafeListMarker.Copyable ||
            !s_builtInEntries.Value.TryGetValue(entry.Id, out var builtIn) ||
            builtIn.TypeParameterNames is not { } builtInNames)
        {
            return null;
        }

        string builtInText = string.Join("', '", builtInNames);

        if (entry.TypeParameterNames is not { } names)
            return ("every type parameter", builtInText);

        var added = names.Except(builtInNames, StringComparer.Ordinal).ToList();
        return added.Count is 0 ? null : ("'" + string.Join("', '", added) + "'", builtInText);
    }

    /// <summary>
    /// Gets, for a 'borrowswhen' line of a user file that names a member of the built-in list, the outcomes that it lists and the
    /// built-in entry does not, in words: "the outcome 'true'". The merge keeps the outcomes that both lines list, so those have no
    /// effect. Null for any other line, and for one that is left out for the type parameters that it names
    /// (<see cref="FindWideningOfBuiltIn"/>).
    /// </summary>
    public static string? FindOutcomesBeyondBuiltIn(SafeListEntry entry)
    {
        if (entry.Marker is not SafeListMarker.BorrowsWhen ||
            !s_builtInEntries.Value.TryGetValue(entry.Id, out var builtIn) ||
            FindWideningOfBuiltIn(entry) is not null)
        {
            return null;
        }

        var beyond = entry.BorrowedOutcomes & ~builtIn.BorrowedOutcomes;

        if (beyond is SafeListOutcomes.None)
            return null;

        var names = new List<string>(2);

        if (beyond.HasFlag(SafeListOutcomes.True))
            names.Add("'true'");

        if (beyond.HasFlag(SafeListOutcomes.False))
            names.Add("'false'");

        return names.Count is 1 ? "the outcome " + names[0] : "the outcomes " + string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
    }

    /// <summary>
    /// Gets, for a 'borrows' line of a user file that names a member which the built-in list names with 'borrowswhen' or with no marker,
    /// what the built-in entry says in words: "with 'borrowswhen=false'", "with no marker". The merge keeps what the built-in entry says,
    /// so the line has no effect. Null for any other line, for one that is left out for the type parameters that it names (<see
    /// cref="FindWideningOfBuiltIn"/>), and for one whose <c>for=</c> leaves out a type parameter that the built-in entry covers: it takes
    /// the entry away for that type parameter, which is an effect.
    /// </summary>
    public static string? FindBorrowsBeyondBuiltIn(SafeListEntry entry, Compilation compilation)
    {
        if (entry.Marker is not SafeListMarker.Borrows ||
            !s_builtInEntries.Value.TryGetValue(entry.Id, out var builtIn) ||
            builtIn.Marker is not (SafeListMarker.None or SafeListMarker.BorrowsWhen) ||
            FindWideningOfBuiltIn(entry) is not null ||
            LeavesOutTypeParameterOfBuiltIn(entry, builtIn, compilation))
        {
            return null;
        }

        if (builtIn.Marker is SafeListMarker.None)
            return "with no marker";

        var outcomes = new List<string>(2);

        if (builtIn.BorrowedOutcomes.HasFlag(SafeListOutcomes.True))
            outcomes.Add("true");

        if (builtIn.BorrowedOutcomes.HasFlag(SafeListOutcomes.False))
            outcomes.Add("false");

        return "with 'borrowswhen=" + string.Join(",", outcomes) + "'";
    }

    // A line whose 'for=' leaves out a type parameter that the built-in entry covers, by its own 'for=' or, with none, as every type
    // parameter of the member and of the types that contain it. A line that names nothing of the compilation leaves nothing out there.
    private static bool LeavesOutTypeParameterOfBuiltIn(SafeListEntry entry, SafeListEntry builtIn, Compilation compilation)
    {
        if (entry.TypeParameterNames is not { } names)
            return false;

        if (builtIn.TypeParameterNames is { } builtInNames)
            return builtInNames.Except(names, StringComparer.Ordinal).Any();

        return DocumentationCommentId.GetSymbolsForDeclarationId(entry.Id, compilation)
            .Any(member => GetTypeParameterNames(member).Except(names, StringComparer.Ordinal).Any());
    }

    private static IEnumerable<string> GetTypeParameterNames(ISymbol member)
    {
        if (member is IMethodSymbol method)
        {
            foreach (var parameter in method.TypeParameters)
                yield return parameter.Name;
        }

        for (var type = member.ContainingType; type is not null; type = type.ContainingType)
        {
            foreach (var parameter in type.TypeParameters)
                yield return parameter.Name;
        }
    }

    private static SafeListEntry Combine(SafeListEntry first, SafeListEntry second)
    {
        // A member hands out a new value, may be passed a stored value, drains its source, or has a task that is its object's alone, only
        // if both say so, and is vouched for only where both vouch for it. A type that one of them calls non-copyable is non-copyable, and
        // one that only one of them calls erasable is not erasable. A member keeps nothing of a value that it is given in the outcomes that
        // both lines list, where 'borrows' lists every outcome and any other marker none: 'borrows' and 'borrowswhen=false' give
        // 'borrowswhen=false', and 'borrowswhen=false' and 'borrowswhen=true' give no marker.
        var marker = (first.Marker, second.Marker) switch {
            var (one, other) when one == other => one,
            (SafeListMarker.NonCopyable, _) or (_, SafeListMarker.NonCopyable) => SafeListMarker.NonCopyable,
            (SafeListMarker.View, _) or (_, SafeListMarker.View) => SafeListMarker.View,
            (SafeListMarker.Borrows, SafeListMarker.BorrowsWhen) or (SafeListMarker.BorrowsWhen, SafeListMarker.Borrows) => SafeListMarker.BorrowsWhen,
            _ => SafeListMarker.None,
        };

        string[]? typeParameterNames = (first.TypeParameterNames, second.TypeParameterNames) switch {
            (null, var names) => names,
            (var names, null) => names,
            (var firstNames, var secondNames) => firstNames.Intersect(secondNames).ToArray(),
        };

        var borrowed = SafeListOutcomes.None;

        if (marker is SafeListMarker.BorrowsWhen)
        {
            borrowed = first.BorrowedOutcomes & second.BorrowedOutcomes;

            if (borrowed is SafeListOutcomes.None)
                marker = SafeListMarker.None;
        }

        return new SafeListEntry(first.Id, marker, typeParameterNames, first.Line, borrowedOutcomes: borrowed);
    }

    private static string RemoveComment(string line)
    {
        // A '#' inside an ID is part of it, as in "M:System.Collections.Generic.List`1.#ctor", so only one at the start of the line or after white
        // space starts a comment.
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] is '#' && (i is 0 || char.IsWhiteSpace(line[i - 1])))
                return line[..i];
        }

        return line;
    }

    private static bool TryParseEntry(string content, TextLine line, [NotNullWhen(true)] out SafeListEntry? entry, out string reason)
    {
        entry = null;
        string[] parts = content.Split(';');
        string id = parts[0].Trim();

        if (id.Length < 3 || id[1] is not ':' || id[0] is not ('M' or 'P' or 'E' or 'T' or 'A'))
        {
            reason = "expected a documentation comment ID that starts with 'M:', 'P:', 'E:' or 'T:', or the name of an assembly after 'A:'";
            return false;
        }

        if (id[0] is 'A')
            return TryParseAssemblyEntry(id, parts, line, out entry, out reason);

        if (id.Any(char.IsWhiteSpace))
        {
            reason = "a documentation comment ID has no white space";
            return false;
        }

        if (GetErrorInId(id) is { } errorInId)
        {
            reason = errorInId;
            return false;
        }

        bool isType = id[0] is 'T';
        var marker = SafeListMarker.None;
        bool isComplete = false;
        bool hasMarker = false;
        var borrowed = SafeListOutcomes.None;
        List<string>? unknownOutcomes = null;
        string[]? typeParameterNames = null;

        for (int i = 1; i < parts.Length; i++)
        {
            string part = parts[i].Trim();

            // 'for=' ends the entry, and a marker comes first and only once.
            if (typeParameterNames is not null)
            {
                reason = "'for=' is the last part of an entry";
                return false;
            }

            // White space is allowed around every part, so also around the '=' of 'for='.
            if (part.StartsWith(ForKeyword, StringComparison.Ordinal) && part[ForKeyword.Length..].TrimStart() is ['=', .. var names])
            {
                if (isType)
                {
                    reason = "'for=' is for a member";
                    return false;
                }

                typeParameterNames = names.Split(',');

                for (int n = 0; n < typeParameterNames.Length; n++)
                {
                    string name = typeParameterNames[n] = typeParameterNames[n].Trim();

                    if (name.Length is 0 || name.Any(char.IsWhiteSpace))
                    {
                        reason = "'for=' takes one or more type parameter names, separated by commas";
                        return false;
                    }
                }

                continue;
            }

            // 'complete' and 'none' are values of 'clones', not markers of their own.
            if (part is CompleteValue or NoneValue)
            {
                reason = $"'{part}' is a value of the marker 'clones', written 'clones={part}'";
                return false;
            }

            if (hasMarker)
            {
                reason = "an entry has one marker, which 'for=' can follow";
                return false;
            }

            hasMarker = true;

            // 'clones=complete' says of a class that its 'clones' lines name every member that clones its object or hands it on. White
            // space is allowed around the '=', as for 'for='.
            if (part.StartsWith(ClonesKeyword, StringComparison.Ordinal) && part[ClonesKeyword.Length..].TrimStart() is ['=', .. var value])
            {
                string valueText = value.Trim();

                if (valueText is not (CompleteValue or NoneValue))
                {
                    reason = "'clones' takes no value other than 'complete' or 'none', as in 'clones=complete'";
                    return false;
                }

                if (!isType)
                {
                    reason = $"'clones={valueText}' is for a class; a method, a property or an event takes 'clones' alone";
                    return false;
                }

                // 'clones=none' says of a class that neither its own code nor that of a class above it clones an object.
                if (valueText is NoneValue)
                {
                    marker = SafeListMarker.NeverClones;
                    continue;
                }

                marker = SafeListMarker.Clones;
                isComplete = true;
                continue;
            }

            // The value of 'borrowswhen' lists the outcomes of a method in which it keeps nothing of a value that it was given by value:
            // 'true' or 'false' where it returned that. An outcome that it does not list is one in which the method has taken the value,
            // and so is a throw. The outcomes come in any order, each once, with white space allowed around the '=' and the ',', as for
            // 'for='. An outcome that the analyzer does not know is skipped, so the line claims less: a later version of the format can
            // add one. A bare 'borrowswhen' gets the same reason as an empty outcome.
            if (part.StartsWith(BorrowsWhenKeyword, StringComparison.Ordinal) && part[BorrowsWhenKeyword.Length..].TrimStart() is "" or ['=', ..])
            {
                string rest = part[BorrowsWhenKeyword.Length..].TrimStart();
                string[] outcomes = rest.Length is 0 ? [string.Empty] : rest[1..].Split(',');

                for (int n = 0; n < outcomes.Length; n++)
                    outcomes[n] = outcomes[n].Trim();

                if (outcomes.Any(outcome => outcome.Length is 0 || outcome.Any(char.IsWhiteSpace)))
                {
                    reason = BorrowsWhenTakesOutcomes;
                    return false;
                }

                if (outcomes.GroupBy(outcome => outcome, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1) is { } repeated)
                {
                    reason = $"'borrowswhen' lists the outcome '{repeated.Key}' twice";
                    return false;
                }

                if (id[0] is not 'M')
                {
                    reason = isType
                        ? "the marker 'borrowswhen' is for a method; a type takes 'view', 'noncopyable', 'copyable', 'erasable' or 'clones'"
                        : "the marker 'borrowswhen' is for a method";

                    return false;
                }

                foreach (string outcome in outcomes)
                {
                    switch (outcome)
                    {
                        case TrueValue:
                            borrowed |= SafeListOutcomes.True;
                            break;

                        case FalseValue:
                            borrowed |= SafeListOutcomes.False;
                            break;

                        default:
                            (unknownOutcomes ??= []).Add(outcome);
                            break;
                    }
                }

                // 'true' and 'false' together would say that the member takes the value only where it throws, which no member was
                // found to do. A member that keeps nothing of the value whatever happens is written 'borrows'.
                if (borrowed.HasFlag(SafeListOutcomes.True | SafeListOutcomes.False))
                {
                    reason = "'borrowswhen' lists 'true' or 'false', not both; for a member that keeps nothing of the value whatever it returns, write 'borrows'";
                    return false;
                }

                // A line whose every outcome is unknown claims nothing more than a line with no marker.
                if (borrowed is not SafeListOutcomes.None)
                    marker = SafeListMarker.BorrowsWhen;

                continue;
            }

            switch (part)
            {
                case "new" when !isType:
                    marker = SafeListMarker.New;
                    break;

                case "borrows" when !isType:
                    marker = SafeListMarker.Borrows;
                    break;

                case "drains" when id[0] is 'M':
                    marker = SafeListMarker.Drains;
                    break;

                case "drains" when !isType:
                    reason = DrainsIsForASequence;
                    return false;

                case "owned" when id[0] is 'P':
                    marker = SafeListMarker.Owned;
                    break;

                case "owned":
                    reason = OwnedIsForATask;
                    return false;

                case "view" when isType:
                    marker = SafeListMarker.View;
                    break;

                case "noncopyable" when isType:
                    marker = SafeListMarker.NonCopyable;
                    break;

                case "copyable" when isType:
                    marker = SafeListMarker.Copyable;
                    break;

                case "erasable" when isType:
                    marker = SafeListMarker.Erasable;
                    break;

                case "clones":
                    marker = SafeListMarker.Clones;
                    break;

                case "trusted":
                    reason = "the marker 'trusted' is for an assembly, named after 'A:'";
                    return false;

                case "new" or "borrows" or "drains":
                    reason = $"the marker '{part}' is for a member; a type takes 'view', 'noncopyable', 'copyable', 'erasable' or 'clones'";
                    return false;

                case "view" or "noncopyable" or "copyable" or "erasable":
                    reason = $"the marker '{part}' is for a type; a member takes 'new', 'borrows', 'drains', 'owned', 'borrowswhen', 'clones' or no marker";
                    return false;

                default:
                    reason = $"unknown marker '{part}'; the markers are 'new', 'borrows', 'drains', 'owned', 'borrowswhen', 'view', 'noncopyable', 'copyable', 'erasable' and 'clones', and 'trusted' for an assembly";
                    return false;
            }
        }

        if (isType && marker is SafeListMarker.None)
        {
            reason = "a type needs the marker 'view', 'noncopyable', 'copyable', 'erasable' or 'clones'; there is no entry for every member of a type";
            return false;
        }

        entry = new SafeListEntry(id, marker, typeParameterNames, line, isComplete, borrowed, unknownOutcomes?.ToArray());
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Parses the line of an assembly: 'A:' and its simple name, then the marker 'trusted', which is the only one that it takes.
    /// </summary>
    private static bool TryParseAssemblyEntry(string id, string[] parts, TextLine line, [NotNullWhen(true)] out SafeListEntry? entry, out string reason)
    {
        entry = null;
        string name = id[AssemblyPrefix.Length..];

        // A display name ('Contoso, Version=1.0.0.0') or a file name would never match the simple name that the line is looked up by.
        if (name.Any(c => char.IsWhiteSpace(c) || c is ',' or '=' or '/' or '\\') || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            reason = "an assembly is named by its simple name, as in 'A:Contoso.Plugins;trusted'";
            return false;
        }

        if (parts.Length is 1)
        {
            reason = "an assembly needs the marker 'trusted'";
            return false;
        }

        if (parts.Length > 2)
        {
            reason = "an assembly takes the one marker 'trusted' and nothing after it";
            return false;
        }

        if (parts[1].Trim() is not "trusted")
        {
            reason = $"the marker '{parts[1].Trim()}' is not for an assembly, which takes 'trusted'";
            return false;
        }

        entry = new SafeListEntry(id, SafeListMarker.Trusted, null, line);
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Gets what makes an ID one that cannot be a documentation comment ID at all, so that a typing error is not taken for the name of
    /// something that the compilation does not reference. Null if the ID has the shape of one.
    /// </summary>
    private static string? GetErrorInId(string id)
    {
        const string Unbalanced = "the parentheses, braces and brackets of a documentation comment ID come in pairs";
        var open = new Stack<char>();
        int parameterLists = 0;

        for (int i = 2; i < id.Length; i++)
        {
            char c = id[i];

            switch (c)
            {
                case '(' or '{' or '[':
                    if (c is '(' && open.Count is 0 && ++parameterLists > 1)
                        return "a documentation comment ID has one parameter list";

                    open.Push(c);
                    break;

                case ')' or '}' or ']':
                    if (open.Count is 0 || open.Pop() != (c is ')' ? '(' : c is '}' ? '{' : '['))
                        return Unbalanced;

                    break;

                // A name has no empty part, before the parameter list or inside it.
                case '.' when i is 2 || i == id.Length - 1 || id[i - 1] is '.' or '(' or ',' || id[i + 1] is '(' or ')' or ',':
                    return "a name in a documentation comment ID has no empty part";
            }
        }

        if (open.Count > 0)
            return Unbalanced;

        return parameterLists > 0 && id[0] is 'T' or 'E' ? "only a method or a property has a parameter list" : null;
    }

    /// <summary>
    /// Gets the method that the compiler makes for a member of an extension block: a static method of the class that the block is
    /// in, with the receiver as its first parameter and the type parameters of the block before its own. For a property that is
    /// the method of its getter, or of its setter if it has no getter. Null for anything that is no member of an extension block.
    /// </summary>
    /// <remarks>
    /// The compiler can give the method with the type parameters of the block in place of its own, so its definition is taken:
    /// that is what has the ID.
    /// </remarks>
    public static IMethodSymbol? GetExtensionImplementation(ISymbol definition) => definition switch {
        { ContainingType.IsExtension: not true } => null,
        IMethodSymbol method => method.AssociatedExtensionImplementation?.OriginalDefinition,
        IPropertySymbol property => (property.GetMethod ?? property.SetMethod)?.AssociatedExtensionImplementation?.OriginalDefinition,
        _ => null,
    };

    private SafeListEntry? GetEntry(ISymbol definition)
    {
        if (_entriesBySymbol.TryGetValue(definition, out var entry))
            return entry;

        if (definition.GetDocumentationCommentId() is { } id)
            _entries.TryGetValue(id, out entry);

        // A member of an extension block is listed under the ID of its implementation method as well: the ID of the member itself
        // has the name that the compiler gives the block, which changes with the declaration of the receiver.
        if (entry is null && GetExtensionImplementation(definition)?.GetDocumentationCommentId() is { } implementationId)
            _entries.TryGetValue(implementationId, out entry);

        // An entry with a name after 'for=' that the member does not have, or that names more than one of its type parameters, is a
        // line that cannot be parsed, which is reported for the file. It vouches for nothing.
        if (entry?.TypeParameterNames is { } names && !names.All(name => CountTypeParameters(definition, name) == 1))
            entry = null;

        _entriesBySymbol[definition] = entry;
        return entry;
    }
}

/// <summary>
/// What an entry of a safe list says about the member or type it names.
/// </summary>
internal enum SafeListMarker
{
    /// <summary>
    /// A member that is safe to use. What it hands out is as new as what it was called on and with.
    /// </summary>
    None,

    /// <summary>
    /// A member that is safe to use and that hands out a new value, whatever it was called on.
    /// </summary>
    New,

    /// <summary>
    /// A member that is safe to use and that a stored value of a non-copyable type may be passed to by value: it reads the copy during the
    /// call and keeps nothing of it. It is the one marker that is of use on a member that is not generic.
    /// </summary>
    Borrows,

    /// <summary>
    /// A member that is safe to use and that returns a sequence which takes each element out of its source as it hands it out, so
    /// that nothing hands out an element twice. The sequence is a new container, whatever the member was called on.
    /// </summary>
    Drains,

    /// <summary>
    /// An instance property of a class, of type Task&lt;TResult&gt;, that is safe to use and whose task belongs to its object alone: it is
    /// made with the object, no constructor takes it from somewhere else, and no other member hands it out. Its one read on an object
    /// that a member has just made is a new value.
    /// </summary>
    Owned,

    /// <summary>
    /// A method that is safe to use and that keeps nothing of a value that it is given by value in some of its outcomes, written
    /// 'borrowswhen=' and the outcomes (<see cref="SafeListEntry.BorrowedOutcomes"/>): the caller still holds the value there. In every
    /// other outcome the method has taken the value. Unlike <see cref="Borrows"/> it says nothing of a stored value or of a container.
    /// </summary>
    BorrowsWhen,

    /// <summary>
    /// A struct whose copies share the elements instead of copying them.
    /// </summary>
    View,

    /// <summary>
    /// A struct that is a non-copyable type.
    /// </summary>
    NonCopyable,

    /// <summary>
    /// A struct that is an ordinary struct for the project, though the built-in list or an attribute in a referenced assembly makes it a
    /// non-copyable type.
    /// </summary>
    Copyable,

    /// <summary>
    /// A generic class or interface of which no base type and no interface hands out a value of its type arguments, so that a container of the
    /// type may be converted to one of them.
    /// </summary>
    Erasable,

    /// <summary>
    /// A class whose own code clones an object of it with MemberwiseClone, so that an object of a class that derives from it can be copied
    /// field by field. It is kept apart from the other entries and only adds warnings.
    /// </summary>
    Clones,

    /// <summary>
    /// A class of which neither the own code nor that of a class above it clones an object with MemberwiseClone, written 'clones=none'. It
    /// answers the warning for a base class of code that was not built with the analyzer, and loses to a 'clones' line.
    /// </summary>
    NeverClones,

    /// <summary>
    /// An assembly, named by its simple name after 'A:', whose code makes no copy of the non-copyable values that it gets at, also behind a
    /// type of checked code or of the framework. It turns off the finding for the assembly, and the warning for a field behind a base class
    /// of the assembly, and changes nothing else.
    /// </summary>
    Trusted,
}

/// <summary>
/// An entry of a safe list: the documentation comment ID of a member or a type, its marker, and for a member optionally the type parameters that
/// the entry vouches for.
/// </summary>
internal sealed class SafeListEntry(
    string id,
    SafeListMarker marker,
    string[]? typeParameterNames,
    TextLine line,
    bool isComplete = false,
    SafeListOutcomes borrowedOutcomes = SafeListOutcomes.None,
    string[]? unknownOutcomes = null)
{
    public string Id { get; } = id;

    public SafeListMarker Marker { get; } = marker;

    /// <summary>
    /// Gets a value indicating whether the line of a class says 'clones=complete': its 'clones' lines name every member of the class that
    /// clones its object or hands it to code that can.
    /// </summary>
    public bool IsComplete { get; } = isComplete;

    /// <summary>
    /// Gets the outcomes of a call in which the member keeps nothing of a value that it is given by value: those that 'borrowswhen' lists,
    /// every outcome for 'borrows', and none for any other marker.
    /// </summary>
    public SafeListOutcomes BorrowedOutcomes { get; } = marker switch {
        SafeListMarker.Borrows => SafeListOutcomes.True | SafeListOutcomes.False,
        SafeListMarker.BorrowsWhen => borrowedOutcomes,
        _ => SafeListOutcomes.None,
    };

    /// <summary>
    /// Gets the outcomes after 'borrowswhen=' that the analyzer does not know, which the entry leaves out. Null if there are none.
    /// </summary>
    public string[]? UnknownOutcomes { get; } = unknownOutcomes;

    /// <summary>
    /// Gets the names after <c>for=</c>: the type parameters of the member or of a type that contains it that the entry vouches for. Null if the
    /// entry covers every type parameter.
    /// </summary>
    public string[]? TypeParameterNames { get; } = typeParameterNames;

    /// <summary>
    /// Gets the line of the file that the entry is on.
    /// </summary>
    public TextLine Line { get; } = line;

    /// <summary>
    /// Gets a value indicating whether the entry vouches for the member when a non-copyable type or a container is bound to the given type parameter
    /// of the member or of a type that contains it. If it does not, the member counts as not listed for that use.
    /// </summary>
    public bool Covers(ITypeParameterSymbol typeParameter) => TypeParameterNames is null || Array.IndexOf(TypeParameterNames, typeParameter.Name) >= 0;
}

/// <summary>
/// The outcomes of a call of a method that a 'borrowswhen' entry can list.
/// </summary>
[Flags]
internal enum SafeListOutcomes
{
    /// <summary>
    /// No outcome.
    /// </summary>
    None = 0,

    /// <summary>
    /// The method returned true, or a task of a bool that gave true when it was awaited.
    /// </summary>
    True = 1,

    /// <summary>
    /// The method returned false, or a task of a bool that gave false when it was awaited.
    /// </summary>
    False = 2,
}

/// <summary>
/// A line of a safe list file that cannot be parsed.
/// </summary>
internal readonly struct SafeListError(TextLine line, string reason)
{
    public TextLine Line { get; } = line;

    public string Reason { get; } = reason;
}
