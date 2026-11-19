using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the rule that a method hands over the container that it returns, and of <c>[Borrowed]</c> for a method that keeps it.
/// </summary>
/// <remarks>
/// Docs: articles/concepts/returning-containers.md.
/// </remarks>
[PrefixTestClass]
public class ReturnedContainerTests
{
    [TestMethod]
    public Task Method_MustNotReturnAContainerThatSomethingElseHolds() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        class Holder
        {
            private Task<Guard> _cached = null!;
            private readonly List<Guard> _guards = new();
            private readonly Guard[] _array = new Guard[4];
            private readonly Dictionary<string, List<Guard>> _byKey = new();

            private List<Guard> Guards { get; } = new();

            // The result of a call is a new value: every caller would get the same container and read its guards as new.
            Task<Guard> FromField() => {|#0:_cached|};

            List<Guard> FromProperty()
            {
                return {|#1:Guards|};
            }

            List<Guard> FromIndexer(string key) => {|#2:_byKey[key]|};

            static Task<Guard> FromParameter(Task<Guard> task) => {|#3:task|};

            List<Guard> FromElement(List<Guard>[] lists) => {|#4:lists[0]|};

            IEnumerable<Guard> AsAnInterface() => {|#5:_guards|};

            IEnumerable<Guard> ArrayAsAnInterface() => {|#6:_array|};

            static Task<Guard> FromACast(object boxed) => {|#7:(Task<Guard>)boxed|};

            async Task<List<Guard>> FromAnAsyncMethod()
            {
                await Task.Yield();
                return {|#8:_guards|};
            }

            IEnumerable<List<Guard>> FromAnIterator()
            {
                yield return {|#9:_guards|};
            }

            Func<List<Guard>> FromALambda() => () => {|#10:_guards|};

            List<Guard> FromALocalFunction()
            {
                return Local();

                List<Guard> Local() => {|#11:_guards|};
            }

            public static List<Guard> operator +(Holder holder, int number) => {|#12:holder._guards|};

            // One such branch is enough.
            List<Guard> FromAConditional(bool own) => {|#13:own ? new List<Guard>() : _guards|};

            List<Guard> FromACoalesce(List<Guard> other) => {|#14:other ?? new List<Guard>()|};

            List<Guard> FromASwitch(int number) => {|#15:number switch { 0 => _guards, _ => new() }|};

            // What the method created or got from a call is its own, and so is what it moved out.
            List<Guard> Created() => new();

            List<Guard> FromACall(RwLock rwLock) => rwLock.CreateGuards();

            List<Guard> Moved(RwLock rwLock)
            {
                var list = rwLock.CreateGuards();
                return NonCopyable.Move(ref list);
            }

            List<Guard> Nothing() => null!;

            List<Guard> OwnBranches(bool many, RwLock rwLock) => many ? rwLock.CreateGuards() : new List<Guard>();

            // An array and a view are checked like any other container: an array segment and the typed interfaces of an array hand out
            // elements by value.
            Guard[] Array() => {|#16:_array|};

            ArraySegment<Guard> Segment() => {|#17:_array|};

            // A span or a memory cannot become an interface, and every by-value read of an element through one is reported whatever
            // it came from.
            Span<Guard> Span() => _array;

            ReadOnlyMemory<Guard> Memory() => _array;

            Guard[] NewArray() => new Guard[4];

            // What the getter of a property or an indexer returns is stored for every caller, and a delegate holds no value.
            Task<Guard> Cached => _cached;

            List<Guard> this[string key] => _byKey[key];

            Func<Guard> Factory(Func<Guard> factory) => factory;
        }

        {{Sources.Guard}}
        """,
        Verify.UnownedContainerReturned(0, "Holder.FromField()", "System.Threading.Tasks.Task<Guard>"),
        Verify.UnownedContainerReturned(1, "Holder.FromProperty()", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(2, "Holder.FromIndexer(string)", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(3, "Holder.FromParameter(Task<Guard>)", "System.Threading.Tasks.Task<Guard>"),
        Verify.UnownedContainerReturned(4, "Holder.FromElement(List<Guard>[])", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(5, "Holder.AsAnInterface()", "System.Collections.Generic.IEnumerable<Guard>"),
        Verify.UnownedContainerReturned(6, "Holder.ArrayAsAnInterface()", "System.Collections.Generic.IEnumerable<Guard>"),
        Verify.UnownedContainerReturned(7, "Holder.FromACast(object)", "System.Threading.Tasks.Task<Guard>"),
        Verify.UnownedContainerReturned(8, "Holder.FromAnAsyncMethod()", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(9, "Holder.FromAnIterator()", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(10, "lambda expression", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(11, "Local()", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(12, "Holder.operator +(Holder, int)", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(13, "Holder.FromAConditional(bool)", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(14, "Holder.FromACoalesce(List<Guard>)", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(15, "Holder.FromASwitch(int)", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(16, "Holder.Array()", "Guard[]"),
        Verify.UnownedContainerReturned(17, "Holder.Segment()", "System.ArraySegment<Guard>"));

    [TestMethod]
    public Task Local_IsTheMethodsOwnIfItOnlyFilledIt() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;

        class C
        {
            List<Guard> _field = new();
            List<Guard>[] _lists = new List<Guard>[2];
            Action _action;

            static void Fill(List<Guard> list) { }

            static bool TryGet(out List<Guard> list)
            {
                list = new();
                return true;
            }

            // A local that the method only filled is its own, also when it passed the local as an argument on the way.
            List<Guard> Built(RwLock rwLock)
            {
                var list = new List<Guard>();
                list.Add(rwLock.Enter());
                Fill(list);
                return list;
            }

            List<Guard> AssignedLater(RwLock rwLock, bool many)
            {
                List<Guard> list = null;

                if (many)
                    list = rwLock.CreateGuards();

                list ??= new();
                return list;
            }

            // A local that was assigned something stored is not.
            List<Guard> FromAField()
            {
                var list = _field;
                return {|#0:list|};
            }

            List<Guard> FromAFieldLater()
            {
                var list = new List<Guard>();
                list = _field;
                return {|#1:list|};
            }

            List<Guard> FromAnOutArgument()
            {
                TryGet(out var list);
                return {|#2:list|};
            }

            List<Guard> FromAnOutArgumentLater()
            {
                var list = new List<Guard>();
                TryGet(out list);
                return {|#3:list|};
            }

            List<Guard> FromAPattern(object boxed)
            {
                if (boxed is List<Guard> list)
                    return {|#4:list|};

                return new();
            }

            List<Guard> FromAForEach()
            {
                foreach (var list in _lists)
                    return {|#5:list|};

                return new();
            }

            // Nor is one that the method has also stored somewhere, or that a lambda captures.
            List<Guard> AlsoInAField()
            {
                var list = new List<Guard>();
                _field = list;
                return {|#6:list|};
            }

            List<Guard> AlsoInAnElement()
            {
                var list = new List<Guard>();
                _lists[0] = list;
                return {|#7:list|};
            }

            List<Guard> AlsoInAnotherVariable()
            {
                var list = new List<Guard>();
                var other = list;
                return {|#8:list|};
            }

            List<Guard> TheOtherVariable()
            {
                var list = new List<Guard>();
                var other = list;
                return {|#9:other|};
            }

            List<Guard> Captured()
            {
                var list = new List<Guard>();
                _action = () => list.Clear();
                return {|#10:list|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UnownedContainerReturned(0, "C.FromAField()", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(1, "C.FromAFieldLater()", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(2, "C.FromAnOutArgument()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 54 it is given what something else hands out", createdHere: false),
        Verify.UnownedContainerReturned(3, "C.FromAnOutArgumentLater()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 61 it is passed to 'C.TryGet(out List<Guard>)'"),
        Verify.UnownedContainerReturned(4, "C.FromAPattern(object)", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(5, "C.FromAForEach()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 75 it is given what something else holds", createdHere: false),
        Verify.UnownedContainerReturned(6, "C.AlsoInAField()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 85 it is assigned to something else"),
        Verify.UnownedContainerReturned(7, "C.AlsoInAnElement()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 92 it is assigned to something else"),
        Verify.UnownedContainerReturned(8, "C.AlsoInAnotherVariable()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 99 it is assigned to something else"),
        Verify.UnownedContainerReturned(9, "C.TheOtherVariable()", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(10, "C.Captured()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 113 a lambda or a local function uses it"));

    [TestMethod]
    public Task Iterator_MustNotYieldTheSameContainerTwice() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        class C
        {
            // An iterator goes on after it has yielded, and its locals with it.
            IEnumerable<List<Guard>> Twice(RwLock rwLock)
            {
                var list = rwLock.CreateGuards();
                yield return {|#0:list|};
                yield return {|#1:list|};
            }

            IEnumerable<List<Guard>> InALoop(RwLock rwLock)
            {
                var list = rwLock.CreateGuards();

                while (true)
                    yield return {|#2:list|};
            }

            IEnumerable<List<Guard>> InANestedLoop(RwLock rwLock)
            {
                for (int i = 0; i < 2; i++)
                {
                    var list = rwLock.CreateGuards();

                    for (int j = 0; j < 2; j++)
                        yield return {|#3:list|};
                }
            }

            // A local that is yielded once, or that every turn of the loop declares anew, is handed over.
            IEnumerable<List<Guard>> Once(RwLock rwLock)
            {
                var list = rwLock.CreateGuards();
                yield return list;
            }

            IEnumerable<List<Guard>> AnewEachTime(RwLock rwLock)
            {
                for (int i = 0; i < 2; i++)
                {
                    var list = rwLock.CreateGuards();
                    list.Add(rwLock.Enter());
                    yield return list;
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UnownedContainerReturned(0, "C.Twice(RwLock)", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(1, "C.Twice(RwLock)", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(2, "C.InALoop(RwLock)", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(3, "C.InANestedLoop(RwLock)", "System.Collections.Generic.List<Guard>"));

    [TestMethod]
    public Task Borrowed_TheMethodKeepsWhatItReturnsAndCallersTreatItAsStored() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Runtime.InteropServices;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        class Holder
        {
            private readonly Dictionary<string, List<Guard>> _byKey = new();
            private Task<Guard> _cached = null!;

            [return: Borrowed]
            public List<Guard> GetGuardsFor(string key) => _byKey[key];

            [return: Borrowed]
            public Task<Guard> GetCachedAsync() => _cached;
        }

        class C
        {
            async Task M(Holder holder, RwLock rwLock)
            {
                // The result is stored, as what a property returns is.
                foreach (Guard guard in {|#0:holder.GetGuardsFor("a")|}) { }
                Guard first = {|#1:holder.GetGuardsFor("a")[0]|};
                Guard a = {|#2:await holder.GetCachedAsync()|};
                Guard b = {|#3:holder.GetCachedAsync().Result|};

                // It can be used in place.
                holder.GetGuardsFor("a").Add(rwLock.Enter());
                int count = holder.GetGuardsFor("a").Count;
                List<Guard> list = holder.GetGuardsFor("a");

                foreach (ref Guard guard in CollectionsMarshal.AsSpan(holder.GetGuardsFor("a")))
                    guard.Dispose();
            }

            // A method that passes it on does not own it either, unless it says the same of itself.
            List<Guard> PassesOn(Holder holder) => {|#4:holder.GetGuardsFor("a")|};

            [return: Borrowed]
            List<Guard> PassesOnAsBorrowed(Holder holder) => holder.GetGuardsFor("a");
        }

        {{Sources.Guard}}
        """,
        Verify.ForEachOverStoredList(0, "Guard", "guard", "list"),
        Verify.ReadFromStoredContainer(1, "'List<T>.this[int]'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(2, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(3, "'Task<TResult>.Result'", "Guard", canBeMoved: false),
        Verify.UnownedContainerReturned(4, "C.PassesOn(Holder)", "System.Collections.Generic.List<Guard>"));

    [TestMethod]
    public Task Borrowed_OfAReferencedProjectIsHonoured()
    {
        var test = new Verify.Test {
            TestCode = """
                class C
                {
                    void M(Library.Holder holder)
                    {
                        Guard stored = {|#0:holder.GetGuards()[0]|};
                        Guard handedOver = holder.CreateGuards()[0];
                    }
                }
                """,
        };

        var library = test.TestState.AdditionalProjects["Library"];
        library.AdditionalReferences.Add(typeof(NonCopyableAttribute).Assembly);
        library.Sources.Add("""
            using System.Collections.Generic;

            // The project is built with the analyzer.
            [assembly: System.Reflection.AssemblyMetadata("Singulink.CodeAnalysis.Analyzed", "1")]

            namespace Singulink.CodeAnalysis
            {
                // The library declares the attribute itself: it is recognised by its full name.
                [System.AttributeUsage(System.AttributeTargets.ReturnValue)]
                internal sealed class BorrowedAttribute : System.Attribute { }
            }

            namespace Library
            {
                using Singulink.CodeAnalysis;

                public sealed class Holder
                {
                    private readonly List<Guard> _guards = new();

                    [return: Borrowed]
                    public List<Guard> GetGuards() => _guards;

                    public List<Guard> CreateGuards() => new();
                }
            }
            """);
        library.Sources.Add(Sources.Guard);

        test.TestState.AdditionalProjectReferences.Add("Library");
        test.ExpectedDiagnostics.Add(Verify.ReadFromStoredContainer(0, "'List<T>.this[int]'", "Guard", canBeMoved: false));

        return test.RunAsync();
    }

    [TestMethod]
    public Task Borrowed_MustNotBeAddedWhereTheBaseMemberHasNone() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        abstract class Base
        {
            public abstract List<Guard> Get();

            [return: Borrowed]
            public abstract List<Guard> GetBorrowed();
        }

        class Derived : Base
        {
            private readonly List<Guard> _list = new();

            // A caller through the base member takes the result for a new value.
            [return: Borrowed]
            public override List<Guard> {|#0:Get|}() => _list;

            // The other way round only makes the caller more careful.
            public override List<Guard> GetBorrowed() => new();
        }

        interface IGet { List<Guard> Get(); }

        class Implicit : IGet
        {
            private readonly List<Guard> _list = new();

            [return: Borrowed]
            public List<Guard> {|#1:Get|}() => _list;
        }

        class Explicit : IGet
        {
            private readonly List<Guard> _list = new();

            [return: Borrowed]
            List<Guard> IGet.{|#2:Get|}() => _list;
        }

        class Keeps
        {
            protected readonly List<Guard> _list = new();

            [return: Borrowed]
            public List<Guard> Get() => _list;
        }

        class {|#3:Brings|} : Keeps, IGet { }

        class Again : Implicit, IGet
        {
            [return: Borrowed]
            public new List<Guard> {|#4:Get|}() => null!;
        }

        // A delegate hands out what its method returns, so its type has to say the same.
        delegate List<Guard> Getter();

        [return: Borrowed]
        delegate List<Guard> BorrowingGetter();

        class C
        {
            private readonly List<Guard> _list = new();

            void M(Keeps keeps)
            {
                Getter getter = {|#5:keeps.Get|};
                Func<List<Guard>> lambda = {|#6:[return: Borrowed] () => _list|};

                BorrowingGetter borrowing = keeps.Get;
                BorrowingGetter borrowingLambda = [return: Borrowed] () => _list;
                Guard guard = {|#7:borrowing()[0]|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.BorrowedNotOnBaseMember(0, "Derived.Get()", "Base.Get()"),
        Verify.BorrowedNotOnBaseMember(1, "Implicit.Get()", "IGet.Get()"),
        Verify.BorrowedNotOnBaseMember(2, "Explicit.IGet.Get()", "IGet.Get()"),
        Verify.BorrowedNotOnBaseMember(3, "Keeps.Get()", "IGet.Get()"),
        Verify.BorrowedNotOnBaseMember(4, "Again.Get()", "IGet.Get()"),
        Verify.BorrowedNotOnDelegate(5, "Keeps.Get()", "Getter"),
        Verify.BorrowedNotOnDelegate(6, "lambda expression", "System.Func<System.Collections.Generic.List<Guard>>"),
        Verify.ReadFromStoredContainer(7, "'List<T>.this[int]'", "Guard", canBeMoved: false));
}
