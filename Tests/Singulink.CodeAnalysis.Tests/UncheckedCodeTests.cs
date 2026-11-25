using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of code of an assembly that was not built with the analyzer: the marker that tells the two apart, the marks that such an
/// assembly cannot grant, a member that gets at a non-copyable value other than by value (SCA1024), and a container that such a member
/// hands out.
/// </summary>
/// <remarks>
/// Docs: articles/rules/unchecked-code.md.
/// </remarks>
[PrefixTestClass]
public class UncheckedCodeTests
{
    // A library of a third party, with a non-copyable struct of its own.
    private const string Library = """
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        namespace ThirdParty
        {
            [NonCopyable]
            public struct Lease
            {
                public int Id;

                public void Release() { }

                public void Merge(ref Lease other) { }
            }

            public static class Leases
            {
                public static void Inspect(ref Lease lease) { }

                public static void Look(in Lease lease) { }

                public static void Take(Lease lease) { }

                public static void Fill(List<Lease> leases) { }

                public static void Scan(Span<Lease> leases) { }

                public static unsafe void Point(Lease* lease) { }

                public static void Make(out Lease lease) => lease = default;

                public static List<Lease> Create() => new();

                public static bool TryTake(out List<Lease> leases)
                {
                    leases = new();
                    return true;
                }

                public static void Extend(this List<Lease> leases) { }

                public static void Adopt([MovedFrom] ref List<Lease> leases) => leases = null!;

                public static T Move<[NonCopyable] T>([MovedFrom] ref T value)
                {
                    T moved = value;
                    value = default!;
                    return moved;
                }
            }

            public static class Ensure
            {
                public static void RefersToSomething([NotNullRef] ref int value) { }
            }

            public delegate void LeaseHandler(ref Lease lease);

            public sealed class Pool
            {
                public static Pool operator +(Pool pool, List<Lease> leases) => pool;

                public static explicit operator Pool(Lease[] leases) => new();
            }

            public class LeaseList : List<Lease> { }

            public interface ILeases : IReadOnlyList<Lease> { }

            public sealed class LeaseSet : ILeases
            {
                private readonly Lease[] _leases = new Lease[1];

                public Lease this[int index] => _leases[index];

                public int Count => _leases.Length;

                public IEnumerator<Lease> GetEnumerator() => ((IEnumerable<Lease>)_leases).GetEnumerator();

                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public static class LeaseLists
            {
                public static LeaseList Make() => new();
            }
        }
        """;

    // The uses of the library. The places are marked for every finding that the rules for unchecked code can give.
    private const string Uses = """
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;
        using ThirdParty;

        unsafe class C
        {
            private Lease _lease;
            private Lease _other;
            private List<Lease> _leases = new();

            void Inbound(LeaseHandler handler, Lease* pointer)
            {
                {|#0:Leases.Inspect(ref _lease)|};
                {|#1:Leases.Look(in _lease)|};
                Leases.Take(NonCopyable.Move(ref _lease));
                Leases.Make(out var made);
                made.Release();

                var leases = new List<Lease>();
                {|#2:Leases.Fill(leases)|};
                {|#3:leases.Extend()|};
                {|#4:Leases.Scan(new Span<Lease>(new Lease[1]))|};

                {|#5:Leases.Point(pointer)|};

                {|#6:handler(ref _lease)|};

                // The members of the non-copyable type itself are trusted.
                _lease.Merge(ref _other);
                _lease.Release();
            }

            void Operators(Pool pool, List<Lease> leases, Lease[] array)
            {
                pool = {|#14:pool + leases|};
                pool = {|#15:(Pool)array|};
            }

            List<Lease> Filled()
            {
                var leases = new List<Lease>();
                {|#7:Leases.Fill(leases)|};
                return {|#8:leases|};
            }

            void Outbound()
            {
                Lease first = {|#9:Leases.Create()[0]|};
                Lease element = {|#10:LeaseLists.Make()[0]|};

                Leases.TryTake(out var taken);
                Lease out1 = {|#13:taken[0]|};
            }

            Lease Generic() => {|#11:Leases.Move(ref _lease)|};

            void Adopted() => {|#16:Leases.Adopt(ref {|#17:_leases|})|};

            Lease ThroughInterface()
            {
                ILeases leases = new LeaseSet();
                return {|#18:leases[0]|};
            }
        }

        static class Mover
        {
            static void Hand([MovedFrom] ref Lease from, ref Lease to, ref int count)
            {
                Ensure.RefersToSomething(ref count);
                to = from;
                {|#12:count|}++;
                from = default;
            }
        }
        """;

    // What a message says of a type of unchecked code that is a container through what it derives from or implements.
    private static string NeverNew(string type) =>
        $"'{type}' is of an assembly that was not built with the analyzer and is a container only through what it derives from or implements, " +
        "so nothing checks what its own code puts in it, and an object of it is never a new container";

    private static Task<MetadataReference> LibraryAsync(bool analyzed) =>
        Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, Library, analyzed);

    private static async Task<Verify.Test> CreateTestAsync(bool analyzed, string? listFile = null)
    {
        var test = new Verify.Test { TestCode = Uses };
        test.TestState.AdditionalReferences.Add(await LibraryAsync(analyzed));

        if (listFile is not null)
            test.TestState.AdditionalFiles.Add((Verify.ListFileName, listFile));

        return test;
    }

    // Built with the analyzer, the library is checked code: its marks hold, and nothing of the rules for unchecked code applies. A
    // class of it that derives from a listed container is never new for the members that it inherits, as before.
    [TestMethod]
    public async Task LibraryBuiltWithTheAnalyzer_IsCheckedCode()
    {
        var test = await CreateTestAsync(analyzed: true);
        test.ExpectedDiagnostics.Add(Verify.ReadFromStoredContainer(10, "'List<T>.this[int]'", "ThirdParty.Lease", canBeMoved: false));

        // A [MovedFrom] parameter of checked code moves the container, which the method must own.
        test.ExpectedDiagnostics.Add(Verify.ContainerMovedFromUnowned(17, "System.Collections.Generic.List<ThirdParty.Lease>", "_leases"));

        // A container that comes out through an 'out' argument is stored, from any assembly.
        test.ExpectedDiagnostics.Add(Verify.ReadFromStoredContainer(13, "'List<T>.this[int]'", "ThirdParty.Lease", "'taken' is not the method's own anywhere in the method, because on line 52 it is given what something else hands out"));
        await test.RunAsync();
    }

    // Built without it, every member that gets at a non-copyable value other than by value is reported, a container that one of its
    // members hands out or holds is stored, and the marks that grant something count as absent.
    [TestMethod]
    public async Task LibraryBuiltWithoutTheAnalyzer_IsUncheckedCode()
    {
        var test = await CreateTestAsync(analyzed: false);
        test.ExpectedDiagnostics.AddRange([
            Verify.UncheckedMember(0, "Leases.Inspect(ref Lease)", "lease", "ThirdParty.Lease", "M:ThirdParty.Leases.Inspect(ThirdParty.Lease@)"),
            Verify.UncheckedMember(1, "Leases.Look(in Lease)", "lease", "ThirdParty.Lease", "M:ThirdParty.Leases.Look(ThirdParty.Lease@)"),
            Verify.UncheckedMember(2, "Leases.Fill(List<Lease>)", "leases", "System.Collections.Generic.List<ThirdParty.Lease>", "M:ThirdParty.Leases.Fill(System.Collections.Generic.List{ThirdParty.Lease})"),
            Verify.UncheckedMember(3, "Leases.Extend(List<Lease>)", "leases", "System.Collections.Generic.List<ThirdParty.Lease>", "M:ThirdParty.Leases.Extend(System.Collections.Generic.List{ThirdParty.Lease})"),
            Verify.UncheckedMember(4, "Leases.Scan(Span<Lease>)", "leases", "System.Span<ThirdParty.Lease>", "M:ThirdParty.Leases.Scan(System.Span{ThirdParty.Lease})"),
            Verify.UncheckedMember(5, "Leases.Point(Lease*)", "lease", "ThirdParty.Lease*", "M:ThirdParty.Leases.Point(ThirdParty.Lease*)"),
            Verify.UncheckedMember(6, "LeaseHandler.Invoke(ref Lease)", "lease", "ThirdParty.Lease", "M:ThirdParty.LeaseHandler.Invoke(ThirdParty.Lease@)"),
            Verify.UncheckedMember(14, "Pool.operator +(Pool, List<Lease>)", "leases", "System.Collections.Generic.List<ThirdParty.Lease>", "M:ThirdParty.Pool.op_Addition(ThirdParty.Pool,System.Collections.Generic.List{ThirdParty.Lease})"),
            Verify.UncheckedMember(15, "Pool.explicit operator Pool(Lease[])", "leases", "ThirdParty.Lease[]", "M:ThirdParty.Pool.op_Explicit(ThirdParty.Lease[])~ThirdParty.Pool"),
            Verify.UncheckedMember(7, "Leases.Fill(List<Lease>)", "leases", "System.Collections.Generic.List<ThirdParty.Lease>", "M:ThirdParty.Leases.Fill(System.Collections.Generic.List{ThirdParty.Lease})"),
            Verify.UnownedContainerReturned(
                8,
                "C.Filled()",
                "System.Collections.Generic.List<ThirdParty.Lease>",
                "'leases' is not the method's own anywhere in the method, because on line 43 it is passed to 'Leases.Fill(List<Lease>)', " +
                "which is taken to keep it: the member is of an assembly that was not built with the analyzer, and the list does not mark it 'borrows'"),
            Verify.ReadFromStoredContainer(
                9,
                "'List<T>.this[int]'",
                "ThirdParty.Lease",
                because: "'Leases.Create()' is a member of an assembly that was not built with the analyzer, so the container that it hands " +
                    "out counts as held by something else; if it hands out a container that nothing else holds, add " +
                    "'M:ThirdParty.Leases.Create;new' to a NonCopyableSymbols.txt file",
                canBeMoved: false),
            Verify.ReadFromStoredContainer(10, "'List<T>.this[int]'", "ThirdParty.Lease", because: NeverNew("ThirdParty.LeaseList"), canBeMoved: false),
            Verify.UnlistedMemberOfUncheckedCode(11, "Leases.Move<T>(ref T)", "ThirdParty.Lease", "T", "M:ThirdParty.Leases.Move``1(``0@)"),
            Verify.UncheckedMember(16, "Leases.Adopt(ref List<Lease>)", "leases", "System.Collections.Generic.List<ThirdParty.Lease>", "M:ThirdParty.Leases.Adopt(System.Collections.Generic.List{ThirdParty.Lease}@)"),
            Verify.ReadFromStoredContainer(18, "'IReadOnlyList<T>.this[int]'", "ThirdParty.Lease", NeverNew("ThirdParty.ILeases")),
            Verify.MovedFromParameterNotAssignedAtOnceThroughReference(12, "from", "count"),
            Verify.ReadFromStoredContainer(13, "'List<T>.this[int]'", "ThirdParty.Lease", "'taken' is not the method's own anywhere in the method, because on line 52 it is given what something else hands out"),
        ]);

        await test.RunAsync();
    }

    // A list entry says of a member of such an assembly what the analyzer cannot see: 'borrows' that it only uses what it gets, 'new'
    // that a container that it hands out is held by nothing else.
    [TestMethod]
    public async Task LibraryBuiltWithoutTheAnalyzer_ListEntriesSayWhatItDoes()
    {
        var test = await CreateTestAsync(analyzed: false, listFile: """
            M:ThirdParty.Leases.Inspect(ThirdParty.Lease@);borrows
            M:ThirdParty.Leases.Look(ThirdParty.Lease@);borrows
            M:ThirdParty.Leases.Fill(System.Collections.Generic.List{ThirdParty.Lease});borrows
            M:ThirdParty.Leases.Extend(System.Collections.Generic.List{ThirdParty.Lease});borrows
            M:ThirdParty.Leases.Scan(System.Span{ThirdParty.Lease});borrows
            M:ThirdParty.Leases.Point(ThirdParty.Lease*);borrows
            M:ThirdParty.LeaseHandler.Invoke(ThirdParty.Lease@);borrows
            M:ThirdParty.Pool.op_Addition(ThirdParty.Pool,System.Collections.Generic.List{ThirdParty.Lease});borrows
            M:ThirdParty.Pool.op_Explicit(ThirdParty.Lease[])~ThirdParty.Pool;borrows
            M:ThirdParty.Leases.Create;new
            M:ThirdParty.Leases.Move``1(``0@);new
            M:ThirdParty.Leases.Adopt(System.Collections.Generic.List{ThirdParty.Lease}@);borrows
            """);

        // A class of the library that derives from a container stays one whose own code nobody checks, and the marks stay absent.
        test.ExpectedDiagnostics.AddRange([
            Verify.ReadFromStoredContainer(10, "'List<T>.this[int]'", "ThirdParty.Lease", because: NeverNew("ThirdParty.LeaseList"), canBeMoved: false),
            Verify.MovedFromParameterNotAssignedAtOnceThroughReference(12, "from", "count"),
            Verify.ReadFromStoredContainer(13, "'List<T>.this[int]'", "ThirdParty.Lease", "'taken' is not the method's own anywhere in the method, because on line 52 it is given what something else hands out"),
            Verify.ReadFromStoredContainer(18, "'IReadOnlyList<T>.this[int]'", "ThirdParty.Lease", NeverNew("ThirdParty.ILeases")),
        ]);

        await test.RunAsync();
    }

    // A setter, an init accessor and an indexer setter get their value as a parameter. A member of a non-copyable type is trusted with a
    // value of that type only, and a pointer at a pointer gets at what the innermost one points at.
    [TestMethod]
    public async Task LibraryBuiltWithoutTheAnalyzer_SettersOtherValuesOfANonCopyableTypeAndPointersAtPointersAreJudged()
    {
        var test = new Verify.Test {
            TestCode = """
                using System.Collections.Generic;
                using Vendor;

                unsafe class C
                {
                    private Guard _guard;
                    private Lease _lease;
                    private Lease _other;

                    void Setters(Holder holder)
                    {
                        {|#0:holder.SetOnly|} = new List<Guard>();
                        _ = new Holder { {|#1:InitOnly|} = new List<Guard>() };
                        {|#2:holder[0]|} = new List<Guard>();
                        {|#3:holder.Guards|} = new List<Guard>();
                        _ = holder.Guards.Count;
                    }

                    void Trust()
                    {
                        {|#4:Lease.Grab(ref _guard)|};
                        {|#5:_lease.Attach(new List<Guard>())|};
                        _lease.Join(ref _other);
                        _lease.Release();
                    }

                    void Pointers(Guard** guards) => {|#6:Raw.Fill(guards)|};
                }
                """,
        };

        test.TestState.AdditionalReferences.Add(await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, VendorLibrary, analyzed: false));
        const string GuardList = "System.Collections.Generic.List<Vendor.Guard>";

        test.ExpectedDiagnostics.AddRange([
            Verify.UncheckedMember(0, "Holder.SetOnly", "value", GuardList, "P:Vendor.Holder.SetOnly"),
            Verify.UncheckedMember(1, "Holder.InitOnly", "value", GuardList, "P:Vendor.Holder.InitOnly"),
            Verify.UncheckedMember(2, "Holder.this[int]", "value", GuardList, "P:Vendor.Holder.Item(System.Int32)"),
            Verify.UncheckedMember(3, "Holder.Guards", "value", GuardList, "P:Vendor.Holder.Guards"),
            Verify.UncheckedMember(4, "Lease.Grab(ref Guard)", "guard", "Vendor.Guard", "M:Vendor.Lease.Grab(Vendor.Guard@)"),
            Verify.UncheckedMember(5, "Lease.Attach(List<Guard>)", "guards", GuardList, "M:Vendor.Lease.Attach(System.Collections.Generic.List{Vendor.Guard})"),
            Verify.UncheckedMember(6, "Raw.Fill(Guard**)", "guards", "Vendor.Guard**", "M:Vendor.Raw.Fill(Vendor.Guard**)"),
        ]);

        await test.RunAsync();
    }

    // A member that the compiler calls for an operation, with nothing written for it, is judged as the member written out: what it gets
    // and what it hands out.
    [TestMethod]
    public async Task LibraryBuiltWithoutTheAnalyzer_MembersThatTheCompilerCallsAreJudgedAsWrittenOut()
    {
        var test = new Verify.Test {
            TestCode = """
                using System;
                using System.Collections.Generic;
                using System.Threading.Tasks;
                using Vendor;

                class C
                {
                    void ForEach(List<List<Guard>> lists)
                    {
                        foreach (Pool pool in {|#0:{|#1:lists|}|}) { }
                    }

                    Pool[] Spread()
                    {
                        var lists = new List<List<Guard>>();
                        return [.. {|#2:lists|}];
                    }

                    void Compound(List<Guard> guards, Pool pool) => {|#4:guards += pool|};

                    void Deconstruction(Pair pair)
                    {
                        (Pool pool, int count) = {|#5:pair|};
                    }

                    void Event(Box box, Action<List<Guard>> handler)
                    {
                        {|#6:box.Handed|} += handler;
                        {|#7:box.Handed|} -= handler;
                    }

                    Guard Current()
                    {
                        foreach (var guards in new Lists())
                            return {|#8:guards[0]|};

                        return default;
                    }

                    Guard Enumerator()
                    {
                        foreach (var guard in {|#9:new Guards()|})
                            return guard;

                        return default;
                    }

                    Guard Source()
                    {
                        foreach (var guard in {|#11:new GuardSource()|})
                            return guard;

                        return default;
                    }

                    Guard OwnEnumerator()
                    {
                        foreach (var guard in {|#12:new EnumeratorSource()|})
                            return guard;

                        return default;
                    }

                    async Task<int> Await()
                    {
                        Guard guard = {|#10:(await new Awaitable())[0]|};
                        return guard.Id;
                    }
                }
                """,
        };

        test.TestState.AdditionalReferences.Add(await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, VendorLibrary, analyzed: false));
        const string GuardList = "System.Collections.Generic.List<Vendor.Guard>";
        const string ToPool = "M:Vendor.Pool.op_Implicit(System.Collections.Generic.List{Vendor.Guard})~Vendor.Pool";

        test.ExpectedDiagnostics.AddRange([
            Verify.OpaqueConversion(1, GuardList, "Vendor.Pool"),
            Verify.UncheckedMember(0, "Pool.implicit operator Pool(List<Guard>)", "guards", GuardList, ToPool),
            Verify.UncheckedMember(2, "Pool.implicit operator Pool(List<Guard>)", "guards", GuardList, ToPool),
            Verify.UncheckedMember(4, "Pool.implicit operator Pool(List<Guard>)", "guards", GuardList, ToPool),
            Verify.UncheckedMember(5, "Pool.implicit operator Pool(List<Guard>)", "guards", GuardList, ToPool),
            Verify.UncheckedMember(6, "Box.Handed", "value", "System.Action<" + GuardList + ">", "E:Vendor.Box.Handed"),
            Verify.UncheckedMember(7, "Box.Handed", "value", "System.Action<" + GuardList + ">", "E:Vendor.Box.Handed"),
            Verify.ReadFromStoredContainer(8, "'List<T>.this[int]'", "Vendor.Guard", "'guards' is not the method's own anywhere in the method, because on line 34 it is given what something else holds"),
            Verify.ReadFromStoredContainer(9, "'foreach'", "Vendor.Guard", NeverNew("Vendor.Guards")),
            Verify.ReadFromStoredContainer(
                10,
                "'List<T>.this[int]'",
                "Vendor.Guard",
                "'Awaiter.GetResult()' is a member of an assembly that was not built with the analyzer, so the container that it hands out " +
                    "counts as held by something else; if it hands out a container that nothing else holds, add 'M:Vendor.Awaiter.GetResult;new' " +
                    "to a NonCopyableSymbols.txt file"),
            Verify.ReadFromStoredContainer(
                11,
                "'foreach'",
                "Vendor.Guard",
                "'GuardSource.GetEnumerator()' is a member of an assembly that was not built with the analyzer, so the container that it hands " +
                    "out counts as held by something else; if it hands out a container that nothing else holds, add " +
                    "'M:Vendor.GuardSource.GetEnumerator;new' to a NonCopyableSymbols.txt file"),
            Verify.ReadFromStoredContainer(12, "'foreach'", "Vendor.Guard", NeverNew("Vendor.IGuardEnumerator")),
        ]);

        await test.RunAsync();
    }

    // A mark that does not count in an assembly that was not built with the analyzer is named as such, not as missing.
    [TestMethod]
    public async Task LibraryBuiltWithoutTheAnalyzer_MessagesSayThatItsMarksDoNotCount()
    {
        var test = new Verify.Test {
            TestCode = """
                using Singulink.CodeAnalysis;
                using Vendor;

                sealed class MyMover : Mover
                {
                    public override void Move([MovedFrom] ref Guard {|#0:from|}, out Guard to)
                    {
                        to = from;
                        from = default;
                    }
                }

                static class Handlers
                {
                    static MoverHandler Make() => {|#1:([MovedFrom] ref Guard from, out Guard to) =>
                    {
                        to = from;
                        from = default;
                    }|};
                }
                """,
        };

        test.TestState.AdditionalReferences.Add(await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, VendorLibrary, analyzed: false));

        test.ExpectedDiagnostics.AddRange([
            Verify.MovedFromAddedOverUncheckedMark(0, "from", "MyMover.Move(ref Guard, out Guard)", "'Mover.Move(ref Guard, out Guard)', which the member overrides or implements,"),
            Verify.MovedFromAddedOverUncheckedMark(1, "from", "lambda expression", "delegate 'MoverHandler', which it is converted to,"),
        ]);

        await test.RunAsync();
    }

    private const string VendorLibrary = """
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        namespace Vendor
        {
            [NonCopyable]
            public struct Guard
            {
                public int Id;
            }

            [NonCopyable]
            public struct Lease
            {
                public static void Grab(ref Guard guard) { }

                public void Attach(List<Guard> guards) { }

                public void Join(ref Lease other) { }

                public void Release() { }
            }

            public class Holder
            {
                public List<Guard> SetOnly { set { } }

                public List<Guard> InitOnly { init { } }

                public List<Guard> Guards { get; set; } = new();

                public List<Guard> this[int index] { set { } }
            }

            public static class Raw
            {
                public static unsafe void Fill(Guard** guards) { }
            }

            public sealed class Pool
            {
                public static implicit operator Pool(List<Guard> guards) => new();

                public static implicit operator List<Guard>(Pool pool) => new();

                public static Pool operator +(Pool pool, Pool other) => pool;
            }

            public sealed class Pair
            {
                public void Deconstruct(out List<Guard> guards, out int count) => (guards, count) = (new(), 0);
            }

            public sealed class Box
            {
                public event Action<List<Guard>>? Handed;
            }

            public sealed class ListsEnumerator
            {
                public bool MoveNext() => false;

                public List<Guard> Current => new();
            }

            public sealed class Lists
            {
                public ListsEnumerator GetEnumerator() => new();
            }

            public sealed class Guards : IEnumerable<Guard>
            {
                public IEnumerator<Guard> GetEnumerator() => new List<Guard>().GetEnumerator();

                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public sealed class GuardSource
            {
                public IEnumerator<Guard> GetEnumerator() => new List<Guard>().GetEnumerator();
            }

            public interface IGuardEnumerator : IEnumerator<Guard> { }

            public sealed class EnumeratorSource
            {
                public IGuardEnumerator GetEnumerator() => null!;
            }

            public abstract class Mover
            {
                public abstract void Move([MovedFrom] ref Guard from, out Guard to);
            }

            public delegate void MoverHandler([MovedFrom] ref Guard from, out Guard to);

            public sealed class Awaitable
            {
                public Awaiter GetAwaiter() => new();
            }

            public sealed class Awaiter : System.Runtime.CompilerServices.INotifyCompletion
            {
                public bool IsCompleted => true;

                public void OnCompleted(Action continuation) { }

                public List<Guard> GetResult() => new();
            }
        }
        """;

    // The attributes assembly of the package carries the marker, so the marks of NonCopyable.Move and of the two checks are trusted. Its
    // value is the version of the package, which is that of the analyzers.
    [TestMethod]
    public void AttributesAssembly_CarriesTheMarkerOfABuildWithTheAnalyzer()
    {
        var assembly = typeof(NonCopyableAttribute).Assembly;
        var marker = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), inherit: false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "Singulink.CodeAnalysis.Analyzed");

        Assert.IsNotNull(marker, "Singulink.CodeAnalysis.dll has no marker of a build with the analyzer.");
        Assert.AreEqual(GetPackageVersion(assembly), marker.Value);
    }

    // The targets file of the analyzers package records the package's own version in the marker: the copy in the package has it as a
    // literal, under build and under buildTransitive, and not the placeholder of the file in the repository.
    [TestMethod]
    public void AnalyzersPackage_RecordsItsVersionInTheMarker()
    {
        var analyzers = typeof(Singulink.CodeAnalysis.Analyzers.DoNotCopyValueAnalyzer).Assembly;
        string version = GetPackageVersion(analyzers);
#if DEBUG
        const string Configuration = "Debug";
#else
        const string Configuration = "Release";
#endif
        string package = Path.Combine(FindRepository(), "Source", "Singulink.CodeAnalysis.Analyzers", "bin", Configuration, $"Singulink.CodeAnalysis.Analyzers.{version}.nupkg");
        Assert.IsTrue(File.Exists(package), $"No package {package}: the analyzers project packs on build.");

        using var archive = System.IO.Compression.ZipFile.OpenRead(package);

        foreach (string folder in new[] { "build", "buildTransitive" })
        {
            var entry = archive.GetEntry($"{folder}/Singulink.CodeAnalysis.Analyzers.targets");
            Assert.IsNotNull(entry, $"The package has no {folder}/Singulink.CodeAnalysis.Analyzers.targets.");

            using var reader = new StreamReader(entry.Open());
            string targets = reader.ReadToEnd();

            StringAssert.Contains(targets, "<_Parameter1>Singulink.CodeAnalysis.Analyzed</_Parameter1>");
            StringAssert.Contains(targets, $"<_Parameter2>{version}</_Parameter2>");
            Assert.IsFalse(targets.Contains("0.0.0-unpacked", StringComparison.Ordinal), $"{folder}: the placeholder was packed.");
        }

        static string FindRepository()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Singulink.CodeAnalysis.slnx")))
                    return directory.FullName;
            }

            throw new DirectoryNotFoundException("No repository above the test assembly.");
        }
    }

    // The version of the package that an assembly was built for, from its informational version without the source revision.
    private static string GetPackageVersion(System.Reflection.Assembly assembly)
    {
        string informational = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .Single()
            .InformationalVersion;

        int plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }

    // The version that an assembly records in its marker: the version where the value is one, and none for the "1" that the marker had
    // before it recorded one, for any other value, and for an assembly with no marker. Nothing else reads it.
    [TestMethod]
    [DataRow("0.7.0", "0.7.0")]
    [DataRow("0.7.1-local.2", "0.7.1-local.2")]
    [DataRow("1.0.0+abc123", "1.0.0")]
    [DataRow("1", null)]
    [DataRow("0.7", null)]
    [DataRow("1.2.3.4", null)]
    [DataRow("0.7.0-", null)]
    [DataRow("a.b.c", null)]
    [DataRow("built with the analyzers", null)]
    [DataRow(null, null)]
    public async Task Marker_RecordsTheVersionOfTheAnalyzers(string? value, string? expected)
    {
        string source = value is null
            ? "namespace Recorded { public class C { } }"
            : $$"""[assembly: System.Reflection.AssemblyMetadata("Singulink.CodeAnalysis.Analyzed", "{{value}}")] namespace Recorded { public class C { } }""";

        var reference = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, source, analyzed: false, "Recorded");
        var references = (await Verify.Test.NetReferenceAssemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None)).Add(reference);
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("User", references: references);
        var assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(reference)!;

        var helper = typeof(Singulink.CodeAnalysis.Analyzers.DoNotCopyValueAnalyzer).Assembly
            .GetType("Singulink.CodeAnalysis.Analyzers.AnalyzedAssemblies", throwOnError: true)!
            .GetMethod("GetRecordedVersion")!;

        object? recorded = helper.Invoke(null, [assembly]);
        Assert.AreEqual(expected, recorded?.ToString());
    }

    // The marker is read by its key, whatever its value, and only from System.Reflection.AssemblyMetadataAttribute: the version that it
    // records, the "1" that it had before, and any other string.
    [TestMethod]
    [DataRow("""[assembly: System.Reflection.AssemblyMetadata("Singulink.CodeAnalysis.Analyzed", "0.7.0")]""", true)]
    [DataRow("""[assembly: System.Reflection.AssemblyMetadata("Singulink.CodeAnalysis.Analyzed", "0.7.1-local.2")]""", true)]
    [DataRow("""[assembly: System.Reflection.AssemblyMetadata("Singulink.CodeAnalysis.Analyzed", "1")]""", true)]
    [DataRow("""[assembly: System.Reflection.AssemblyMetadata("Singulink.CodeAnalysis.Analyzed", "any string at all")]""", true)]
    [DataRow("""[assembly: System.Reflection.AssemblyMetadata("Singulink.CodeAnalysis.Analyzed", "2")]""", true)]
    [DataRow("""[assembly: System.Reflection.AssemblyMetadata("Singulink.CodeAnalysis.Analysed", "1")]""", false)]
    [DataRow("""[assembly: Other.AssemblyMetadata("Singulink.CodeAnalysis.Analyzed", "1")]""", false)]
    public async Task Marker_IsReadByItsKey(string marker, bool analyzed)
    {
        var test = new Verify.Test {
            TestCode = """
                using ThirdParty;

                class C
                {
                    private Lease _lease;

                    void M() => {|#0:Leases.Inspect(ref _lease)|};
                }
                """,
        };

        // The marker goes after the usings of the library; an attribute of the name in another namespace is declared at its end.
        string source = Library.Replace("namespace ThirdParty", marker + "\n\nnamespace ThirdParty", StringComparison.Ordinal) +
            "\nnamespace Other { class AssemblyMetadataAttribute(string key, string value) : System.Attribute { public string Key => key + value; } }";

        test.TestState.AdditionalReferences.Add(await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, source, analyzed: false));

        if (!analyzed)
            test.ExpectedDiagnostics.Add(Verify.UncheckedMember(0, "Leases.Inspect(ref Lease)", "lease", "ThirdParty.Lease", "M:ThirdParty.Leases.Inspect(ThirdParty.Lease@)"));

        await test.RunAsync();
    }
}
