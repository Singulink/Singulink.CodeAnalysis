using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Testing;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// The conversions that C# applies with no conversion node in the operation tree, one test for each row of DESIGN.md's table of them
/// that can carry a conversion that the analyzer cares about: the hiding check of a container (SCA1023), the check of an object with a
/// binding to a class that clones itself (SCA1009), and the boxing of a stored non-copyable value (SCA1004).
/// </summary>
/// <remarks>
/// Docs: DESIGN.md, "Conversions with no node".
/// </remarks>
[TestClass]
public class ImplicitConversionTests
{
    private const string Culture = "System.Globalization.CultureInfo";
    private const string Tagged = "Lib.Tagged<Guard>";
    private const string ListOfGuards = "System.Collections.Generic.List<Guard>";

    private const string Library = """
        using System.Globalization;
        using Singulink.CodeAnalysis;

        namespace Lib
        {
            public class Tagged<[NonCopyable] T> : CultureInfo
            {
                public T Tag = default!;

                public Tagged() : base("en-US") { }
            }

            public static class Factory
            {
                public static Tagged<T> Make<[NonCopyable] T>() => new();
            }
        }
        """;

    // ICoalesceOperation.ValueConversion: the left side of '??' is converted to the type of the result.
    [TestMethod]
    public Task Coalesce_ConvertsItsValueAsAConversionWould() => RunAsync(
        """
        class C
        {
            object Routes(List<Guard> list, Guard? stored)
            {
                var tagged = Factory.Make<Guard>();
                object a = {|#0:list|} ?? new object();
                CultureInfo b = {|#1:tagged|} ?? CultureInfo.InvariantCulture;
                return {|#2:stored|} ?? (object)1;
            }
        }
        """,
        Verify.OpaqueConversion(0, ListOfGuards, "object"),
        Verify.CloningUseConversion(1, Tagged, Culture, Culture, "Guard", Tagged),
        Verify.Boxing(2, "Guard", "object"));

    // ISpreadOperation.ElementConversion: each element of a spread is converted to the element type of the new collection.
    [TestMethod]
    public Task Spread_ConvertsEachElementAsAConversionWould() => RunAsync(
        """
        class C
        {
            void Routes()
            {
                var lists = new List<List<Guard>>();
                var taggeds = new List<Tagged<Guard>> { Factory.Make<Guard>() };
                object[] a = [.. {|#0:lists|}];
                CultureInfo[] b = [.. {|#1:taggeds|}];
                IEnumerable<CultureInfo> c = [.. {|#2:taggeds|}];
                List<object> d = [.. {|#3:taggeds|}];
            }
        }
        """,
        Verify.OpaqueConversion(0, ListOfGuards, "object"),
        Verify.CloningUseConversion(1, Tagged, Culture, Culture, "Guard", Tagged),
        Verify.CloningUseConversion(2, Tagged, Culture, Culture, "Guard", Tagged),
        Verify.CloningUseConversion(3, Tagged, "object", Culture, "Guard", Tagged));

    // DeconstructionInfo.Conversion and its nested conversions: the element of a tuple variable, of the result of a call, of a 'foreach'
    // element, and an output of a Deconstruct method, converted to the type of its variable. An element of a tuple literal has a
    // conversion node of its own (#5).
    [TestMethod]
    public Task Deconstruction_ConvertsEachElementAsAConversionWould() => RunAsync(
        """
        public sealed class Pair
        {
            public void Deconstruct(out List<Guard> list, out int number) { list = new(); number = 1; }
        }

        class C
        {
            static (List<Guard>, int) MakePair() => (new(), 1);

            static (Tagged<Guard>, int) MakeTaggedPair() => (Factory.Make<Guard>(), 1);

            (Guard Guard, int Count) _stored;

            void Routes(Pair pair)
            {
                var tuple = (new List<Guard>(), 1);
                ({|#0:object a|}, int _) = tuple;
                ({|#1:object b|}, int _) = MakePair();
                foreach (({|#2:CultureInfo c|}, int _) in new List<(Tagged<Guard>, int)> { MakeTaggedPair() }) { }
                ({|#3:object d|}, int _) = pair;
                ({|#4:object e|}, int _) = _stored;
                (CultureInfo f, int _) = ({|#5:Factory.Make<Guard>()|} as CultureInfo, 1);
            }
        }
        """,
        Verify.OpaqueConversion(0, ListOfGuards, "object"),
        Verify.OpaqueConversion(1, ListOfGuards, "object"),
        Verify.CloningUseConversion(2, Tagged, Culture, Culture, "Guard", Tagged),
        Verify.OpaqueConversion(3, ListOfGuards, "object"),
        Verify.Boxing(4, "Guard", "object"),
        Verify.CloningUseConversion(5, Tagged, Culture, Culture, "Guard", Tagged));

    // The receiver of an extension that a pattern calls, with no argument for it in the tree: Deconstruct in a deconstruction and in a
    // positional pattern, GetEnumerator in a 'foreach', GetAwaiter in an 'await'.
    [TestMethod]
    public Task PatternCall_ConvertsTheReceiverOfAnExtensionAsAnArgumentWould() => RunAsync(
        """
        public static class Extensions
        {
            public static void Deconstruct(this CultureInfo culture, out int a, out int b) { a = 1; b = 2; }

            public static IEnumerator<int> GetEnumerator(this CultureInfo culture) { yield break; }

            public static TaskAwaiter GetAwaiter(this CultureInfo culture) => Task.CompletedTask.GetAwaiter();

            public static void Deconstruct(this object value, out int a, out int b) { a = 1; b = 2; }
        }

        class C
        {
            async Task Routes(List<Guard> list)
            {
                var tagged = Factory.Make<Guard>();
                var (a, b) = {|#0:tagged|};
                var (c, d) = {|#1:list|};
                foreach (var e in {|#2:tagged|}) { }
                {|#3:await tagged|};
                if (tagged is {|#4:(1, 2)|}) { }
            }
        }
        """,
        Verify.CloningUseConversion(0, Tagged, Culture, Culture, "Guard", Tagged),
        Verify.OpaqueConversion(1, ListOfGuards, "object"),
        Verify.CloningUseConversion(2, Tagged, Culture, Culture, "Guard", Tagged),
        Verify.CloningUseConversion(3, Tagged, Culture, Culture, "Guard", Tagged),
        Verify.CloningUseConversion(4, Tagged, Culture, Culture, "Guard", Tagged));

    // ICompoundAssignmentOperation.InConversion and OutConversion: with a user-defined operator, the target is converted to the type of
    // its left parameter, and the result back to the type of the target.
    [TestMethod]
    public Task CompoundAssignment_ConvertsTheTargetAndTheResultAsConversionsWould() => RunAsync(
        """
        public sealed class Adder
        {
            public static List<Guard> operator +(object left, Adder right) => new();

            public static Tagged<Guard> operator -(CultureInfo left, Adder right) => Factory.Make<Guard>();
        }

        class C
        {
            void Routes(Adder adder)
            {
                object a = 1;
                {|#0:a += adder|};
                var tagged = Factory.Make<Guard>();
                {|#1:tagged|} -= adder;
            }
        }
        """,
        Verify.OpaqueConversion(0, ListOfGuards, "object"),
        Verify.CloningUseConversion(1, Tagged, Culture, Culture, "Guard", Tagged));

    // ITupleBinaryOperation: the elements of tuples that are compared are compared as references, which hands them to no code, as a
    // comparison of the values themselves does.
    [TestMethod]
    public Task TupleComparison_OfReferencesHandsTheElementsToNoCode() => RunAsync(
        """
        class C
        {
            bool Routes(object other)
            {
                var list = new List<Guard>();
                var tagged = Factory.Make<Guard>();
                return (tagged, 1) == (CultureInfo.InvariantCulture, 1) && (list, 2) != (other, 2) && tagged == CultureInfo.InvariantCulture;
            }
        }
        """);

    // A method group made into a delegate holds what it is made on as the delegate's Target, which code that gets the delegate can read as
    // an object. That is not checked, as reflection is not (rules/not-covered.md): a method group is judged as a use of its member, so one of
    // a member that clones warns as a call of it would.
    [TestMethod]
    public Task MethodGroup_IsJudgedAsAUseOfItsMemberAndItsTargetIsNotChecked() => RunAsync(
        """
        public static class Extensions
        {
            public static int Size(this List<Guard> list) => list.Count;
        }

        class C
        {
            void Routes()
            {
                var list = new List<Guard>();
                var tagged = Factory.Make<Guard>();
                Action clear = list.Clear;
                Func<int> size = list.Size;
                Func<string> name = tagged.ToString;
                Func<object> clone = {|#2:tagged.Clone|};
                Action nothing = () => list.Clear();
            }
        }
        """,
        Verify.CloningUseOfCloningMember(2, "CultureInfo.Clone()", Tagged, "Guard", Tagged));

    // IArgumentOperation.InConversion and OutConversion carry a conversion only in Visual Basic: in C#, the conversion of an argument is a
    // node of the tree in its value, and an argument by reference has the type of its parameter. So nothing of the analyzer reads them.
    [TestMethod]
    public async Task ArgumentConversions_AreIdentitiesInCSharp()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            using System;
            using System.Collections.Generic;

            static class C
            {
                static void ByValue(object value) { }

                static void ByIn(in object value) { }

                static void ByRef(ref object value) { }

                static void ByOut(out object value) => value = null!;

                static void Params(params object[] values) { }

                static void Optional(object value = null!) { }

                static void Generic<T>(T value) { }

                static void Run(List<int> list, int number, object reference)
                {
                    ByValue(list);
                    ByValue(number);
                    ByIn(list);
                    ByIn(reference);
                    ByRef(ref reference);
                    ByOut(out reference);
                    ByOut(out object declared);
                    Params(list, number);
                    Optional();
                    Optional(value: list);
                    Generic<IEnumerable<int>>(list);
                }
            }
            """);

        var references = await Verify.Test.NetReferenceAssemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var compilation = CSharpCompilation.Create("Arguments", [tree], references);
        var model = compilation.GetSemanticModel(tree);
        int arguments = 0;

        foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>())
        {
            foreach (var argument in ((IInvocationOperation)model.GetOperation(invocation)!).Arguments)
            {
                arguments++;
                Assert.IsTrue(argument.InConversion.IsIdentity, $"InConversion of {argument.Syntax}");
                Assert.IsTrue(argument.OutConversion.IsIdentity, $"OutConversion of {argument.Syntax}");
            }
        }

        // A 'params' call has one argument for its array, and an optional parameter that is left out has one too.
        Assert.AreEqual(11, arguments);
    }

    private static async Task RunAsync(string source, params DiagnosticResult[] expected)
    {
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, Library, analyzed: true, "Lib");
        var test = new Verify.Test {
            TestCode = """
                using System;
                using System.Collections.Generic;
                using System.Globalization;
                using System.Runtime.CompilerServices;
                using System.Threading.Tasks;
                using Lib;

                """ + source + Environment.NewLine + Sources.Guard,
        };

        test.TestState.AdditionalReferences.Add(library);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}
