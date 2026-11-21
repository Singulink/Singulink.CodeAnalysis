using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// Reports copies of values of non-copyable types: structs and type parameters marked <c>[NonCopyable]</c>, structs that a safe list names as
/// non-copyable, and generic structs over a non-copyable type.
/// </summary>
/// <remarks>
/// <para>
/// The attributes are recognized by their full name, in the namespace <c>Singulink.CodeAnalysis</c> of any assembly. On a struct of a referenced
/// assembly a <c>NonCopyableAttribute</c> of any namespace counts as well.</para>
/// <para>
/// This file has the entry point: the registration, the options and the list files. The walker over the operations of a member is in
/// <c>DoNotCopyValueAnalyzer.Walker.cs</c>, with one partial file beside it for each concept of the rules, the checks of declarations are in
/// <c>DoNotCopyValueAnalyzer.Declarations.cs</c>, and the checks that follow the paths through a method in <c>DoNotCopyValueAnalyzer.Flow.cs</c>.</para>
/// <para>
/// The rules for values come from Roslyn's <c>RS0042</c> rule (<c>DoNotCopyValue</c>) for C#: a non-copyable value may only be used where the analyzer
/// knows that it is not copied, and any other use is reported as unsupported.</para>
/// <para>
/// On top of them, generic code is checked exactly where its type parameter is marked and is assumed to copy everywhere else: a member of a generic
/// that is used with a non-copyable type bound to a type parameter that is not marked is reported unless a safe list names it, and what a listed
/// member hands out is as new as what it was called on and with.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class DoNotCopyValueAnalyzer : DiagnosticAnalyzer
{
    // The MSBuild property that leaves out the built-in non-copyable types, as the compiler passes it on.
    private const string IncludeBuiltInTypesOption = "build_property.NonCopyableIncludeBuiltInTypes";
    private const string AllowImplicitMovesOption = "build_property.NonCopyableAllowImplicitMoves";

    // What the 'lock' statement calls. A container can be locked if the list has the member.
    private const string MonitorEnterId = "M:System.Threading.Monitor.Enter(System.Object,System.Boolean@)";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Rules.All;

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

        context.RegisterCompilationStartAction(context => {
            // The compilation is checked code whether its assembly carries the marker of an analyzed build or not.
            AnalyzedAssemblies.AddCompilation(context.Compilation.Assembly);

            // The list is built once for the compilation. The two classifiers share it.
            var listFiles = GetListFiles(context.Options, context.CancellationToken);
            var list = SafeMemberList.Create([.. listFiles.Select(f => f.Text)], IncludesBuiltInNonCopyableTypes(context.Options), context.Compilation);
            var typesInCopyingMember = new NonCopyableTypes(list, context.Compilation.Assembly, typeParametersAreCopyable: true);
            var types = new NonCopyableTypes(list, context.Compilation.Assembly, typeParametersAreCopyable: false, definitions: typesInCopyingMember);
            var marks = new MarkCache();
            var moves = new MoveTrust();
            bool allowsImplicitMoves = AllowsImplicitMoves(context.Options);

            context.RegisterCompilationEndAction(moves.Report);
            context.RegisterCompilationEndAction(context => AnalyzeUncheckedReferences(context, list, listFiles));
            context.RegisterSymbolAction(context => AnalyzeCloningBases(context, types), SymbolKind.NamedType);
            context.RegisterSyntaxNodeAction(context => AnalyzeCloningBinding(context, types), SyntaxKind.ClassDeclaration);

            context.RegisterOperationBlockAction(context => {
                // In the body of a member marked [RequiresCopyable] the marked type parameters count as copyable.
                var walker = new NonCopyableWalker(
                    context, NonCopyableTypes.IsMarkedRequiresCopyable(context.OwningSymbol) ? typesInCopyingMember : types, moves, allowsImplicitMoves);

                // Every recursion of the analyzer stops with this exception before the stack runs out. The rest of the member is then not
                // checked, which is reported at the member, and the next member is checked as usual. No other exception is caught.
                try
                {
                    walker.CheckBaseConstructorCalledWithNothingWritten();

                    foreach (var operation in context.OperationBlocks)
                        walker.Visit(operation);

                    walker.Complete();
                }
                catch (InsufficientExecutionStackException)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rules.NestedTooDeeply, GetMemberLocation(context), Display(context.OwningSymbol)));
                }
            });

            context.RegisterSymbolAction(context => AnalyzeField(context, types), SymbolKind.Field);
            context.RegisterSymbolAction(context => AnalyzeProperty(context, types, typesInCopyingMember), SymbolKind.Property);
            context.RegisterSymbolAction(context => AnalyzeNamedType(context, types, typesInCopyingMember, marks), SymbolKind.NamedType);
            context.RegisterSymbolAction(AnalyzeForeignNonCopyableMark, SymbolKind.Method);
            context.RegisterSymbolAction(AnalyzeRequiresCopyableTarget, SymbolKind.Method);

            // An attribute of a referenced assembly that is conditional is rare: only then is every use of an attribute looked at.
            if (FindConditionalMarkers(context.Compilation) is { Count: > 0 } conditionalMarkers)
                context.RegisterSyntaxNodeAction(context => AnalyzeConditionalMarkerUse(context, conditionalMarkers), SyntaxKind.Attribute);

            context.RegisterSymbolAction(context => AnalyzeMovedFromTarget(context, moves), SymbolKind.Method, SymbolKind.Property, SymbolKind.Field, SymbolKind.Event);
            context.RegisterSymbolAction(AnalyzeNotNullRefTarget, SymbolKind.Method, SymbolKind.Property, SymbolKind.NamedType);
        });

        context.RegisterAdditionalFileAction(AnalyzeListFile);
    }

    /// <summary>
    /// Gets a value indicating whether an additional file is a list file: one named 'NonCopyableSymbols.txt', or
    /// 'NonCopyableSymbols.&lt;name&gt;.txt' for a file that is named after a library which ships it, in any folder and whatever
    /// the case. This is the rule of 'BannedSymbols.txt'.
    /// </summary>
    private static bool IsListFile(AdditionalText file)
    {
        const string Prefix = "NonCopyableSymbols.";
        const string Extension = ".txt";

        // The plain name is the prefix and the extension with their dot shared.
        string name = Path.GetFileName(file.Path);
        return name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets a value indicating whether a list file came in a package: it is under the NuGet package root of the build, which the props
    /// file of the package makes visible to the compiler. Its author is the library's, whose own build reads it from the project's folder,
    /// so a warning that a line adds nothing to the built-in list is given there and not in each project that uses the library. A build
    /// that does not pass the property counts every file as its own.
    /// </summary>
    private static bool IsFileOfAPackage(AdditionalText file, AnalyzerOptions options)
    {
        if (!options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(PackageRootOption, out string? root) || string.IsNullOrWhiteSpace(root))
            return false;

        try
        {
            string folder = Path.GetFullPath(root.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(file.Path).StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private const string PackageRootOption = "build_property.NuGetPackageRoot";

    private static IReadOnlyList<(AdditionalText File, SourceText Text)> GetListFiles(AnalyzerOptions options, CancellationToken cancellationToken)
    {
        List<(AdditionalText File, SourceText Text)>? files = null;

        foreach (var file in options.AdditionalFiles)
        {
            if (IsListFile(file) && file.GetText(cancellationToken) is { } text)
                (files ??= []).Add((file, text));
        }

        return files ?? (IReadOnlyList<(AdditionalText File, SourceText Text)>)[];
    }

    /// <summary>
    /// Gets a value indicating whether the framework structs that the built-in list names as non-copyable apply, which they do only if the
    /// MSBuild property NonCopyableIncludeBuiltInTypes is true. The targets file of the package sets it, where the project has not, to true in
    /// a project that references one of the two packages itself and to false in one that only gets the analyzer through a library, and the
    /// props file makes it visible to the compiler. A build in which the targets file did not run gets no value, and errs to off.
    /// </summary>
    private static bool IncludesBuiltInNonCopyableTypes(AnalyzerOptions options) => IsSetTo(options, IncludeBuiltInTypesOption, "true");

    /// <summary>
    /// Gets a value indicating whether a local or a by-value parameter is a new value at its last use, which it is unless the MSBuild
    /// property NonCopyableAllowImplicitMoves is false. With the property false no read of a variable is a move, and NonCopyable.Move is
    /// what hands a value over. The check of the paths of a [MovedFrom] parameter does not depend on it.
    /// </summary>
    private static bool AllowsImplicitMoves(AnalyzerOptions options) => !IsSetToFalse(options, AllowImplicitMovesOption);

    // Only the value 'false' turns a property of the package off, in any case and with space around it.
    private static bool IsSetToFalse(AnalyzerOptions options, string property) => IsSetTo(options, property, "false");

    private static bool IsSetTo(AnalyzerOptions options, string property, string expected) =>
        options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(property, out string? value) &&
        string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static void AnalyzeListFile(AdditionalFileAnalysisContext context)
    {
        if (!IsListFile(context.AdditionalFile) || context.AdditionalFile.GetText(context.CancellationToken) is not { } text)
            return;

        var entries = new List<SafeListEntry>();
        var errors = new List<SafeListError>();
        SafeMemberList.Parse(text, entries, errors);

        // What an entry says of the member or the type that it names can only be checked here, where that is known. An entry for something that
        // the compilation does not reference is ignored, so that one file can serve several projects, except a 'noncopyable' line: a
        // mistyped ID there leaves the type copyable in a build that looks clean. The built-in list is not checked here: its lines for
        // newer frameworks may name nothing.
        // A line that says a member makes no copy is left out where a 'clones' line of any symbol list file names the member, and gets its
        // warning here whichever file that line is in.
        var clonesOfFiles = new HashSet<string>(entries.Where(e => e.Marker is SafeListMarker.Clones).Select(e => e.Id), StringComparer.Ordinal);

        foreach (var other in context.Options.AdditionalFiles)
        {
            if (ReferenceEquals(other, context.AdditionalFile) || !IsListFile(other) || other.GetText(context.CancellationToken) is not { } otherText)
                continue;

            var otherEntries = new List<SafeListEntry>();
            SafeMemberList.Parse(otherText, otherEntries, errors: null);
            clonesOfFiles.UnionWith(otherEntries.Where(e => e.Marker is SafeListMarker.Clones).Select(e => e.Id));
        }

        foreach (var entry in entries)
        {
            if (SafeMemberList.GetErrorInCompilation(entry, context.Compilation) is { } reason)
                errors.Add(new SafeListError(entry.Line, reason));
            else if (entry.Marker is SafeListMarker.NonCopyable && DocumentationCommentId.GetSymbolsForDeclarationId(entry.Id, context.Compilation).IsEmpty)
                Report(Rules.SafeListNonCopyableNamesNothing, entry.Line, entry.Id);
            else if (entry.Marker is SafeListMarker.Copyable && SafeMemberList.GetWhyCopyableHasNoEffect(entry, context.Compilation) is { } why)
                Report(Rules.SafeListCopyableHasNoEffect, entry.Line, entry.Id, why);
            else if (SafeMemberList.GetAccessorOwnerId(entry, context.Compilation) is { } owner)
                Report(Rules.SafeListNamesAccessor, entry.Line, entry.Id, owner);
            else if (entry.Marker is SafeListMarker.Clones && SafeMemberList.GetWhyClonesHasNoEffect(entry, context.Compilation) is { } whyNot)
                Report(Rules.SafeListClonesHasNoEffect, entry.Line, entry.Id, entry.Id[0] switch { 'T' => "a class that clones itself", 'E' => "an event that clones its object", _ => $"a {SafeMemberList.GetMemberKind(entry.Id)} that clones its object" }, whyNot);
            else if (SafeMemberList.ContradictsClones(entry, clonesOfFiles))
                Report(Rules.SafeListContradictsClones, entry.Line, entry.Id);
            else if (SafeMemberList.FindWideningOfBuiltIn(entry) is { } widening && !IsFileOfAPackage(context.AdditionalFile, context.Options))
                Report(Rules.SafeListWidensBuiltIn, entry.Line, entry.Id, widening.Added, widening.BuiltIn);
        }

        foreach (var error in errors)
            Report(Rules.SafeListLine, error.Line, error.Reason);

        void Report(DiagnosticDescriptor rule, TextLine line, params object[] arguments)
        {
            var location = Location.Create(context.AdditionalFile.Path, line.Span, text.Lines.GetLinePositionSpan(line.Span));
            context.ReportDiagnostic(Diagnostic.Create(rule, location, arguments));
        }
    }

    /// <summary>
    /// Gets where a member that an operation block belongs to is declared in the tree of the block, or the start of the block if it has no name
    /// there.
    /// </summary>
    private static Location GetMemberLocation(OperationBlockAnalysisContext context)
    {
        var tree = context.OperationBlocks[0].Syntax.SyntaxTree;
        return context.OwningSymbol.Locations.FirstOrDefault(location => location.SourceTree == tree) ??
            Location.Create(tree, new TextSpan(context.OperationBlocks[0].Syntax.SpanStart, 0));
    }

    private static string Display(ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

    /// <summary>
    /// Takes the nullable annotation off the reference types and type parameters among the arguments of a message, so that an unconstrained
    /// type parameter reads 'T' and not 'T?'.
    /// </summary>
    private static void WithoutNullableAnnotations(object?[] arguments)
    {
        for (int i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] is ITypeSymbol { IsValueType: false, NullableAnnotation: NullableAnnotation.Annotated } type)
                arguments[i] = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        }
    }

    /// <summary>
    /// Gets the local or the parameter that a reference refers to. Null for any other operation.
    /// </summary>
    private static ISymbol? GetReferencedVariable(IOperation? operation) => operation switch {
        ILocalReferenceOperation local => local.Local,
        IParameterReferenceOperation parameter => parameter.Parameter,
        _ => null,
    };

    /// <summary>
    /// Gets the variable that a pattern declares for the value that it matches, if it declares one.
    /// </summary>
    private static ISymbol? GetDeclaredVariable(IOperation? pattern) => pattern switch {
        IDeclarationPatternOperation declaration => declaration.DeclaredSymbol,
        IRecursivePatternOperation recursive => recursive.DeclaredSymbol,
        IListPatternOperation list => list.DeclaredSymbol,
        _ => null,
    };

    private static bool ReturnsByValue(ISymbol? member) => member switch {
        IMethodSymbol method => !method.ReturnsByRef && !method.ReturnsByRefReadonly,
        IPropertySymbol property => !property.ReturnsByRef && !property.ReturnsByRefReadonly,
        _ => false,
    };

    // A member of an extension block is named by its implementation method, whose ID has no generated name in it.
    private static string GetListId(ISymbol definition) =>
        (SafeMemberList.GetExtensionImplementation(definition) ?? definition).GetDocumentationCommentId() ?? Display(definition);

    /// <summary>
    /// What is known of the methods of a compilation that take a [MovedFrom] parameter of a container type to be their own. A method that
    /// is used as a method group for a delegate which gives that parameter the type of a type parameter is passed a slot of generic code,
    /// and the conversion can be anywhere, so the two are brought together when the whole compilation has been seen.
    /// </summary>
    private sealed class MoveTrust
    {
        private readonly ConcurrentDictionary<IMethodSymbol, string> _passedSlots = new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<(IMethodSymbol Member, IParameterSymbol Parameter), byte> _declared = new(ParameterOfMemberComparer.Instance);
        private readonly ConcurrentDictionary<(IMethodSymbol Member, IParameterSymbol Parameter), byte> _checked = new(ParameterOfMemberComparer.Instance);
        private readonly ConcurrentDictionary<IMethodSymbol, ConcurrentDictionary<Location, (string Type, string Parameter)>> _owned = new(SymbolEqualityComparer.Default);

        /// <summary>
        /// Notes a method that a method group converts to a delegate which gives a [MovedFrom] parameter the type of a type parameter.
        /// </summary>
        public void NotePassedSlot(IMethodSymbol method, string giver) => _passedSlots.TryAdd(method.OriginalDefinition, giver);

        /// <summary>
        /// Notes a place where the body of a method moves a container out of a [MovedFrom] parameter as out of something of its own.
        /// </summary>
        public void NoteOwned(IMethodSymbol method, Location location, ITypeSymbol type, IParameterSymbol parameter)
        {
            string typeName = type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();
            _owned.GetOrAdd(method.OriginalDefinition, static _ => new()).TryAdd(location, (typeName, parameter.Name));
        }

        /// <summary>
        /// Notes a [MovedFrom] parameter of a member that has code, where the member is declared. The receiver of an extension block
        /// is a parameter of every member of the block, and is noted with each.
        /// </summary>
        public void NoteDeclared(IMethodSymbol member, IParameterSymbol parameter) => _declared.TryAdd(GetDefinitionPart(member, parameter), 0);

        /// <summary>
        /// Notes a [MovedFrom] parameter whose member was followed along its paths, from every read of the parameter to its assignment.
        /// </summary>
        public void NotePathsChecked(IMethodSymbol member, IParameterSymbol parameter) => _checked.TryAdd(GetDefinitionPart(member, parameter), 0);

        // The two parts of a partial member have a parameter each. The receiver of an extension block belongs to the block.
        private static (IMethodSymbol Member, IParameterSymbol Parameter) GetDefinitionPart(IMethodSymbol member, IParameterSymbol parameter)
        {
            if (member.PartialDefinitionPart is not { } definition)
                return (member, parameter);

            if (parameter.ContainingSymbol is not IMethodSymbol)
                return (definition, NonCopyableTypes.GetExtensionReceiver(definition) ?? parameter);

            return (definition, parameter.Ordinal < definition.Parameters.Length ? definition.Parameters[parameter.Ordinal] : parameter);
        }

        public void Report(CompilationAnalysisContext context)
        {
            // A guard against a kind of member that the check of the paths does not run for: its [MovedFrom] parameter would be
            // clean by omission, as that of a primary constructor was.
            foreach (var (member, declared) in _declared.Keys)
            {
                if (!_checked.ContainsKey((member, declared)) && declared.Locations.FirstOrDefault(static location => location.IsInSource) is { } location)
                    context.ReportDiagnostic(Diagnostic.Create(Rules.MovedFromPathsNotChecked, location, declared.Name, member.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)));
            }

            foreach (var passed in _passedSlots)
            {
                if (!_owned.TryGetValue(passed.Key, out var places))
                    continue;

                foreach (var place in places)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Rules.ContainerMovedFromSlotParameterOfMethodGroup, place.Key, place.Value.Type, place.Value.Parameter, passed.Value));
                }
            }
        }

        // A parameter together with the member that it is a parameter of: the receiver of an extension block is one of every member.
        private sealed class ParameterOfMemberComparer : IEqualityComparer<(IMethodSymbol Member, IParameterSymbol Parameter)>
        {
            public static readonly ParameterOfMemberComparer Instance = new();

            public bool Equals((IMethodSymbol Member, IParameterSymbol Parameter) x, (IMethodSymbol Member, IParameterSymbol Parameter) y) =>
                SymbolEqualityComparer.Default.Equals(x.Member, y.Member) && SymbolEqualityComparer.Default.Equals(x.Parameter, y.Parameter);

            public int GetHashCode((IMethodSymbol Member, IParameterSymbol Parameter) obj) =>
                (SymbolEqualityComparer.Default.GetHashCode(obj.Member) * 397) ^ SymbolEqualityComparer.Default.GetHashCode(obj.Parameter);
        }
    }
}
