using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Singulink.CodeAnalysis.Analyzers;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Runs the analyzer on test code and checks the diagnostics it reports.
/// </summary>
internal static class Verify
{
    public const string ListFileName = "NonCopyableSymbols.txt";

    private const string ListFileAdvice = "a NonCopyableSymbols.txt file if the member makes no copy, or avoid the use.";

    public static Task AnalyzerAsync([StringSyntax("C#-test")] string source, params DiagnosticResult[] expected)
    {
        var test = new Test { TestCode = source };
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }

    /// <summary>
    /// Runs the analyzer on test code with a user list file.
    /// </summary>
    public static Task AnalyzerAsync([StringSyntax("C#-test")] string source, string listFile, params DiagnosticResult[] expected)
    {
        var test = new Test { TestCode = source };
        test.TestState.AdditionalFiles.Add((ListFileName, listFile));
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }

    /// <summary>
    /// Runs the analyzer on a source of the defensive-copy tests of another analyzer and checks it against what that analyzer expects.
    /// </summary>
    /// <param name="source">The source. <c>{|#n:...|}</c> marks a place where the other analyzer expects a diagnostic, and
    /// <c>{|ours:...|}</c> a place where only this analyzer reports a member that runs on a copy.</param>
    /// <param name="unnecessary">The places where the other analyzer reports a copy that is unnecessary only, for a member that is
    /// readonly in fact. Nothing must be reported there.</param>
    /// <param name="reportedThough">Those of the unnecessary places where this analyzer does report.</param>
    /// <param name="isLibrary">False for a source with top-level statements.</param>
    /// <remarks>
    /// Every other marked place must have a finding, of whatever ID, and <see cref="DiagnosticIds.MemberOnReadOnlyReference"/> must be
    /// reported at the marked places only.
    /// </remarks>
    public static async Task DefensiveCopiesAsync(string source, int[]? unnecessary = null, int[]? reportedThough = null, bool isLibrary = true)
    {
        const string Ours = "ours";

        // Take the markup out and remember where it was.
        var clean = new StringBuilder();
        var places = new List<(string Name, TextSpan Span)>();
        var open = new Stack<(string Name, int Start)>();

        for (int i = 0; i < source.Length; i++)
        {
            if (string.CompareOrdinal(source, i, "{|", 0, 2) == 0)
            {
                int colon = source.IndexOf(':', i);
                open.Push((source[(i + 2)..colon].TrimStart('#'), clean.Length));
                i = colon;
            }
            else if (string.CompareOrdinal(source, i, "|}", 0, 2) == 0 && open.Count > 0)
            {
                var (name, start) = open.Pop();
                places.Add((name, TextSpan.FromBounds(start, clean.Length)));
                i++;
            }
            else
            {
                clean.Append(source[i]);
            }
        }

        var tree = CSharpSyntaxTree.ParseText(clean.ToString(), new CSharpParseOptions(LanguageVersion.Latest));
        var references = await Test.NetReferenceAssemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);

        var compilation = CSharpCompilation.Create(
            "DefensiveCopies",
            [tree],
            references.Add(MetadataReference.CreateFromFile(typeof(NonCopyableAttribute).Assembly.Location)),
            new CSharpCompilationOptions(
                isLibrary ? OutputKind.DynamicallyLinkedLibrary : OutputKind.ConsoleApplication,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error).ToList();
        Assert.IsEmpty(errors, "The source does not compile: " + errors.FirstOrDefault());

        var found = await compilation
            .WithAnalyzers([new DoNotCopyValueAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        // An exception of the analyzer is a diagnostic of the compiler with no place, which the search of the marked places below would
        // not see. It fails the test here, as it fails the tests that list their findings, where it is one finding too many.
        var thrown = found.Where(d => !d.Id.StartsWith("SCA", StringComparison.Ordinal)).ToList();
        Assert.IsEmpty(thrown, "The analyzer threw an exception: " + thrown.FirstOrDefault());

        var text = tree.GetText();
        var failures = new List<string>();

        foreach (var (name, span) in places)
        {
            bool isReported = found.Any(d => d.Location.SourceSpan.IntersectsWith(span));
            bool isUnnecessary = name != Ours && unnecessary?.Contains(int.Parse(name)) is true;
            bool expectsReport = !isUnnecessary || reportedThough?.Contains(int.Parse(name)) is true;

            if (isReported != expectsReport)
                failures.Add($"{(expectsReport ? "Nothing is" : "A finding is")} reported at {Describe(name, span)}");
        }

        foreach (var diagnostic in found.Where(d => d.Id == DiagnosticIds.MemberOnReadOnlyReference))
        {
            if (!places.Any(p => p.Span.IntersectsWith(diagnostic.Location.SourceSpan)))
                failures.Add($"{diagnostic.Id} is reported at a place that is not marked: {Describe("-", diagnostic.Location.SourceSpan)}");
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));

        string Describe(string name, TextSpan span) => $"#{name}, line {text.Lines.GetLineFromPosition(span.Start).LineNumber + 1}: {text.ToString(span)}";
    }

    /// <summary>
    /// The main finding for an assignment that copies a stored value.
    /// </summary>
    /// <param name="location">The number of the marked location.</param>
    /// <param name="type">The non-copyable type.</param>
    /// <param name="usedByHandler">The variable that would be at its last use but for a handler that uses it, if the message says so.</param>
    public static DiagnosticResult CopyByAssignment(int location, string type, string? usedByHandler = null) => Diagnostic(
        DiagnosticIds.CopyByAssignment,
        location,
        $"Assigning a stored value of non-copyable type '{type}' copies it{HandlerHint(usedByHandler)}. Assign a reference, assign a new value, " +
        "or move the stored value out first with NonCopyable.Move.");

    public static DiagnosticResult CopyByAssignmentIntoVariable(int location, string type, string variable) =>
        Diagnostic(DiagnosticIds.CopyByAssignment, location, $"Variable '{variable}' is a copy of a stored value of non-copyable type '{type}'");

    public static DiagnosticResult CopyByAssignmentIntoForEachVariable(int location, string type, string variable) => Diagnostic(
        DiagnosticIds.CopyByAssignment,
        location,
        $"Variable '{variable}' is a copy of a stored value of non-copyable type '{type}': 'foreach' copies each element into its variable. " +
        $"Write 'foreach (ref var {variable} in ...)' over a span of the elements, such as 'CollectionsMarshal.AsSpan(list)' or " +
        "'array.AsSpan()', or take the elements out with a member that removes them.");

    public static DiagnosticResult CopyByAssignmentIntoSecondVariable(int location, string type, string variable) => Diagnostic(
        DiagnosticIds.CopyByAssignment,
        location,
        $"Variable '{variable}' is a copy of a value of non-copyable type '{type}' that the pattern gives to another variable as well. A value " +
        "goes to one variable only.");

    public static DiagnosticResult CopyByAssignmentForDeconstruct(int location, string type, string method) => Diagnostic(
        DiagnosticIds.CopyByAssignment,
        location,
        $"'foreach' copies each element of non-copyable type '{type}' before '{method}' deconstructs it, so the method is given a copy. Make the " +
        "method 'readonly', or declare a 'ref' iteration variable and call the method on it by name.");

    public static DiagnosticResult DeconstructionOfStoredValue(int location, string type, string method) => Diagnostic(
        DiagnosticIds.CopyByAssignment,
        location,
        $"A deconstruction copies the stored value of non-copyable type '{type}' before '{method}' deconstructs it, a local included, so the " +
        "method runs on a copy and what it changes is lost. Make the method 'readonly', deconstruct a new value or what NonCopyable.Move " +
        "takes out of the stored value, or call the method by name.");

    public static DiagnosticResult CopyByAssignmentOutOfBox(int location, string type, string sourceType) => Diagnostic(
        DiagnosticIds.CopyByAssignment, location, $"Converting '{sourceType}' to non-copyable type '{type}' copies the value out of the box");

    public static DiagnosticResult CopyByReturnOfLivingVariable(int location, string type, string variable) => Diagnostic(
        DiagnosticIds.CopyByReturn,
        location,
        $"Returning '{variable}' copies a value of non-copyable type '{type}' that lives on after the return: a variable that a lambda or a local " +
        "function captured, a parameter of a primary constructor, a 'using' variable or a variable of an iterator. Return a moved value instead.");

    public static DiagnosticResult CopyByReturnOfVariableUsedAfter(int location, string type, string variable) => Diagnostic(
        DiagnosticIds.CopyByReturn,
        location,
        $"Returning '{variable}' copies a value of non-copyable type '{type}' that is used after the return: a 'finally' block, or the end " +
        "of a 'using' scope that holds a reference to the variable, runs after the value was read. Return after the 'try' statement, or " +
        "return a moved value.");

    public static DiagnosticResult CopyByReturn(int location, string type) =>
        Diagnostic(DiagnosticIds.CopyByReturn, location, $"Do not return a stored value of non-copyable type '{type}' by value");

    public static DiagnosticResult UnsupportedUse(int location, string type, string operationKind) =>
        Diagnostic(
            DiagnosticIds.UnsupportedUse,
            location,
            $"The analyzer does not know that this use of a value of non-copyable type '{type}' makes no copy (a '{operationKind}' operation). Use " +
            "the value in place or by reference, or move it out first with NonCopyable.Move.");

    public static DiagnosticResult ValuePassedTwice(int location, string type, string where) => Diagnostic(
        DiagnosticIds.UnsupportedUse,
        location,
        $"A value of non-copyable type '{type}' is passed twice here, to {where}, and each gets a copy of its own. Pass it by reference, or " +
        "do the two steps one after the other.");

    public static DiagnosticResult DelegatesCombined(int location, string delegateType, string parameterType) => Diagnostic(
        DiagnosticIds.UnsupportedUse,
        location,
        $"Combining delegates of type '{delegateType}' copies a non-copyable value: every target of a delegate with several targets gets a " +
        $"copy of its own of the argument of type '{parameterType}', which the delegate takes by value. Call the targets one by one, or " +
        "pass the value by reference.");

    public static DiagnosticResult CopyOfStoredValue(int location, string what, string type, string? usedByHandler = null, string? usedAfterFinally = null) => Diagnostic(
        DiagnosticIds.UnsupportedUse,
        location,
        $"{what} a copy of the stored value of non-copyable type '{type}'{HandlerHint(usedByHandler, usedAfterFinally)}. Use the value in place, use a new value, " +
        "or move the stored value out first with NonCopyable.Move.");

    public static DiagnosticResult SwitchStatementMatchesCopy(int location, string type) => Diagnostic(
        DiagnosticIds.UnsupportedUse,
        location,
        $"The pattern reads from a copy of the value of non-copyable type '{type}': a 'switch' statement matches a copy of what it " +
        "switches on, also where that is a local or a parameter. An 'is' pattern and a 'switch' expression without 'when' match it in " +
        "place: use one of those, or move the value into the 'switch' with NonCopyable.Move.");

    public static DiagnosticResult SwitchExpressionWithWhenMatchesCopy(int location, string type) => Diagnostic(
        DiagnosticIds.UnsupportedUse,
        location,
        $"The pattern reads from a copy of the value of non-copyable type '{type}': a 'when' in a 'switch' expression makes every arm " +
        "match a copy of what it switches on, also where that is a local or a parameter. An 'is' pattern and a 'switch' expression " +
        "without 'when' match it in place: use one of those, or move the value into the 'switch' with NonCopyable.Move.");

    public static DiagnosticResult PartUsedTwiceInPattern(int location, string first, string second) => Diagnostic(
        DiagnosticIds.UnsupportedUse,
        location,
        $"{first} and {second} use one value of a non-copyable type twice in this pattern or 'switch': the compiler copies each part " +
        "that a pattern looks into to a temporary, and the value that it is a part of still holds the original. Take the value apart in " +
        "one way only, or in a statement of its own.");

    public static DiagnosticResult MemberOnReadOnlyReference(int location, string type, string member) => Diagnostic(
        DiagnosticIds.MemberOnReadOnlyReference,
        location,
        $"'{member}' is used through a read-only reference to a value of non-copyable type '{type}', so the compiler runs it on a copy and what it " +
        "changes is lost. Make the member 'readonly', or reach the value through a reference that can be written.");

    public static DiagnosticResult MemberOnReadOnlyTypeParameter(int location, string typeParameter, string member) => Diagnostic(
        DiagnosticIds.MemberOnReadOnlyReference,
        location,
        $"'{member}' is used through a read-only reference to a value of type parameter '{typeParameter}'. The compiler cannot tell which " +
        "members of a type parameter are readonly, so it runs every one of them on a copy. Reach the value through a reference that can be " +
        "written, such as a 'ref' parameter.");

    public static DiagnosticResult Boxing(int location, string type, string targetType) =>
        Diagnostic(DiagnosticIds.Boxing, location, $"Converting a stored value of non-copyable type '{type}' to '{targetType}' boxes a copy of it");

    public static DiagnosticResult MemberwiseCloneCall(int location, string type, string fieldType, string field) => Diagnostic(
        DiagnosticIds.MemberwiseCloneCall, location, $"MemberwiseClone of '{type}' copies its field '{field}' of non-copyable type '{fieldType}'");

    public static DiagnosticResult MemberwiseCloneCallOnValue(int location, string type) =>
        Diagnostic(DiagnosticIds.MemberwiseCloneCall, location, $"MemberwiseClone copies a value of non-copyable type '{type}'");

    public static DiagnosticResult FieldOfCopyableType(int location, string type, string field) =>
        Diagnostic(DiagnosticIds.FieldOfCopyableType, location, $"Copyable field '{field}' cannot have non-copyable type '{type}'");

    public static DiagnosticResult FieldOfRecord(int location, string type, string field) =>
        Diagnostic(DiagnosticIds.FieldOfCopyableType, location, $"Field '{field}' of a record cannot have non-copyable type '{type}'");

    public static DiagnosticResult FieldByPrimaryConstructor(int location, string type, string parameter, string containingType) => Diagnostic(
        DiagnosticIds.FieldOfCopyableType,
        location,
        $"Parameter '{parameter}' of the primary constructor of copyable struct '{containingType}' cannot have non-copyable type '{type}': members " +
        "of the struct use it, which makes it a field");

    public static DiagnosticResult MovedFromOnPrimaryConstructor(int location, string parameter, string type) => Diagnostic(
        DiagnosticIds.MovedFromMisuse,
        location,
        $"[MovedFrom] cannot be applied to parameter '{parameter}' of the primary constructor of '{type}': the code of a primary " +
        "constructor is the initializers of the type and the call of the base constructor, which have no order in which the parameter " +
        "could be read and then assigned. Declare an ordinary constructor.");

    public static DiagnosticResult AutoProperty(int location, string type, string property) =>
        Diagnostic(DiagnosticIds.AutoProperty, location, $"Auto-property '{property}' cannot have non-copyable type '{type}'");

    public static DiagnosticResult AnonymousTypeMember(int location, string type, string member) => Diagnostic(
        DiagnosticIds.AutoProperty,
        location,
        $"Member '{member}' of an anonymous type cannot have non-copyable type '{type}': the members of an anonymous type are auto-properties, " +
        "which its ToString, Equals and GetHashCode copy, and 'with' makes a second object that holds the same value. Use a tuple, or a " +
        "class with a field.");

    public static DiagnosticResult PositionalParameterReadByDeconstruct(int location, string type, string parameter, string record) => Diagnostic(
        DiagnosticIds.AutoProperty,
        location,
        $"Positional parameter '{parameter}' of record '{record}' cannot have non-copyable type '{type}': the Deconstruct method that the " +
        "compiler writes for the record reads the member by value, also where the record declares it as a property that returns a " +
        "reference. Declare the Deconstruct method yourself, or give the type an ordinary constructor.");

    public static DiagnosticResult IteratorParameter(int location, string type, string parameter, string iterator) => Diagnostic(
        DiagnosticIds.IteratorParameter,
        location,
        $"Iterator '{iterator}' keeps parameter '{parameter}' of non-copyable type '{type}' in the object that it returns and copies it into " +
        "every enumerator. Create the value inside the iterator, or pass an object that holds it.");

    public static DiagnosticResult DefaultInterfaceMember(int location, string type, string member) => Diagnostic(
        DiagnosticIds.DefaultInterfaceMember,
        location,
        $"Struct '{type}' of a non-copyable type leaves '{member}' to the default implementation of the interface, which runs on a boxed copy " +
        "of the value. Implement the member in the struct.");

    public static DiagnosticResult DefaultInterfaceMemberBound(
        int location, string type, string member, string typeParameter, string definition, string constraint) => Diagnostic(
        DiagnosticIds.DefaultInterfaceMember,
        location,
        $"Struct '{type}' of a non-copyable type is bound to type parameter '{typeParameter}' of '{definition}', which is constrained to " +
        $"'{constraint}', and leaves '{member}' to the default implementation of the interface, which runs on a boxed copy of the value. " +
        "Implement the member in the struct.");

    public static DiagnosticResult AsyncOrIteratorMember(int location, string member, string type, string kind) => Diagnostic(
        DiagnosticIds.AsyncOrIteratorMember,
        location,
        $"'{member}' is an {kind} member of non-copyable struct '{type}', so it runs on a copy of 'this' that the compiler keeps in its state " +
        "machine: what it changes is lost, and it runs later, on a copy that is out of date by then, also where the member is 'readonly'. Do " +
        "the work in a member that is neither async nor an iterator, or in a static method that is given what it needs.");

    public static DiagnosticResult UnlistedMember(int location, string member, string type, string typeParameter, string id) => Diagnostic(
        DiagnosticIds.UnlistedMember,
        location,
        $"'{member}' may copy non-copyable type '{type}': type parameter '{typeParameter}' is not marked [NonCopyable] and the member is not on the safe " +
        $"list. Mark the type parameter, add '{id}' to {ListFileAdvice}");

    public static DiagnosticResult RequiresCopyableMember(int location, string member, string type, string typeParameter) => Diagnostic(
        DiagnosticIds.RequiresCopyable,
        location,
        $"'{member}' requires a copyable type and cannot be used with non-copyable type '{type}' as '{typeParameter}'. Use a member that does not copy.");

    public static DiagnosticResult UntypedCollectionConversion(int location, string container, string targetType, string? through = null) => Diagnostic(
        DiagnosticIds.UntypedCollection,
        location,
        $"Container {Container(container, through)} is converted to untyped collection '{targetType}', which hands out copies of its elements. " +
        "Keep the element type visible, for example with a generic interface, or pass the container only to a member on the safe list.");

    public static DiagnosticResult UntypedCollectionVariance(int location, string container, string hidingType, string sourceType, string targetType) =>
        Diagnostic(
            DiagnosticIds.UntypedCollection,
            location,
            $"The conversion of '{sourceType}' to '{targetType}' turns container '{container}' into untyped collection '{hidingType}' for the code " +
            "behind the reference, which hands out copies of its elements. Variance converts a type argument with no conversion written. Keep " +
            "the type arguments the same.");

    public static DiagnosticResult UntypedCollectionMember(int location, string container, string declaringType, string member, string id) => Diagnostic(
        DiagnosticIds.UntypedCollection,
        location,
        $"'{member}' uses container '{container}' as untyped collection '{declaringType}', which hands out copies of its elements, and is not on the " +
        $"safe list. Add '{id}' to {ListFileAdvice}");

    public static DiagnosticResult UntypedCollectionReturn(int location, string container, string targetType, string member, string overridden) =>
        Diagnostic(
            DiagnosticIds.UntypedCollection,
            location,
            $"'{member}' returns container '{container}' where '{overridden}', which it overrides, returns untyped collection '{targetType}': a " +
            "caller of the base member gets a collection that hands out copies of the elements. Return the type of the base member, or keep the " +
            "element type visible in both.");

    public static DiagnosticResult UntypedCollectionMethodGroup(int location, string container, string targetType, string method, string delegateType) =>
        Diagnostic(
            DiagnosticIds.UntypedCollection,
            location,
            $"The conversion of '{method}' to delegate '{delegateType}' turns container '{container}' into untyped collection '{targetType}', " +
            "which hands out copies of its elements. Use a delegate type that keeps the element type visible.");

    public static DiagnosticResult UntypedCollectionConstraint(int location, string member, string container, string typeParameter, string constraint) =>
        Diagnostic(
            DiagnosticIds.UntypedCollection,
            location,
            $"'{container}' is bound to type parameter '{typeParameter}' of '{member}', which is constrained to untyped collection '{constraint}': " +
            "the generic can use it as that type, which hands out copies of its elements. Use a type parameter that is constrained to a type " +
            "which keeps the element type visible.");

    public static DiagnosticResult OpaqueConversion(int location, string container, string targetType, string? through = null) => Diagnostic(
        DiagnosticIds.OpaqueType,
        location,
        $"Container {Container(container, through)} is converted to '{targetType}', which hides its non-copyable elements from the analyzer. " +
        "Keep the element type visible, or pass the container only to a member on the safe list.");

    public static DiagnosticResult OpaqueVariance(int location, string container, string hidingType, string sourceType, string targetType) => Diagnostic(
        DiagnosticIds.OpaqueType,
        location,
        $"The conversion of '{sourceType}' to '{targetType}' turns container '{container}' into '{hidingType}' for the code behind the reference, " +
        "which hides its non-copyable elements from the analyzer. Variance converts a type argument with no conversion written. Keep the type " +
        "arguments the same.");

    public static DiagnosticResult OpaqueMember(int location, string container, string declaringType, string member, string id) => Diagnostic(
        DiagnosticIds.OpaqueType,
        location,
        $"'{member}' uses container '{container}' as '{declaringType}', which hides its non-copyable elements from the analyzer, and is not on the " +
        $"safe list. Add '{id}' to {ListFileAdvice}");

    public static DiagnosticResult OpaqueReturn(int location, string container, string targetType, string member, string overridden) => Diagnostic(
        DiagnosticIds.OpaqueType,
        location,
        $"'{member}' returns container '{container}' where '{overridden}', which it overrides, returns '{targetType}': a caller of the base member " +
        "gets the container as a type that hides its non-copyable elements from the analyzer. Return the type of the base member, or keep the " +
        "element type visible in both.");

    public static DiagnosticResult OpaqueMethodGroup(int location, string container, string targetType, string method, string delegateType) => Diagnostic(
        DiagnosticIds.OpaqueType,
        location,
        $"The conversion of '{method}' to delegate '{delegateType}' turns container '{container}' into '{targetType}', which hides its " +
        "non-copyable elements from the analyzer. Use a delegate type that keeps the element type visible.");

    public static DiagnosticResult OpaqueTypeParameterConversion(int location, string typeParameter, string targetType) => Diagnostic(
        DiagnosticIds.OpaqueType,
        location,
        $"A value of type parameter '{typeParameter}' is converted to '{targetType}'. With a container bound to '{typeParameter}' that hands " +
        "out the container as a type that hides its non-copyable elements from the analyzer, and nothing in the generic can tell. Keep the " +
        $"value as '{typeParameter}'.");

    public static DiagnosticResult OpaqueConstraint(int location, string member, string container, string typeParameter, string constraint) => Diagnostic(
        DiagnosticIds.OpaqueType,
        location,
        $"'{container}' is bound to type parameter '{typeParameter}' of '{member}', which is constrained to '{constraint}': the generic can use " +
        "it as that type, which hides the non-copyable type that it holds from the analyzer. Use a type parameter that is constrained to a type " +
        "which keeps that type visible.");

    public static DiagnosticResult OpaqueBoxing(int location, string type, string targetType, string definition) => Diagnostic(
        DiagnosticIds.OpaqueType,
        location,
        $"Boxing '{type}' as '{targetType}' hides the non-copyable type that it holds. The code of '{definition}' is not checked, and what its " +
        $"interfaces give on the box are boxed copies. Keep the value as '{type}'.");

    public static DiagnosticResult OpaqueLambdaBoxing(int location, string type, string targetType, string definition) => Diagnostic(
        DiagnosticIds.OpaqueType,
        location,
        $"The result of this lambda, of type '{type}', is boxed as '{targetType}', which hides the non-copyable type that it holds. The code " +
        $"of '{definition}' is not checked, and what its interfaces give on the box are boxed copies. If the result is not meant to be " +
        $"returned, give the lambda a statement body, as in '() => {{ Call(); }}'. Otherwise keep the value as '{type}'.");

    public static DiagnosticResult OpaqueTypeArgument(int location, string member, string container, string typeParameter, string id) => Diagnostic(
        DiagnosticIds.OpaqueType,
        location,
        $"Container '{container}' is bound to type parameter '{typeParameter}' of '{member}', which is not marked [NonCopyable], and the member is not " +
        $"on the safe list. Mark the type parameter, add '{id}' to {ListFileAdvice}");

    /// <summary>
    /// The finding for a by-value read from a stored container. <paramref name="source"/> is the member or the construct that reads, in quotes.
    /// </summary>
    /// <param name="location">The number of the marked location.</param>
    /// <param name="source">The member or the construct that reads, in quotes.</param>
    /// <param name="type">The non-copyable type.</param>
    /// <param name="because">What the message says of a local that is not the method's own: its name in quotes and why.</param>
    /// <param name="canBeMoved">False if the container is what a property or a call hands out by value, which nothing can be moved
    /// out of.</param>
    public static DiagnosticResult ReadFromStoredContainer(int location, string source, string type, string? because = null, bool canBeMoved = true) =>
        Diagnostic(
            DiagnosticIds.ReadFromStoredContainer,
            location,
            $"{source} hands out a copy of non-copyable type '{type}' because it reads from a stored value{Because(because)}. Read from a new " +
            $"value, take the value out with a member that removes it{(canBeMoved ? ", use a reference, or move the stored value out first with NonCopyable.Move" : ", or use a reference")}.");

    /// <summary>
    /// The finding for a 'foreach' over a stored <c>List&lt;T&gt;</c>, which says what to write instead.
    /// </summary>
    /// <param name="location">The number of the marked location.</param>
    /// <param name="type">The non-copyable type.</param>
    /// <param name="variable">The name of the loop's variable.</param>
    /// <param name="list">The list as the loop names it, or "list" where the loop has an expression there.</param>
    public static DiagnosticResult ForEachOverStoredList(int location, string type, string variable, string list) =>
        Diagnostic(
            DiagnosticIds.ReadFromStoredContainer,
            location,
            $"'foreach' hands out a copy of non-copyable type '{type}' because it reads from a stored value. Read from a new value, take the value " +
            $"out with a member that removes it, or loop over references to the elements with 'foreach (ref var {variable} in CollectionsMarshal.AsSpan({list}))'.");

    /// <summary>
    /// The finding for a value that is created out of a stored container.
    /// </summary>
    /// <param name="location">The number of the marked location.</param>
    /// <param name="type">The non-copyable type.</param>
    /// <param name="because">What the message says of a local that is not the method's own: its name in quotes and why.</param>
    /// <param name="handsOut">The property or the method that hands the container out by value, if the message names one: nothing can
    /// be moved out of what it hands out.</param>
    public static DiagnosticResult CreatedFromStoredContainer(int location, string type, string? because = null, string? handsOut = null) => Diagnostic(
        DiagnosticIds.ReadFromStoredContainer,
        location,
        $"This creates a value of non-copyable type '{type}' out of a stored value{Because(because)}, which still holds what the new value " +
        "reads. Create it from a new value" + (handsOut is null
            ? ", or move the stored value out first with NonCopyable.Move."
            : $". What '{handsOut}' hands out stays with what holds it and cannot be moved out of: make the value in this method and hand it " +
                "to the object, as a completion source is made in the method that returns its 'Task'."));

    public static DiagnosticResult OpaqueLambdaResult(int location, string container, string targetType, string? through = null) => Diagnostic(
        DiagnosticIds.OpaqueType,
        location,
        $"The result of this lambda, a container of type {Container(container, through)}, is converted to '{targetType}', which hides its " +
        "non-copyable elements from the analyzer. If the result is not meant to be returned, give the lambda a statement body, as in " +
        "'() => { Call(); }'. Otherwise keep the element type visible.");

    /// <summary>
    /// The finding for a container that a method returns while something else still holds it.
    /// </summary>
    /// <param name="location">The number of the marked location.</param>
    /// <param name="method">The method.</param>
    /// <param name="type">The type of the container.</param>
    /// <param name="because">What the message says of a local that is not the method's own: its name in quotes and why.</param>
    /// <param name="createdHere">False if the local was given something that the method did not create.</param>
    /// <param name="through">The base class or the interface that makes the type a container, if the message names one.</param>
    /// <param name="fromObjectMadeHere">True if the container is read out of an object that the method made, for which the message
    /// does not name [return: Borrowed].</param>
    public static DiagnosticResult UnownedContainerReturned(
        int location, string method, string type, string? because = null, bool createdHere = true, string? through = null, bool fromObjectMadeHere = false) =>
        Diagnostic(
            DiagnosticIds.UnownedContainerReturned,
            location,
            $"'{method}' returns a container of type {Container(type, through)} that something else still holds{Because(because)}, and callers " +
            $"take the result of a call for a new value. {(because is not null && createdHere ? "Hand the container to nothing that keeps it before it is returned" : "Return a container that the method created")}" +
            (fromObjectMadeHere
                ? ": this one comes out of an object that the method made, so make the container in the method and hand it to the object, as a " +
                    "completion source is made in the method that returns its 'Task'."
                : ", or mark the method [return: Borrowed] so that callers treat the result as stored."));

    public static DiagnosticResult DelegateHandsOutStoredContainer(int location, string method, string type) => Diagnostic(
        DiagnosticIds.UnownedContainerReturned,
        location,
        $"The delegate for '{method}' hands out a container of type '{type}' that something else still holds, and callers of a delegate take " +
        "its result for a new value. Use a delegate type whose return value is marked [return: Borrowed], or a lambda that returns a " +
        "container which it created.");

    public static DiagnosticResult HolderReturnedByOverride(int location, string type, string overriddenType, string member, string overridden) => Diagnostic(
        DiagnosticIds.UnownedContainerReturned,
        location,
        $"'{member}' returns '{type}' where '{overridden}', which it overrides, returns '{overriddenType}': a caller of the base member " +
        $"takes the result for a new '{overriddenType}', while an object of '{type}' is never new, since the code of its class decides what " +
        "it holds and who else holds it. Return the type of the base member, or mark the base member [return: Borrowed] so that its " +
        "callers treat the result as stored.");

    public static DiagnosticResult HolderLostByVariance(int location, string sourceType, string targetType, string holder, string container) => Diagnostic(
        DiagnosticIds.UnownedContainerReturned,
        location,
        $"The conversion of '{sourceType}' to '{targetType}' turns '{holder}' into '{container}': what comes out through '{targetType}' is " +
        $"taken for a new '{container}', while an object of '{holder}' is never new, since the code of its class decides what it holds and " +
        $"who else holds it. Keep the type argument '{holder}'.");

    public static DiagnosticResult DelegateOverTypeParameter(int location, string method, string type, string typeParameter) => Diagnostic(
        DiagnosticIds.UnownedContainerReturned,
        location,
        $"The delegate for '{method}' returns '{type}', which mentions the marked type parameter '{typeParameter}'. With a container bound to " +
        $"'{typeParameter}', every call hands out a container that something else may still hold, and callers of a delegate take its result " +
        "for a new value. Use a delegate type whose return value is marked [return: Borrowed].");

    public static DiagnosticResult TypeParameterMarkNotPassedOn(int location, string typeParameter, string baseTypeParameter, string baseType) => Diagnostic(
        DiagnosticIds.MarkNotKept,
        location,
        $"Type parameter '{typeParameter}' is passed to [NonCopyable] type parameter '{baseTypeParameter}' of '{baseType}' and must be marked " +
        "[NonCopyable] as well");

    public static DiagnosticResult TypeParameterMarkNotKept(int location, string typeParameter, string member, string baseTypeParameter, string baseMember) =>
        Diagnostic(
            DiagnosticIds.MarkNotKept,
            location,
            $"Type parameter '{typeParameter}' of '{member}' must be marked [NonCopyable] because type parameter '{baseTypeParameter}' of " +
            $"'{baseMember}', which the member overrides or implements, is");

    public static DiagnosticResult RequiresCopyableNotOnBaseMember(int location, string member, string baseMember) => Diagnostic(
        DiagnosticIds.MarkNotKept,
        location,
        $"'{member}' cannot be marked [RequiresCopyable] because '{baseMember}', which it overrides or implements, is not");

    public static DiagnosticResult RequiresCopyableOnMemberNeverCalled(int location, string member) => Diagnostic(
        DiagnosticIds.MarkNotKept,
        location,
        $"'{member}' cannot be marked [RequiresCopyable]: the runtime runs a static constructor and a finalizer, so there is no use at " +
        "which a copy could be reported. The mark counts for nothing, and the body is checked as it would be without it.");

    public static DiagnosticResult BorrowedNotOnBaseMember(int location, string member, string baseMember) => Diagnostic(
        DiagnosticIds.MarkNotKept,
        location,
        $"'{member}' cannot be marked [return: Borrowed] because '{baseMember}', which it overrides or implements, is not: a caller through " +
        "that member takes the result for a new value");

    public static DiagnosticResult BorrowedNotOnDelegate(int location, string method, string delegateType) => Diagnostic(
        DiagnosticIds.MarkNotKept,
        location,
        $"'{method}' is marked [return: Borrowed] and is converted to delegate '{delegateType}', whose return value is not: a caller of the " +
        "delegate takes the result for a new value. Use a delegate type whose return value is marked [Borrowed] as well.");

    public static DiagnosticResult ParameterUsedInAWayNotFollowed(int location, string method, string parameter, string type, string? through = null) => Diagnostic(
        DiagnosticIds.KeptParameter,
        location,
        $"'{method}' uses its parameter '{parameter}', a container of type {Container(type, through)}, which is not marked [Kept], in a way that " +
        "is not known to leave it with the caller alone, so it counts as kept. A caller takes a container that it passes to be its own " +
        "afterwards. Mark the parameter [Kept], or use it in a way that is known: use a member of it, pass it to a parameter that does not " +
        "keep it, test it or enumerate it.");

    public static DiagnosticResult PassesParameterToUncheckedCode(int location, string method, string parameter, string type, string member, string id) => Diagnostic(
        DiagnosticIds.KeptParameter,
        location,
        $"'{method}' passes its parameter '{parameter}', a container of type '{type}', which is not marked [Kept], to '{member}', which is " +
        "taken to keep it: the type of the parameter that receives it mentions a type parameter that is not marked [NonCopyable], so nothing " +
        "checks what the member does with the container. A caller takes a container that it passes to be its own afterwards. If the member " +
        $"keeps nothing of it, add '{id};borrows' to a NonCopyableSymbols.txt file; if it keeps the container, mark the parameter [Kept].");

    private static string DescribeKeeping(bool byUncheckedCode) => byUncheckedCode
        ? "as a parameter whose type mentions a type parameter that is not marked [NonCopyable] is where the list does not mark the member 'borrows'"
        : "as a parameter whose type is a type parameter is";

    public static DiagnosticResult KeepsUnmarkedParameter(int location, string method, string parameter, string type, string? through = null) => Diagnostic(
        DiagnosticIds.KeptParameter,
        location,
        $"'{method}' keeps its parameter '{parameter}', a container of type {Container(type, through)}, which is not marked [Kept]. A caller " +
        "takes a container that it passes to be its own afterwards. Mark the parameter [Kept], or do not store it.");

    /// <summary>
    /// The finding for a container parameter that an iterator uses, or an 'async' method after an 'await'.
    /// </summary>
    public static DiagnosticResult KeepsParameterAfterReturning(int location, string method, string parameter, string type, bool isIterator = false) => Diagnostic(
        DiagnosticIds.KeptParameter,
        location,
        $"'{method}' {(isIterator ? "is an iterator, which runs after the call that creates it has returned, and uses" : "goes on after an 'await', when it has returned to its caller, and uses")} " +
        $"its parameter '{parameter}', a container of type '{type}', which is not marked [Kept]. A caller takes a container that it passes to " +
        $"be its own afterwards. Mark the parameter [Kept]{(isIterator ? "." : ", or use the container only before the first 'await'.")}");

    public static DiagnosticResult ContainerMovedFromUnowned(int location, string container, string expression, string? because = null, string? through = null) =>
        Diagnostic(
            DiagnosticIds.ContainerNotOwned,
            location,
            $"Container {Container(container, through)} is moved out of '{expression}', which is not a local that the method owns{(because is null ? string.Empty : $", because {because}")}, " +
            "so clearing it does not make this the only reference to the container. Move a local that holds a container which the method " +
            "created or got from a call.");

    public static DiagnosticResult ContainerMovedFromSlotParameter(int location, string container, string parameter, string giver) => Diagnostic(
        DiagnosticIds.ContainerNotOwned,
        location,
        $"Container '{container}' is moved out of [MovedFrom] parameter '{parameter}', which is not the method's own: {giver} gives the " +
        "parameter the type of a type parameter, so generic code passes it a slot of its own, and with a container bound that slot may hold " +
        "a container that something else still holds. Use the container in place.");

    public static DiagnosticResult SlotGivenToOwner(int location, string method, string parameter, string giver) => Diagnostic(
        DiagnosticIds.ContainerNotOwned,
        location,
        $"'{method}' takes its [MovedFrom] parameter '{parameter}' to be its own, and {giver} gives that parameter the type of a type " +
        "parameter: generic code passes it a slot of its own, which with a container bound may hold a container that something else still " +
        "holds. Use a method or a lambda that uses the container in place.");

    public static DiagnosticResult MovedFromAdded(int location, string parameter, string member, string giver) => Diagnostic(
        DiagnosticIds.MarkNotKept,
        location,
        $"Parameter '{parameter}' of '{member}' cannot be marked [MovedFrom] because {giver} does not mark it: a caller through that passes " +
        "anything, and not only what it owns");

    public static DiagnosticResult MovedFromDropped(int location, string parameter, string member, string giver) => Diagnostic(
        DiagnosticIds.MarkNotKept,
        location,
        $"Parameter '{parameter}' of '{member}' must be marked [MovedFrom] because {giver} marks it: a caller through that trusts what the " +
        "method leaves in its variable, and the body is checked for that only with the mark");

    public static DiagnosticResult MovedFromDroppedWithNoParameterList(int location, string giver, string parameter) => Diagnostic(
        DiagnosticIds.MarkNotKept,
        location,
        $"The anonymous method is converted to {giver}, which marks its parameter '{parameter}' [MovedFrom], and has no parameter list in " +
        "which the parameter could be marked: a caller through the delegate trusts what the method leaves in its variable, and the body " +
        "is checked for that only with the mark. Write the parameter list, and mark the parameter [MovedFrom].");

    public static DiagnosticResult KeptNotOnBaseMember(int location, string parameter, string member, string baseMember) => Diagnostic(
        DiagnosticIds.MarkNotKept,
        location,
        $"Parameter '{parameter}' of '{member}' cannot be marked [Kept] because '{baseMember}', which the member overrides or implements, only " +
        "uses what it is given: a caller through that member takes its container to be its own afterwards");

    public static DiagnosticResult RequiresCopyableNotOnBaseCopyConstructor(int location, string constructor, string baseRecord, string derived) => Diagnostic(
        DiagnosticIds.MarkNotKept,
        location,
        $"'{constructor}' cannot be marked [RequiresCopyable] because the copy constructor of its base record '{baseRecord}' is not: 'with' on " +
        $"a variable of the base record runs the copy constructor of '{derived}' with nothing to report it at");

    public static DiagnosticResult KeepingNotOnInterfaceMember(
        int location, string parameter, string member, string interfaceMember, string type, bool byUncheckedCode = false) => Diagnostic(
        DiagnosticIds.MarkNotKept,
        location,
        $"Parameter '{parameter}' of '{member}' is taken to keep what it is given, {DescribeKeeping(byUncheckedCode)}, and " +
        $"'{type}' inherits the member as its implementation of '{interfaceMember}', whose parameter is not marked [Kept]: a caller through " +
        $"the interface takes its container to be its own afterwards. Implement the member in '{type}', or mark the parameter of the " +
        "interface member [Kept].");

    public static DiagnosticResult KeepingNotOnDelegate(int location, string parameter, string member, string delegateType, bool byUncheckedCode = false) => Diagnostic(
        DiagnosticIds.MarkNotKept,
        location,
        $"Parameter '{parameter}' of '{member}' is taken to keep what it is given, {DescribeKeeping(byUncheckedCode)}, and the " +
        $"member is converted to delegate '{delegateType}', whose parameter is not marked [Kept]: a caller of the delegate takes its " +
        "container to be its own afterwards. Use a delegate type whose parameter is marked [Kept].");

    public static DiagnosticResult KeptNotOnDelegate(int location, string parameter, string member, string delegateType) => Diagnostic(
        DiagnosticIds.MarkNotKept,
        location,
        $"Parameter '{parameter}' of '{member}' is marked [Kept], and the member is converted to delegate '{delegateType}', whose parameter is " +
        "not: a caller of the delegate takes its container to be its own afterwards. Use a delegate type whose parameter is marked [Kept] as well.");

    public static DiagnosticResult ImplementationNotChecked(
        int location, string implementation, string interfaceMember, string type, string typeArgument, string typeParameter) => Diagnostic(
            DiagnosticIds.MarkNotKept,
            location,
            $"'{implementation}', which '{type}' inherits and uses to implement '{interfaceMember}', is not checked code for non-copyable type " +
            $"'{typeArgument}': type parameter '{typeParameter}' is not marked [NonCopyable]. Implement the interface member in '{type}', or mark the " +
            "type parameter.");

    public static DiagnosticResult MovedFromNotOnRefParameter(int location) =>
        Diagnostic(DiagnosticIds.MovedFromMisuse, location, "[MovedFrom] can only be applied to a 'ref' parameter");

    public static DiagnosticResult MovedFromParameterNotAssigned(int location, string method, string parameter) => Diagnostic(
        DiagnosticIds.MovedFromMisuse,
        location,
        $"'{method}' can leave with what it read here from [MovedFrom] parameter '{parameter}' still in the parameter. A method that moves a " +
        $"value out must leave a fresh one behind on every path, for example with '{parameter} = default' right after the read.");

    public static DiagnosticResult MovedFromParameterReadAgain(int location, string parameter) => Diagnostic(
        DiagnosticIds.MovedFromMisuse,
        location,
        $"[MovedFrom] parameter '{parameter}' is read again before it is assigned. Only one read can be the move, and every other read is a " +
        "copy. Read it once into a local, and assign the parameter before it is read again.");

    /// <summary>
    /// The finding for a read or a write through a reference that can be a null reference, between the read of a [MovedFrom]
    /// parameter and its assignment. The message says how to check the reference.
    /// </summary>
    public static DiagnosticResult MovedFromParameterNotAssignedAtOnceThroughReference(int location, string parameter, string reference) => Diagnostic(
        DiagnosticIds.MovedFromMisuse,
        location,
        $"Between the read of [MovedFrom] parameter '{parameter}' and its assignment the method reads or writes through '{reference}', which " +
        $"can be a null reference: if that fails, the value is in two places. Check the reference before the read of '{parameter}', with " +
        $"NonCopyable.ThrowIfNullRef(in {reference}) or with a method of the project whose parameter is marked [NotNullRef].");

    public const string NotNullRefByValue =
        "the parameter is passed by value, and the mark is for a reference, which a 'ref', 'in', 'ref readonly' or 'out' parameter is";

    public const string NotNullRefNotOnAMethod =
        "the mark counts only on a method that is not virtual, or on a local function, where a call names the code that runs, and not on a " +
        "virtual or abstract member, an override, a member of an interface, a delegate, a lambda, an operator, a constructor, an accessor " +
        "or the receiver of an extension block";

    public const string NotNullRefCanBeLeftOut =
        "the compiler can leave a call of a method out of a build that is [Conditional] or a partial method with no body, and a check that " +
        "is there in one build and not in the next proves nothing";

    /// <summary>
    /// The finding for [NotNullRef] where it does not count.
    /// </summary>
    public static DiagnosticResult NotNullRefMisplaced(int location, string parameter, string member, string problem) => Diagnostic(
        DiagnosticIds.NotNullRefMisplaced,
        location,
        $"[NotNullRef] on parameter '{parameter}' of '{member}' does not count: {problem}. A call of '{member}' does not make a reference " +
        "known to refer to something.");

    /// <summary>
    /// The finding for [NotNullRef] where it counts, on a parameter that the project declares: nothing checks what it says.
    /// </summary>
    public static DiagnosticResult NestedTooDeeply(int location, string member) => Diagnostic(
        DiagnosticIds.NestedTooDeeply,
        location,
        $"'{member}' is nested too deeply for the analyzer and was not checked: a copy in it is not reported. Break up the expression that " +
        "is nested this deeply, with locals or with methods of its own.");

    public static DiagnosticResult NotNullRefNotChecked(int location, string parameter, string member) => Diagnostic(
        DiagnosticIds.NotNullRefNotChecked,
        location,
        $"The analyzer does not check that '{member}' returns only if the reference that it is given for parameter '{parameter}' refers to " +
        "something, as [NotNullRef] says: what it reports about a [MovedFrom] parameter and about a value that is committed at the end of a " +
        "'try' block rests on that. Verify and test the method, then suppress this finding here.");

    /// <summary>
    /// For the <c>usedByHandler</c> of a finding: the variable, and the reference through which the 'try' block reads or writes after
    /// the value was stored, which is all that keeps the store from being the commit.
    /// </summary>
    public static string ThenThrough(string variable, string reference) => variable + "\u0001" + reference;

    public static DiagnosticResult MovedFromParameterNotAssignedAtOnce(int location, string parameter, string what) => Diagnostic(
        DiagnosticIds.MovedFromMisuse,
        location,
        $"Between the read of [MovedFrom] parameter '{parameter}' and its assignment the method runs {what}. If that throws, or reaches the " +
        $"same variable in another way, the value is in two places. Do it before the read, into a local, and assign '{parameter}' right " +
        "after the read.");

    public static DiagnosticResult MovedFromParameterReassigned(int location, string parameter) => Diagnostic(
        DiagnosticIds.MovedFromMisuse,
        location,
        $"[MovedFrom] parameter '{parameter}' is made to refer to something else. The assignment that follows then resets that, and the " +
        "caller's variable keeps the value that was moved out. Assign the parameter itself.");

    public static DiagnosticResult GenericMoveReturnsSomethingElse(int location, string method, string typeParameter) => Diagnostic(
        DiagnosticIds.MovedFromMisuse,
        location,
        $"'{method}' is a generic move, and returns something other than 'default' or what it read from its [MovedFrom] parameters. With a " +
        $"container bound to '{typeParameter}', its callers take what it returns to be the container that they passed, which nobody else " +
        "holds. Return what was moved out, or mark the return value [return: Borrowed] so that callers treat a container that it hands out " +
        "as stored.");

    public static DiagnosticResult GenericMoveLeavesSomethingElse(int location, string method, string typeParameter, string parameter) => Diagnostic(
        DiagnosticIds.MovedFromMisuse,
        location,
        $"'{method}' is a generic move, and leaves something in [MovedFrom] parameter '{parameter}' other than 'default' or what it read " +
        $"from its [MovedFrom] parameters. With a container bound to '{typeParameter}', its callers take what is left in their variable to be " +
        "their own.");

    public static DiagnosticResult MovedFromContainerNotOwned(int location, string parameter) => Diagnostic(
        DiagnosticIds.MovedFromMisuse,
        location,
        $"[MovedFrom] parameter '{parameter}' is assigned a container that something else still holds. The caller's variable counts as its " +
        "own afterwards, so leave null, a new value or a local that the method owns in it.");

    public static DiagnosticResult MovedFromContainerExposed(int location, string parameter, string how) => Diagnostic(
        DiagnosticIds.MovedFromMisuse,
        location,
        $"[MovedFrom] parameter '{parameter}' {how}, which can leave a container in it that something else still holds. The caller's variable " +
        "counts as its own afterwards, so assign the parameter itself, or pass it on only to a [MovedFrom] parameter of a move.");

    /// <summary>
    /// The finding for a line of the list file that cannot be parsed. The line and columns are one-based, as in an editor.
    /// </summary>
    public static DiagnosticResult SafeListLine(int line, int startColumn, int endColumn, string reason) =>
        new DiagnosticResult(DiagnosticIds.SafeListLine, DiagnosticSeverity.Warning)
            .WithSpan(ListFileName, line, startColumn, line, endColumn)
            .WithMessage($"This line of the safe list cannot be parsed: {reason}");

    /// <summary>
    /// The finding for a 'noncopyable' line of the list file that names no type of the compilation.
    /// </summary>
    public static DiagnosticResult SafeListNonCopyableNamesNothing(int line, int startColumn, int endColumn, string id) =>
        new DiagnosticResult(DiagnosticIds.SafeListLine, DiagnosticSeverity.Warning)
            .WithSpan(ListFileName, line, startColumn, line, endColumn)
            .WithMessage(
                $"This line of the safe list marks '{id}' as non-copyable, and the compilation has no type of that ID: the type stays copyable. " +
                "Fix the ID, or take the line out of the file of a project that does not reference the type.");

    /// <summary>
    /// The finding for a 'copyable' line of the list file that has no effect.
    /// </summary>
    public static DiagnosticResult SafeListCopyableHasNoEffect(int line, int startColumn, int endColumn, string id, string because) =>
        new DiagnosticResult(DiagnosticIds.SafeListLine, DiagnosticSeverity.Warning)
            .WithSpan(ListFileName, line, startColumn, line, endColumn)
            .WithMessage($"This line of the safe list marks '{id}' as copyable. It has no effect, because {because}.");

    public static DiagnosticResult NonCopyableAwaiter(int location, string type) => Diagnostic(
        DiagnosticIds.NonCopyableAwaiter,
        location,
        $"The awaiter of this 'await' is of non-copyable type '{type}'. When the 'await' suspends, the compiler copies the awaiter into its " +
        "state machine, calls OnCompleted on the original and GetResult on the copy. Make the awaiter a class or a copyable struct that " +
        "refers to the state.");

    public static DiagnosticResult ConditionalMarker(int location, string attribute) => Diagnostic(
        DiagnosticIds.ConditionalMarker,
        location,
        $"Attribute '{attribute}' is declared [Conditional]. The compiler leaves a conditional attribute out of the assembly unless its symbol " +
        "is defined, so the marks would hold inside this project and be gone for every project that uses it. Remove [Conditional] from the " +
        "attribute.");

    public static DiagnosticResult ConditionalMarkerBase(int location, string attribute, string baseClass) => Diagnostic(
        DiagnosticIds.ConditionalMarker,
        location,
        $"Attribute '{attribute}' derives from '{baseClass}', which is declared [Conditional]. A class that derives from a conditional " +
        "attribute is conditional too: the compiler leaves its uses out of the assembly unless the symbol is defined, so the marks would " +
        "hold inside this project and be gone for every project that uses it. Derive the attribute from a class that is not conditional.");

    public static DiagnosticResult ConditionalMarkerUse(int location, string attribute, string assembly, string symbol) => Diagnostic(
        DiagnosticIds.ConditionalMarker,
        location,
        $"Attribute '{attribute}' is declared [Conditional] in '{assembly}', and '{symbol}' is not defined here: the compiler leaves this use " +
        "out of the assembly, so the mark holds inside this project and is gone for every project that uses it. Use an attribute that is not " +
        "conditional: the one of the Singulink.CodeAnalysis package, or one that the project declares in the namespace Singulink.CodeAnalysis.");

    public static DiagnosticResult ForeignNonCopyableAttribute(int location, string attribute) => Diagnostic(
        DiagnosticIds.ForeignNonCopyableAttribute,
        location,
        $"'{attribute}' is not the attribute that the analyzer reads: only 'Singulink.CodeAnalysis.NonCopyableAttribute' is recognised, so a " +
        "struct that is marked with this class is copied with no finding. Declare the class in the namespace 'Singulink.CodeAnalysis', or " +
        "suppress this finding if the attribute is meant for something else.");

    public static DiagnosticResult NonCopyableAttributeWithNoSuffix(int location, string attribute) => Diagnostic(
        DiagnosticIds.ForeignNonCopyableAttribute,
        location,
        $"'{attribute}' is not the attribute that the analyzer reads: only 'Singulink.CodeAnalysis.NonCopyableAttribute' is recognised, by " +
        "that name, so a struct that is marked with this class is copied with no finding. Name the class 'NonCopyableAttribute' and declare " +
        "it in the namespace 'Singulink.CodeAnalysis', or suppress this finding if the attribute is meant for something else.");

    public static DiagnosticResult ForeignNonCopyableAttributeUse(int location, string attribute, string assembly, string effect) => Diagnostic(
        DiagnosticIds.ForeignNonCopyableAttribute,
        location,
        $"This mark is not recognised: '{attribute}' of '{assembly}' is not the attribute that the analyzer reads, so {effect}. Use " +
        "'Singulink.CodeAnalysis.NonCopyableAttribute', from the Singulink.CodeAnalysis package or declared by the project in the namespace " +
        "'Singulink.CodeAnalysis', or suppress this finding if the attribute is meant for something else.");

    public static DiagnosticResult ErasableBaseNotKept(int location, string type, string erasable, string reason) => Diagnostic(
        DiagnosticIds.ErasableBase,
        location,
        $"'{type}' derives from or implements erasable type '{erasable}' and does not stay erasable: {reason}. An object of it that is " +
        $"converted as '{erasable}' can be converted on to a type that hands out its non-copyable values untyped.");

    public static DiagnosticResult CopyOfStoredValueInFilter(int location, string what, string type) => Diagnostic(
        DiagnosticIds.UnsupportedUse,
        location,
        $"{what} a copy of the stored value of non-copyable type '{type}': a read in an exception filter is never the last use of a " +
        "variable, because a 'finally' inside the 'try' runs after the filter and the filter can run again. Use the value in place, use a " +
        "new value, or move the stored value out first with NonCopyable.Move.");

    private static string HandlerHint(string? variable, string? afterFinally = null) => (variable, afterFinally) switch {
        (null, null) => string.Empty,
        (not null, _) when variable.Split('\u0001') is [var stored, var reference] =>
            $": '{stored}' is not used again after this, but a 'catch', a 'finally' or an exception filter that uses it can still run, because " +
            $"the 'try' block then reads or writes through '{reference}', which can be a null reference. Check the reference first, with " +
            $"NonCopyable.ThrowIfNullRef(in {reference}) or with a method of the project whose parameter is marked [NotNullRef]",
        (not null, _) => $": '{variable}' is not used again after this, but a 'catch', a 'finally' or an exception filter that uses it can still run",
        _ => $": '{afterFinally}' is not used again after this, but it is used after a 'finally' that can still run, written or added by the " +
            "compiler for a 'foreach', a 'using' or a 'lock', and what follows a 'finally' counts as following an exception as well",
    };

    private static string Container(string type, string? through) => through is null ? $"'{type}'" : $"'{type}' (a container through '{through}')";

    private static string Because(string? because) => because is null ? string.Empty : $": {because}";

    private static DiagnosticResult Diagnostic(string id, int location, string message) =>
        new DiagnosticResult(id, DiagnosticSeverity.Warning).WithLocation(location).WithMessage(message);

    /// <summary>
    /// A test of the analyzer on .NET 10, with the latest C# version and a reference to the attributes package.
    /// </summary>
    public sealed class Test : CSharpAnalyzerTest<DoNotCopyValueAnalyzer, DefaultVerifier>
    {
        // The reference assemblies of .NET 10, which the testing library does not name. Every entry of the built-in list exists there.
        public static readonly ReferenceAssemblies NetReferenceAssemblies = new(
            "net10.0", new PackageIdentity("Microsoft.NETCore.App.Ref", "10.0.0"), Path.Combine("ref", "net10.0"));

#if NET11_0_OR_GREATER
        /// <summary>
        /// The reference assemblies of .NET 11, for a test of what the language binds to types of that runtime, as a union does.
        /// </summary>
        public static readonly ReferenceAssemblies Net11ReferenceAssemblies = new(
            "net11.0", new PackageIdentity("Microsoft.NETCore.App.Ref", "11.0.0"), Path.Combine("ref", "net11.0"));
#endif

        public Test()
        {
            ReferenceAssemblies = NetReferenceAssemblies;

            // The attributes assembly is the one for .NET 10, which the compiler remarks on when warnings are checked.
            DisabledDiagnostics.Add("CS1701");
            TestState.AdditionalReferences.Add(typeof(NonCopyableAttribute).Assembly);
        }

        public LanguageVersion LanguageVersion { get; init; } = LanguageVersion.Latest;

        // The properties of the project file as the analyzer sees them. A test project references the attributes package itself, so the
        // targets file of the package turns the built-in non-copyable types on, as it does for every project that references a package.
        private readonly Dictionary<string, string> _buildProperties = new(StringComparer.Ordinal) { ["NonCopyableIncludeBuiltInTypes"] = "true" };

        /// <summary>
        /// Sets a property of the project file as the analyzer sees it: what the build files of the package and the compiler make of
        /// an element such as <c>&lt;NonCopyableAllowImplicitMoves&gt;</c>. Null leaves the property out, as in a build in which the
        /// targets file of the package did not run.
        /// </summary>
        public Test WithBuildProperty(string name, string? value)
        {
            if (value is null)
                _buildProperties.Remove(name);
            else
                _buildProperties[name] = value;

            return this;
        }

        /// <summary>
        /// Gets the symbols that the project defines for conditional compilation.
        /// </summary>
        public string[] PreprocessorSymbols { get; init; } = [];

        /// <summary>
        /// Gets a value indicating whether the project is compiled with overflow checks, as <c>&lt;CheckForOverflowUnderflow&gt;</c>
        /// in the project file makes it.
        /// </summary>
        public bool ChecksOverflow { get; init; }

        protected override Task RunImplAsync(CancellationToken cancellationToken)
        {
            if (_buildProperties.Count > 0)
            {
                TestState.AnalyzerConfigFiles.Add((
                    "/.globalconfig",
                    "is_global = true\n" + string.Concat(_buildProperties.Select(pair => $"build_property.{pair.Key} = {pair.Value}\n"))));
            }

            return base.RunImplAsync(cancellationToken);
        }

        protected override CompilationOptions CreateCompilationOptions() =>
            ((CSharpCompilationOptions)base.CreateCompilationOptions()).WithOverflowChecks(ChecksOverflow);

        protected override ParseOptions CreateParseOptions() =>
            ((CSharpParseOptions)base.CreateParseOptions()).WithLanguageVersion(LanguageVersion).WithPreprocessorSymbols(PreprocessorSymbols);
    }
}
