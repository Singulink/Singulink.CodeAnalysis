using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using PrefixClassName.MsTest;
using Singulink.CodeAnalysis.Analyzers;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Holds the analyzer's list of what a container parameter may be used in against the compiler's API: every member that an
/// operation on the list carries, and that the semantic information for it gives, is one that the analyzer reads or that the
/// table here names as taking no container.
/// </summary>
/// <remarks>
/// <para>Docs: articles/concepts/keeping-containers.md.</para>
/// <para>
/// A use of a container parameter is allowed by its kind of operation, and a kind can run code of the project with the container
/// where nothing but what the compiler bound shows it. The passes before this test each found members that nobody had thought
/// of. So the question is turned round: the properties are found by reflection, and one that is in neither list fails the test.
/// A member that a later compiler adds to one of these operations is found here when the compiler package is raised.
/// </para>
/// <para>
/// What this cannot see: a member that the compiler binds and gives through no property, which the analyzer finds by a lookup
/// ('operator true' and 'operator false', 'OnCompleted', the 'foreach' behind a spread) or cannot find (the 'Add' of a collection
/// expression, which is why an element of one is kept by its kind); a member behind an operation inside the operation, as the
/// member of a property pattern is; a property that this version of the compiler keeps internal; and whether the code reads each
/// property that the list names and judges it rightly, which the tests of the behavior hold.
/// </para>
/// </remarks>
[PrefixTestClass]
public sealed class CarriedSymbolTests
{
    // The properties that give something which takes no container, each with the reason.
    private static readonly (Type Carrier, string Property, string Reason)[] s_takesNoContainer = [
        (typeof(IArgumentOperation), nameof(IArgumentOperation.InConversion), "Visual Basic only: C# converts no argument on the way in"),
        (typeof(IArgumentOperation), nameof(IArgumentOperation.OutConversion), "Visual Basic only: C# converts no argument on the way out"),
        (typeof(ICollectionExpressionOperation), nameof(ICollectionExpressionOperation.ConstructMethod), "null for the array or the span of a 'params' parameter, the one collection that passes a container on: every other one keeps its elements by its kind"),
        (typeof(ICompoundAssignmentOperation), nameof(ICompoundAssignmentOperation.OutConversion), "converts what the operator returns, not what it is given"),
        (typeof(IConversionOperation), nameof(IConversionOperation.Conversion), "its method is the one that OperatorMethod gives"),
        (typeof(IEventReferenceOperation), nameof(IEventReferenceOperation.Event), "an event of the container's own type: an extension declares none"),
        (typeof(IEventReferenceOperation), nameof(IEventReferenceOperation.Member), "the same event"),
        (typeof(IFieldReferenceOperation), nameof(IFieldReferenceOperation.Field), "a field runs no code"),
        (typeof(IFieldReferenceOperation), nameof(IFieldReferenceOperation.Member), "the same field"),
        (typeof(IWithOperation), nameof(IWithOperation.CloneMethod), "the copy constructor of the record itself, a member of the container's own type"),
        (typeof(IRecursivePatternOperation), nameof(IRecursivePatternOperation.DeclaredSymbol), "the variable that the pattern declares, which is followed as a variable that holds the container"),
        (typeof(IListPatternOperation), nameof(IListPatternOperation.DeclaredSymbol), "the variable that the pattern declares, which is followed as a variable that holds the container"),
        (typeof(IDeclarationPatternOperation), nameof(IDeclarationPatternOperation.DeclaredSymbol), "the variable that the pattern declares, which is followed as a variable that holds the container"),
        (typeof(AwaitExpressionInfo), nameof(AwaitExpressionInfo.RuntimeAwaitMethod), "the runtime's own helper for an 'await', which awaits the task as the members of the awaiter do"),
#if NET11_0_OR_GREATER

        // What the compiler of the .NET 11 SDK has more. The analyzer is built against the oldest compiler that it supports, which
        // does not have these two, so it cannot read them. They give the members that await what MoveNextAsync and DisposeAsync
        // of an 'await foreach' return: that is the enumerator's answer, a 'ValueTask' for every enumerator of the framework and
        // of an 'async' iterator, and not the container.
        (typeof(ForEachStatementInfo), "MoveNextAwaitableInfo", "awaits what MoveNextAsync returns, which is not the container: a ValueTask<bool> but for an enumerator with an awaitable type of its own"),
        (typeof(ForEachStatementInfo), "DisposeAwaitableInfo", "awaits what DisposeAsync returns, which is not the container: a ValueTask but for an enumerator with an awaitable type of its own"),
#endif
    ];

    // The results of the model's 'Get...Info' methods that say nothing of one construct, or of one whose members are in the tree.
    private static readonly (Type Information, string Reason)[] s_otherInformation = [
        (typeof(SymbolInfo), "what a name or an expression binds to, for any syntax"),
        (typeof(Microsoft.CodeAnalysis.TypeInfo), "the type of an expression"),
        (typeof(QueryClauseInfo), "a query clause is a call of its method, which is an invocation in the operation tree"),
        (typeof(IAliasSymbol), "what an alias of a 'using' directive stands for"),
    ];

    [TestMethod]
    public void EveryMemberThatAnOperationOnTheListCarries_IsReadOrTakesNoContainer()
    {
        var read = GetRead().ToHashSet();
        var excused = s_takesNoContainer.Select(entry => (entry.Carrier, entry.Property)).ToHashSet();
        var carriers = GetKinds().Select(kind => kind.Operation).Concat(GetInformation()).Distinct().ToList();
        var found = new HashSet<(Type, string)>();
        var failures = new List<string>();

        foreach (var carrier in carriers)
        {
            foreach (var property in GetProperties(carrier).Where(property => CanTakeAContainer(property.PropertyType)))
            {
                found.Add((carrier, property.Name));
                bool isRead = read.Contains((carrier, property.Name));
                bool isExcused = excused.Contains((carrier, property.Name));

                if (!isRead && !isExcused)
                    failures.Add($"{carrier.Name}.{property.Name} ({property.PropertyType.Name}) is neither read by the analyzer nor named here as taking no container.");
                else if (isRead && isExcused)
                    failures.Add($"{carrier.Name}.{property.Name} is named in both lists.");
            }
        }

        // An entry that names nothing which the compiler has says nothing.
        foreach (var (carrier, property) in read.Concat(excused))
        {
            if (!found.Contains((carrier, property)))
                failures.Add($"{carrier.Name}.{property} is in a list, and is no property of an operation on the list that gives a member.");
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    public void EveryKindOnTheList_IsAKindOfOperationWithItsInterface()
    {
        var failures = new List<string>();

        foreach (var (kind, operation) in GetKinds())
        {
            if (!operation.IsInterface || !typeof(IOperation).IsAssignableFrom(operation))
                failures.Add($"{operation.Name}, for {kind}, is no interface of an operation.");

            if (!Enum.IsDefined(kind) || kind is OperationKind.None or OperationKind.Invalid)
                failures.Add($"{kind} is no kind of operation that can be on the list.");
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    // A construct for which a later compiler gives its members through a new 'Get...Info' method of the model is found here.
    [TestMethod]
    public void EveryInformationThatTheModelGives_IsReadOrIsOfAnotherKind()
    {
        var known = GetInformation().Concat(s_otherInformation.Select(entry => entry.Information)).ToHashSet();

        var given = typeof(Microsoft.CodeAnalysis.CSharp.CSharpExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name.StartsWith("Get", StringComparison.Ordinal) &&
                method.Name.EndsWith("Info", StringComparison.Ordinal) &&
                method.GetParameters() is [{ ParameterType: var first }, ..] &&
                first == typeof(SemanticModel))
            .Select(method => (method.Name, method.ReturnType))
            .Distinct()
            .ToList();

        Assert.IsNotEmpty(given);

        var failures = given
            .Where(method => !known.Contains(method.ReturnType))
            .Select(method => $"{method.Name} gives {method.ReturnType.Name}, which the analyzer does not read and which is not named here as of another kind.")
            .ToList();

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    // A method, a property, an event, a parameter or a symbol of any kind can be code that takes the container, and so can the
    // method of a conversion. A type, a label and a local are none.
    private static bool CanTakeAContainer(Type type)
    {
        if (type == typeof(CommonConversion) || type == typeof(Conversion) || GetInformation().Contains(type))
            return true;

        if (type.IsGenericType)
            return type.GetGenericArguments().Any(CanTakeAContainer);

        return typeof(ISymbol).IsAssignableFrom(type) &&
            !typeof(ITypeSymbol).IsAssignableFrom(type) &&
            !typeof(ILabelSymbol).IsAssignableFrom(type) &&
            !typeof(ILocalSymbol).IsAssignableFrom(type) &&
            !typeof(IDiscardSymbol).IsAssignableFrom(type);
    }

    private static IEnumerable<PropertyInfo> GetProperties(Type carrier)
    {
        var types = carrier.IsInterface ? carrier.GetInterfaces().Prepend(carrier) : [carrier];

        return types
            .Where(type => type != typeof(IOperation))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .GroupBy(property => property.Name)
            .Select(group => group.First());
    }

    private static Type CarriedSymbolsType =>
        typeof(DoNotCopyValueAnalyzer).Assembly.GetType("Singulink.CodeAnalysis.Analyzers.CarriedSymbols", throwOnError: true)!;

    private static IEnumerable<ITuple> GetList(string name)
    {
        var field = CarriedSymbolsType.GetField(name, BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(field, name);
        return ((IEnumerable)field.GetValue(null)!).Cast<ITuple>();
    }

    private static IEnumerable<(OperationKind Kind, Type Operation)> GetKinds() => GetList("Kinds").Select(entry => ((OperationKind)entry[0]!, (Type)entry[1]!));

    private static IEnumerable<(Type Carrier, string Property)> GetRead() => GetList("Read").Select(entry => ((Type)entry[0]!, (string)entry[1]!));

    private static ImmutableArray<Type> GetInformation()
    {
        var field = CarriedSymbolsType.GetField("Information", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(field);
        var types = (Type[])field.GetValue(null)!;
        return [.. types];
    }
}
