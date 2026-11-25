using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using PrefixClassName.MsTest;
using Singulink.CodeAnalysis.Analyzers;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the marker 'borrowswhen' of a symbol list entry: a member that keeps nothing of a value that it is given by value in the
/// outcomes that the marker lists, and has taken it in the others. Where its result goes straight into a condition, the hand-off is
/// followed only on the branch where the member has taken the value, and a use on the other branch is no use after the hand-off.
/// </summary>
/// <remarks>
/// Docs: DESIGN.md, Last Use and Members That Take a Value Only Where They Say So, and NonCopyableSymbols.evidence.md, Members That
/// Keep Nothing of a Value in Some Outcomes.
/// </remarks>
[PrefixTestClass]
public class BorrowsWhenTests
{
    private const string TryWrite = "'Writer<Guard>.TryWrite(Guard)' received its value";

    // The reason that a line with an empty outcome of 'borrowswhen' gets.
    private const string BorrowsWhenOutcomes =
        "'borrowswhen' takes one or more outcomes, separated by commas, as in 'borrowswhen=false'; the outcomes are 'true' and 'false'";

    // The reasons of the lines of a method that the marker does not fit.
    private const string BothOutcomes =
        "'borrowswhen' lists 'true' or 'false', not both; for a member that keeps nothing of the value whatever it returns, write 'borrows'";

    private const string ResultIsForABoolean = "the marker 'borrowswhen' is for a method that returns bool, Task<bool> or ValueTask<bool>";

    private const string CalledByItsName =
        "the marker 'borrowswhen' is for a method that is called by its name, not for a constructor, an operator, a conversion or an accessor";

    private const string NeedsAParameter =
        "the marker 'borrowswhen' is for a method with a parameter that it takes by value, of a struct type or of a type parameter that the entry covers";

    // A generic writer of the project, whose members the list file names, and a method that is not generic.
    private const string Writer = """
        public class Writer<T>
        {
            public bool TryWrite(T item) => true;
            public bool TryReject(T item) => false;
            public bool Write(T item) => true;
            public System.Threading.Tasks.ValueTask<bool> WaitToWriteAsync() => default;
            public System.Threading.Tasks.Task<bool> TryWriteAsync(T item) => null!;
            public System.Threading.Tasks.ValueTask<bool> TryWriteValueAsync(T item) => default;
        }

        public class Map<TKey, TValue>
        {
            public bool TryAdd(TKey key, TValue value) => true;
            public bool TryPut(TKey key, TValue value) => true;
        }

        public static class Pool
        {
            public static bool TryReturn(Guard guard) => true;
        }
        """;

    private const string ListFile = """
        M:Writer`1.#ctor
        M:Writer`1.TryWrite(`0);borrowswhen=false
        M:Writer`1.TryReject(`0);borrowswhen=true
        M:Writer`1.Write(`0)
        M:Writer`1.WaitToWriteAsync
        M:Writer`1.TryWriteAsync(`0);borrowswhen=false
        M:Writer`1.TryWriteValueAsync(`0);borrowswhen=false
        M:Map`2.TryAdd(`0,`1);borrowswhen=false;for=TValue
        M:Map`2.TryPut(`0,`1);borrowswhen=false;for=TKey
        M:Pool.TryReturn(Guard);borrowswhen=false
        """;

    // The two shapes that the marker is for: hand the value over, and dispose of it where the member did not take it.
    private const string Examples = $$"""
        using System.Threading.Tasks;

        class C
        {
            void DisposeWhereNotWritten(Writer<Guard> writer, Guard lease)
            {
                if (!writer.TryWrite(lease))
                    lease.Dispose();
            }

            Writer<Guard> _writer = new();

            async Task WriteOrDisposeAsync(Guard lease)
            {
                while (await _writer.WaitToWriteAsync())
                {
                    if (_writer.TryWrite(lease))
                        return;
                }

                lease.Dispose();   // the channel closed
            }
        }

        {{Writer}}

        {{Sources.Guard}}
        """;

    [TestMethod]
    public Task Examples_AreClean() => Verify.AnalyzerAsync(Examples, ListFile);

    [TestMethod]
    public Task Examples_AreCleanUnderTheUpdatedRules() => Verify.UpdatedRulesAsync(Examples, ListFile);

    // Without the marker, the use where the member did not take the value is a use after the hand-off.
    [TestMethod]
    public Task Examples_WithoutTheMarkerAreReported() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;

        class C
        {
            void DisposeWhereNotWritten(Writer<Guard> writer, Guard lease)
            {
                if (!writer.Write({|#1:lease|}))
                    {|#0:lease|}.Dispose();
            }

            Writer<Guard> _writer = new();

            async Task WriteOrDisposeAsync(Guard lease)
            {
                while (await _writer.WaitToWriteAsync())
                {
                    if (_writer.Write({|#3:lease|}))
                        return;
                }

                {|#2:lease|}.Dispose();
            }
        }

        {{Writer}}

        {{Sources.Guard}}
        """,
        ListFile,
        Verify.UsedAfterHandOff(0, "lease", "Guard", "'Writer<Guard>.Write(Guard)' received its value", 1),
        Verify.CopyOfStoredValue(3, "'Writer<Guard>.Write(Guard)' receives", "Guard"),
        Verify.UsedAfterHandOff(2, "lease", "Guard", "'Writer<Guard>.Write(Guard)' received its value", 3));

    // A use on the branch where the member has taken the value is a use after the hand-off; one on the other branch is not.
    [TestMethod]
    public Task Branch_WhereTheValueWasTakenIsReportedAndTheOtherIsNot() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            void TakenWhereTrue(Writer<Guard> writer, Guard guard)
            {
                if (writer.TryWrite({|#1:guard|}))
                    {|#0:guard|}.Dispose();
                else
                    guard.Dispose();
            }

            void TakenWhereFalse(Writer<Guard> writer, Guard guard)
            {
                if (writer.TryReject({|#3:guard|}))
                    guard.Dispose();
                else
                    {|#2:guard|}.Dispose();
            }

            void TakenWhereFalseClean(Writer<Guard> writer, Guard guard)
            {
                if (writer.TryReject(guard))
                    guard.Dispose();
            }

            // On the branch where the member did not take it, the value can be handed on again, and that is its last use there.
            void HandedOnWhereNotTaken(Writer<Guard> writer, Writer<Guard> other, Guard guard)
            {
                if (!writer.TryWrite(guard))
                    other.Write(guard);
            }
        }

        {{Writer}}

        {{Sources.Guard}}
        """,
        ListFile,
        Verify.UsedAfterHandOff(0, "guard", "Guard", TryWrite, 1),
        Verify.UsedAfterHandOff(2, "guard", "Guard", "'Writer<Guard>.TryReject(Guard)' received its value", 3));

    [TestMethod]
    public Task Branch_WhereTheValueWasTakenIsAnErrorUnderTheUpdatedRules() => Verify.UpdatedRulesAsync(
        $$"""
        class C
        {
            void TakenWhereTrue(Writer<Guard> writer, Guard guard)
            {
                if (writer.TryWrite({|#1:guard|}))
                    {|#0:guard|}.Dispose();
                else
                    guard.Dispose();
            }
        }

        {{Writer}}

        {{Sources.Guard}}
        """,
        ListFile,
        Verify.UsedAfterHandOff(0, "guard", "Guard", TryWrite, 1).AsError());

    // '&&', '||', '?:' and '!' are split into branches on the call, in a condition and in a 'return'.
    [TestMethod]
    public Task Operators_OfConditionsAreFollowed() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            static bool Release(Guard guard) { guard.Dispose(); return false; }

            void And(Writer<Guard> writer, Guard guard, bool ready)
            {
                if (ready && writer.TryWrite(guard))
                    return;

                guard.Dispose();
            }

            void Or(Writer<Guard> writer, Guard guard)
            {
                if (writer.TryWrite(guard) || Release(guard))
                    return;
            }

            bool ReturnOr(Writer<Guard> writer, Guard guard) => writer.TryWrite(guard) || Release(guard);

            bool ReturnAndNot(Writer<Guard> writer, Guard guard) => !writer.TryWrite(guard) && Release(guard);

            int Conditional(Writer<Guard> writer, Guard guard) => writer.TryWrite(guard) ? 1 : (Release(guard) ? 2 : 3);

            void InConditional(Writer<Guard> writer, Guard guard, bool ready)
            {
                if (ready ? writer.TryWrite(guard) : false)
                    return;

                guard.Dispose();
            }

            void NotTwice(Writer<Guard> writer, Guard guard)
            {
                if (!!writer.TryWrite(guard))
                    return;

                guard.Dispose();
            }

            // The branch where the member took the value goes on to the right operand.
            void OrAfterTaken(Writer<Guard> writer, Guard guard, bool other)
            {
                if (!writer.TryWrite({|#1:guard|}) || other)
                    {|#0:guard|}.Dispose();
            }
        }

        {{Writer}}

        {{Sources.Guard}}
        """,
        ListFile,
        Verify.UsedAfterHandOff(0, "guard", "Guard", TryWrite, 1));

    // A getter in an operand of '&&', '||' or '?:' that gives a value runs on the branch of its operand only. The graph captures
    // what it gives, and the use is where the capture is made, not where the value of the operator is used after the branches meet.
    [TestMethod]
    public Task Operators_AGetterInAValueRunsOnItsBranchOnly() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            bool ReturnAnd(Writer<Lease> writer, Lease lease) => !writer.TryWrite(lease) && lease.IsOpen;

            bool LocalAnd(Writer<Lease> writer, Lease lease)
            {
                bool open = !writer.TryWrite(lease) && lease.IsOpen;
                return open;
            }

            bool ReturnConditional(Writer<Lease> writer, Lease lease) => !writer.TryWrite(lease) ? lease.IsOpen : false;

            int ReturnConditionalOfAnotherType(Writer<Lease> writer, Lease lease) => !writer.TryWrite(lease) ? lease.Uses : 0;

            bool OrWhereTaken(Writer<Lease> writer, Lease lease) => !writer.TryWrite({|#1:lease|}) || {|#0:lease|}.IsOpen;

            bool ConditionalWhereTaken(Writer<Lease> writer, Lease lease) => writer.TryWrite({|#3:lease|}) ? {|#2:lease|}.IsOpen : false;

            bool AfterTheBranchesMeet(Writer<Lease> writer, Lease lease) => (!writer.TryWrite({|#5:lease|}) && lease.IsOpen) || {|#4:lease|}.IsOpen;
        }

        [Singulink.CodeAnalysis.NonCopyable]
        public struct Lease
        {
            public int Count;
            public int Uses => Count;
            public bool IsOpen => Count > 0;
        }

        {{Writer}}

        {{Sources.Guard}}
        """,
        ListFile,
        Verify.UsedAfterHandOff(0, "lease", "Lease", "'Writer<Lease>.TryWrite(Lease)' received its value", 1),
        Verify.UsedAfterHandOff(2, "lease", "Lease", "'Writer<Lease>.TryWrite(Lease)' received its value", 3),
        Verify.UsedAfterHandOff(4, "lease", "Lease", "'Writer<Lease>.TryWrite(Lease)' received its value", 5));

    // A comparison with the constant true or false, and an 'is' pattern of either constant or of 'not' and either.
    [TestMethod]
    public Task Comparisons_WithAConstantAreFollowed() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            void EqualsTrue(Writer<Guard> writer, Guard guard)
            {
                if (writer.TryWrite(guard) == true)
                    return;

                guard.Dispose();
            }

            void EqualsFalse(Writer<Guard> writer, Guard guard)
            {
                if (writer.TryWrite(guard) == false)
                    guard.Dispose();
            }

            void TrueEquals(Writer<Guard> writer, Guard guard)
            {
                if (false == writer.TryWrite(guard))
                    guard.Dispose();
            }

            void NotEqualsTrue(Writer<Guard> writer, Guard guard)
            {
                if (writer.TryWrite(guard) != true)
                    guard.Dispose();
            }

            void IsTrue(Writer<Guard> writer, Guard guard)
            {
                if (writer.TryWrite(guard) is true)
                    return;

                guard.Dispose();
            }

            void IsFalse(Writer<Guard> writer, Guard guard)
            {
                if (writer.TryWrite(guard) is false)
                    guard.Dispose();
            }

            void IsNotTrue(Writer<Guard> writer, Guard guard)
            {
                if (writer.TryWrite(guard) is not true)
                    guard.Dispose();
            }

            // The polarity is followed: here the member has taken the value where the condition is false.
            void EqualsFalseTheOtherWay(Writer<Guard> writer, Guard guard)
            {
                if (writer.TryWrite({|#1:guard|}) == false)
                    return;

                {|#0:guard|}.Dispose();
            }

            void IsNotFalse(Writer<Guard> writer, Guard guard)
            {
                if (writer.TryWrite({|#3:guard|}) is not false)
                    {|#2:guard|}.Dispose();
            }
        }

        {{Writer}}

        {{Sources.Guard}}
        """,
        ListFile,
        Verify.UsedAfterHandOff(0, "guard", "Guard", TryWrite, 1),
        Verify.UsedAfterHandOff(2, "guard", "Guard", TryWrite, 3));

    // The condition of a 'do', a 'while' and a 'for' loop: the branch where the member did not take the value goes round again, to the
    // same call, which is then given the value that the caller still holds.
    [TestMethod]
    public Task Loops_TheirConditionsAreFollowed() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            void Do(Writer<Guard> writer, Guard guard)
            {
                do { }
                while (!writer.TryWrite(guard));
            }

            void While(Writer<Guard> writer, Guard guard)
            {
                while (!writer.TryWrite(guard)) { }
            }

            void For(Writer<Guard> writer, Guard guard)
            {
                for (int i = 0; !writer.TryWrite(guard); i++) { }
            }

            void ForWithUseInside(Writer<Guard> writer, Guard guard)
            {
                for (int i = 0; !writer.TryWrite(guard); i++)
                {
                    if (i == 3)
                    {
                        guard.Dispose();
                        return;
                    }
                }
            }

            // The branch where the member took the value goes round again: the next call is given a copy.
            void WhileTaken(Writer<Guard> writer, Guard guard)
            {
                while (writer.TryWrite({|#0:guard|})) { }
            }
        }

        {{Writer}}

        {{Sources.Guard}}
        """,
        ListFile,
        Verify.CopyOfStoredValue(0, "'Writer<Guard>.TryWrite(Guard)' receives", "Guard"));

    // A result that is stored or passed on before it decides anything is judged as the result of any other hand-off.
    [TestMethod]
    public Task Result_ThatIsStoredOrPassedOnGetsTheNormalJudgement() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            static bool Check(bool value) => value;

            void Stored(Writer<Guard> writer, Guard guard)
            {
                bool written = writer.TryWrite({|#1:guard|});

                if (!written)
                    {|#0:guard|}.Dispose();
            }

            void PassedOn(Writer<Guard> writer, Guard guard)
            {
                if (!Check(writer.TryWrite({|#3:guard|})))
                    {|#2:guard|}.Dispose();
            }

            void NotShortCircuited(Writer<Guard> writer, Guard guard, bool ready)
            {
                if (!(writer.TryWrite({|#5:guard|}) & ready))
                    {|#4:guard|}.Dispose();
            }
        }

        {{Writer}}

        {{Sources.Guard}}
        """,
        ListFile,
        Verify.UsedAfterHandOff(0, "guard", "Guard", TryWrite, 1),
        Verify.UsedAfterHandOff(2, "guard", "Guard", TryWrite, 3),
        Verify.UsedAfterHandOff(4, "guard", "Guard", TryWrite, 5));

    // A member that throws may have taken the value, so a 'catch' or a 'finally' that uses the variable after the hand-off is reported,
    // after a member with 'borrowswhen=false' as after one with no marker. The marker gives the caller the branch where the member
    // returned false, and nothing where it threw.
    [TestMethod]
    public Task Handlers_ThatUseTheVariableAfterTheHandOffAreReported() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            void CatchAfterBorrowsWhen(Writer<Guard> writer, Guard guard)
            {
                try
                {
                    if (!writer.TryWrite({|#1:guard|}))
                        guard.Dispose();
                }
                catch
                {
                    {|#0:guard|}.Dispose();
                    throw;
                }
            }

            void FinallyAfterBorrowsWhen(Writer<Guard> writer, Guard guard)
            {
                try
                {
                    if (writer.TryWrite({|#3:guard|}))
                        return;
                }
                finally
                {
                    {|#2:guard|}.Value = 1;
                }
            }

            void CatchAfterNoMarker(Writer<Guard> writer, Guard guard)
            {
                try
                {
                    writer.Write({|#5:guard|});
                }
                catch
                {
                    {|#4:guard|}.Dispose();
                    throw;
                }
            }

            void FinallyAfterNoMarker(Writer<Guard> writer, Guard guard)
            {
                try
                {
                    writer.Write({|#7:guard|});
                }
                finally
                {
                    {|#6:guard|}.Value = 1;
                }
            }
        }

        {{Writer}}

        {{Sources.Guard}}
        """,
        ListFile,
        Verify.UsedAfterHandOffInHandler(0, "guard", "Guard", TryWrite, 1),
        Verify.UsedAfterHandOffInHandler(2, "guard", "Guard", TryWrite, 3),
        Verify.UsedAfterHandOffInHandler(4, "guard", "Guard", "'Writer<Guard>.Write(Guard)' received its value", 5),
        Verify.UsedAfterHandOffInHandler(6, "guard", "Guard", "'Writer<Guard>.Write(Guard)' received its value", 7));

    // A field, an element and a 'ref' parameter are never moved from: handing one over is a copy, as before.
    [TestMethod]
    public Task StoredValues_AreCopiedAsBefore() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            Guard _field;
            Guard[] _array = new Guard[1];

            void Field(Writer<Guard> writer)
            {
                if (!writer.TryWrite({|#0:_field|}))
                    _field.Dispose();
            }

            void Element(Writer<Guard> writer)
            {
                if (!writer.TryWrite({|#1:_array[0]|}))
                    _array[0].Dispose();
            }

            void RefParameter(Writer<Guard> writer, ref Guard guard)
            {
                if (!writer.TryWrite({|#2:guard|}))
                    guard.Dispose();
            }
        }

        {{Writer}}

        {{Sources.Guard}}
        """,
        ListFile,
        Verify.CopyOfStoredValue(0, "'Writer<Guard>.TryWrite(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(1, "'Writer<Guard>.TryWrite(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(2, "'Writer<Guard>.TryWrite(Guard)' receives", "Guard"));

    // An entry with 'for=' covers the parameters of the type parameters that it names: a member that is used with a non-copyable type
    // bound to another one is not listed for that use, and its hand-off is judged as any other.
    [TestMethod]
    public Task GenericMember_IsNarrowedByFor() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            void Covered(Map<int, Guard> map, Guard guard)
            {
                if (!map.TryAdd(1, guard))
                    guard.Dispose();
            }

            void NotCovered(Map<int, Guard> map, Guard guard)
            {
                if (!{|#0:map.TryPut(1, {|#2:guard|})|})
                    {|#1:guard|}.Dispose();
            }
        }

        {{Writer}}

        {{Sources.Guard}}
        """,
        ListFile,
        Verify.MemberListedForOtherTypeParameter(0, "Map<TKey, TValue>.TryPut(TKey, TValue)", "Guard", "TValue", "TKey"),
        Verify.UsedAfterHandOff(1, "guard", "Guard", "'Map<int, Guard>.TryPut(int, Guard)' received its value", 2));

    // A method that is not generic, of the project itself, takes its entry from the project's file.
    [TestMethod]
    public Task MemberOfTheProject_TakesItsEntryFromTheProjectsFile() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            void Return(Guard guard)
            {
                if (!Pool.TryReturn(guard))
                    guard.Dispose();
            }
        }

        {{Writer}}

        {{Sources.Guard}}
        """,
        ListFile);

    // A member of a library that was not built with the analyzer takes its entry from the project's file as well.
    [TestMethod]
    public async Task MemberOfALibrary_TakesItsEntryFromTheProjectsFile()
    {
        var test = new Verify.Test {
            TestCode = $$"""
                class C
                {
                    void DisposeWhereNotWritten(ThirdParty.Sink<Guard> sink, Guard guard)
                    {
                        if (!sink.TryPost(guard))
                            guard.Dispose();
                    }

                    void TakenWhereTrue(ThirdParty.Sink<Guard> sink, Guard guard)
                    {
                        if (sink.TryPost({|#1:guard|}))
                            {|#0:guard|}.Dispose();
                    }
                }

                {{Sources.Guard}}
                """,
        };

        test.TestState.AdditionalReferences.Add(await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies,
            """
            namespace ThirdParty;

            public class Sink<T>
            {
                public bool TryPost(T item) => true;
            }
            """,
            analyzed: false));

        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "M:ThirdParty.Sink`1.TryPost(`0);borrowswhen=false"));
        test.ExpectedDiagnostics.Add(Verify.UsedAfterHandOff(0, "guard", "Guard", "'Sink<Guard>.TryPost(Guard)' received its value", 1));
        await test.RunAsync();
    }

    // A method that returns a task of a bool goes by what awaiting the task gives, also behind ConfigureAwait.
    [TestMethod]
    public Task TaskOfBoolean_GoesByTheAwaitedResult() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;

        class C
        {
            async Task Task(Writer<Guard> writer, Guard guard)
            {
                if (!await writer.TryWriteAsync(guard))
                    guard.Dispose();
            }

            async Task ValueTask(Writer<Guard> writer, Guard guard)
            {
                if (await writer.TryWriteValueAsync(guard).ConfigureAwait(false))
                    return;

                guard.Dispose();
            }

            async Task Taken(Writer<Guard> writer, Guard guard)
            {
                if (await writer.TryWriteAsync({|#1:guard|}).ConfigureAwait(false))
                    {|#0:guard|}.Dispose();
            }

            // A task that is stored before it is awaited is judged as any other hand-off.
            async Task Stored(Writer<Guard> writer, Guard guard)
            {
                var writing = writer.TryWriteAsync({|#3:guard|});

                if (!await writing)
                    {|#2:guard|}.Dispose();
            }
        }

        {{Writer}}

        {{Sources.Guard}}
        """,
        ListFile,
        Verify.UsedAfterHandOff(0, "guard", "Guard", "'Writer<Guard>.TryWriteAsync(Guard)' received its value", 1),
        Verify.UsedAfterHandOff(2, "guard", "Guard", "'Writer<Guard>.TryWriteAsync(Guard)' received its value", 3));

    // The line of each form that the parser takes, and of each that it does not, with the reason. The outcomes come in any order, each
    // once. Each needs a method that returns a bool or a task of one and is called by its name. An outcome that the analyzer does not
    // know is left out of a line that is otherwise taken, with a warning in the project's own file: 'throws' is one, and a line of a
    // constructor whose every outcome is unknown is a line with no marker.
    [TestMethod]
    public Task ListFile_TakesTheFormsThatFitAndReportsTheOthers() => Verify.AnalyzerAsync(
        $$"""
        class C { }

        public class Box<T>
        {
            public Box(T item) { }
            public bool TryPut(T item) => true;
            public int Put(T item) => 0;
            public bool Check() => true;
            public bool TryRef(ref T item) => true;
            public bool TryWrap(Box<T> box) => true;
            public int Count => 0;
            public static bool operator ==(Box<T> left, Box<T> right) => true;
            public static bool operator !=(Box<T> left, Box<T> right) => false;
            public override bool Equals(object other) => false;
            public override int GetHashCode() => 0;
            public System.Threading.Tasks.Task<bool> TryPutAsync(T item) => null!;
            public System.Threading.Tasks.ValueTask<bool> TryPutValueAsync(T item) => default;
            public System.Threading.Tasks.Task<int> PutAsync(T item) => null!;
        }

        public class Store<TKey, TValue> where TKey : class
        {
            public bool TryPut(TKey key, int value) => true;
        }

        {{Sources.Guard}}
        """,
        """
        M:Box`1.TryPut(`0);borrowswhen=false
        M:Box`1.TryPut(`0);borrowswhen=true
        M:Box`1.TryPut(`0) ; borrowswhen = false ; for = T
        M:Box`1.TryPutAsync(`0);borrowswhen=false
        M:Box`1.TryPutValueAsync(`0);borrowswhen=true
        M:Box`1.TryPut(`0);borrowswhen
        M:Box`1.TryPut(`0);borrowswhen=
        M:Box`1.TryPut(`0);borrowswhen=false,
        M:Box`1.TryPut(`0);borrowswhen=false,,true
        M:Box`1.TryPut(`0);borrowswhen=false,false
        M:Box`1.TryPut(`0);borrowswhen=true , true
        M:Box`1.TryPut(`0);borrowswhen=true,false
        M:Box`1.TryPut(`0);borrowswhen=false,true,later
        M:Box`1.TryPut(`0);borrowswhen=false;new
        M:Box`1.TryPut(`0);borrowswhen=false;borrowswhen=true
        M:Box`1.TryPut(`0);borrowswhen=maybe;borrows
        M:Box`1.TryPut(`0);for=T;borrowswhen=false
        T:Box`1;borrowswhen=false
        P:Box`1.Count;borrowswhen=false
        M:Box`1.Put(`0);borrowswhen=false
        M:Box`1.PutAsync(`0);borrowswhen=true
        M:Box`1.op_Equality(Box{`0},Box{`0});borrowswhen=false
        M:Box`1.#ctor(`0);borrowswhen=false
        M:Box`1.Check;borrowswhen=false
        M:Box`1.TryRef(`0@);borrowswhen=false
        M:Box`1.TryWrap(Box{`0});borrowswhen=false
        M:Store`2.TryPut(`0,System.Int32);borrowswhen=false;for=TKey
        M:Box`1.TryPut(`0);borrowswhen=false;for=U
        M:Box`1.TryPut(`0);takeswhen=true
        M:Box`1.TryPut(`0);borrowswhen=returns
        M:Box`1.TryPut(`0);borrowswhen=maybe
        M:Box`1.TryPut(`0);borrowswhen=false, never , later
        M:Box`1.TryPut(`0);borrowswhen=false,throws
        M:Box`1.#ctor(`0);borrowswhen=throws
        """,
        Verify.SafeListLine(6, 1, 31, BorrowsWhenOutcomes),
        Verify.SafeListLine(7, 1, 32, BorrowsWhenOutcomes),
        Verify.SafeListLine(8, 1, 38, BorrowsWhenOutcomes),
        Verify.SafeListLine(9, 1, 43, BorrowsWhenOutcomes),
        Verify.SafeListLine(10, 1, 43, "'borrowswhen' lists the outcome 'false' twice"),
        Verify.SafeListLine(11, 1, 43, "'borrowswhen' lists the outcome 'true' twice"),
        Verify.SafeListLine(12, 1, 42, BothOutcomes),
        Verify.SafeListLine(13, 1, 48, BothOutcomes),
        Verify.SafeListLine(14, 1, 41, "an entry has one marker, which 'for=' can follow"),
        Verify.SafeListLine(15, 1, 54, "an entry has one marker, which 'for=' can follow"),
        Verify.SafeListLine(16, 1, 45, "an entry has one marker, which 'for=' can follow"),
        Verify.SafeListLine(17, 1, 43, "'for=' is the last part of an entry"),
        Verify.SafeListLine(18, 1, 26, "the marker 'borrowswhen' is for a method; a type takes 'view', 'noncopyable', 'copyable', 'erasable' or 'clones'"),
        Verify.SafeListLine(19, 1, 32, "the marker 'borrowswhen' is for a method"),
        Verify.SafeListLine(20, 1, 34, ResultIsForABoolean),
        Verify.SafeListLine(21, 1, 38, ResultIsForABoolean),
        Verify.SafeListLine(22, 1, 55, CalledByItsName),
        Verify.SafeListLine(23, 1, 36, CalledByItsName),
        Verify.SafeListLine(24, 1, 32, NeedsAParameter),
        Verify.SafeListLine(25, 1, 38, NeedsAParameter),
        Verify.SafeListLine(26, 1, 43, NeedsAParameter),
        Verify.SafeListLine(27, 1, 61, NeedsAParameter),
        Verify.SafeListLine(28, 1, 43, "'U' is not a type parameter of the member or of a type that contains it"),
        Verify.SafeListLine(29, 1, 34, "unknown marker 'takeswhen=true'; the markers are 'new', 'borrows', 'drains', 'owned', 'borrowswhen', 'view', 'noncopyable', 'copyable', 'erasable' and 'clones', and 'trusted' for an assembly"),
        Verify.SafeListUnknownOutcome(30, 39, "returns"),
        Verify.SafeListUnknownOutcome(31, 37, "maybe"),
        Verify.SafeListUnknownOutcome(32, 52, "never"),
        Verify.SafeListUnknownOutcome(32, 52, "later"),
        Verify.SafeListUnknownOutcome(33, 44, "throws"),
        Verify.SafeListUnknownOutcome(34, 37, "throws"));

    // A line that cannot be parsed is reported and left out in a file that came in a package as well, as in the project's own: the member
    // is then off the list, and its use is reported as for any member that is not on it.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task LineThatCannotBeParsed_IsReportedAndLeftOutInAFileOfAPackageAsWell(bool inPackage)
    {
        const string Line = "M:Writer`1.TryWrite(`0);borrowswhen=";
        string path = inPackage ? "/packages/lib/1.0.0/build/NonCopyableSymbols.Lib.txt" : Verify.ListFileName;
        var test = new Verify.Test {
            TestCode = $$"""
                class C
                {
                    void Write(Writer<Guard> writer, Guard guard) => {|#0:writer.TryWrite(guard)|};
                }

                {{Writer}}

                {{Sources.Guard}}
                """,
        };

        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", "is_global = true" + "\n" + "build_property.NuGetPackageRoot = /packages/" + "\n"));
        test.TestState.AdditionalFiles.Add((path, Line));
        test.ExpectedDiagnostics.Add(Verify.UnlistedMember(0, "Writer<T>.TryWrite(T)", "Guard", "T", "M:Writer`1.TryWrite(`0)"));
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticIds.SafeListLine, DiagnosticSeverity.Warning)
                .WithSpan(path, 1, 1, 1, Line.Length + 1)
                .WithMessage("This line of the symbol list file cannot be parsed: " + BorrowsWhenOutcomes));

        await test.RunAsync();
    }

    // An outcome that the analyzer does not know is left out, so the line claims less: with 'false' beside it the line says what
    // 'borrowswhen=false' says, and alone it leaves a line with no marker, where the use after the hand-off is reported. 'throws' is
    // such an outcome: a 'catch' after the hand-off is reported either way. The project that owns the file gets a warning for the
    // outcome; a project that gets the file in a package gets none.
    [TestMethod]
    [DataRow("false,later", false, false)]
    [DataRow("false,later", true, false)]
    [DataRow("later", false, true)]
    [DataRow("later", true, true)]
    [DataRow("false,throws", false, false)]
    [DataRow("false,throws", true, false)]
    [DataRow("throws", false, true)]
    [DataRow("throws", true, true)]
    public async Task UnknownOutcome_IsLeftOutAndReportedOnlyInTheProjectsOwnFile(string outcomes, bool inPackage, bool isUseReported)
    {
        string line = $"M:Writer`1.TryWrite(`0);borrowswhen={outcomes}";
        string unknown = outcomes.Split(',').Single(outcome => outcome is not "false");
        string path = inPackage ? "/packages/lib/1.0.0/build/NonCopyableSymbols.Lib.txt" : Verify.ListFileName;
        var test = new Verify.Test {
            TestCode = $$"""
                class C
                {
                    void DisposeWhereNotWritten(Writer<Guard> writer, Guard guard)
                    {
                        if (!writer.TryWrite({|#1:guard|}))
                            {|#0:guard|}.Dispose();
                    }

                    void Catch(Writer<Guard> writer, Guard guard)
                    {
                        try
                        {
                            if (writer.TryWrite({|#3:guard|}))
                                return;
                        }
                        catch
                        {
                            {|#2:guard|}.Dispose();
                            throw;
                        }
                    }
                }

                {{Writer}}

                {{Sources.Guard}}
                """,
        };

        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", "is_global = true" + "\n" + "build_property.NuGetPackageRoot = /packages/" + "\n"));
        test.TestState.AdditionalFiles.Add((path, line));

        if (isUseReported)
            test.ExpectedDiagnostics.Add(Verify.UsedAfterHandOff(0, "guard", "Guard", TryWrite, 1));

        test.ExpectedDiagnostics.Add(Verify.UsedAfterHandOffInHandler(2, "guard", "Guard", TryWrite, 3));

        if (!inPackage)
            test.ExpectedDiagnostics.Add(Verify.SafeListUnknownOutcome(1, line.Length + 1, unknown));

        await test.RunAsync();
    }

    // The entries of the built-in framework symbol list that have the marker: each hands the value over only where it returns true.
    [TestMethod]
    public Task BuiltInList_MarksTheMembersThatTakeAValueOnlyWhereTheyReturnTrue() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        class C
        {
            void CompletionSource(TaskCompletionSource<Guard> source, Guard guard)
            {
                if (!source.TrySetResult(guard))
                    guard.Dispose();
            }

            void Dictionary(Dictionary<int, Guard> map, Guard guard)
            {
                if (!map.TryAdd(1, guard))
                    guard.Dispose();
            }

            void DictionaryLookup(Dictionary<string, Guard> map, Guard guard)
            {
                var lookup = map.GetAlternateLookup<ReadOnlySpan<char>>();

                if (!lookup.TryAdd("key".AsSpan(), guard))
                    guard.Dispose();
            }

            void ConcurrentDictionary(ConcurrentDictionary<int, Guard> map, Guard guard)
            {
                if (!map.TryAdd(1, guard))
                    guard.Dispose();
            }

            void ConcurrentDictionaryLookup(ConcurrentDictionary<string, Guard> map, Guard guard)
            {
                var lookup = map.GetAlternateLookup<ReadOnlySpan<char>>();

                if (!lookup.TryAdd("key".AsSpan(), guard))
                    guard.Dispose();
            }

            void BlockingCollection(BlockingCollection<Guard> collection, Guard guard, CancellationToken cancellationToken)
            {
                if (!collection.TryAdd(guard))
                    guard.Dispose();
            }

            void BlockingCollectionTimeSpan(BlockingCollection<Guard> collection, Guard guard)
            {
                if (!collection.TryAdd(guard, TimeSpan.FromSeconds(1)))
                    guard.Dispose();
            }

            void BlockingCollectionMilliseconds(BlockingCollection<Guard> collection, Guard guard)
            {
                if (!collection.TryAdd(guard, 1000))
                    guard.Dispose();
            }

            void BlockingCollectionCancellable(BlockingCollection<Guard> collection, Guard guard, CancellationToken cancellationToken)
            {
                if (!collection.TryAdd(guard, 1000, cancellationToken))
                    guard.Dispose();
            }

            // Where it returned true, the value is the member's.
            void Taken(Dictionary<int, Guard> map, Guard guard)
            {
                if (map.TryAdd(1, {|#1:guard|}))
                    {|#0:guard|}.Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOff(0, "guard", "Guard", "'Dictionary<int, Guard>.TryAdd(int, Guard)' received its value", 1));

    // A member of the built-in list that throws may have taken the value, with 'borrowswhen=false' or with no marker, so a 'catch' that
    // disposes the value after the hand-off is reported. The clean form does the work that can fail inside the 'try', and hands the
    // value over after it.
    [TestMethod]
    public Task BuiltInList_ACatchAfterTheHandOffIsReported() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Channels;

        class C
        {
            void Add(List<Guard> list, Guard lease)
            {
                try
                {
                    list.Add({|#1:lease|});
                }
                catch
                {
                    {|#0:lease|}.Dispose();
                    throw;
                }
            }

            void TryAdd(Dictionary<int, Guard> map, Guard lease)
            {
                try
                {
                    if (!map.TryAdd(1, {|#3:lease|}))
                        lease.Dispose();
                }
                catch
                {
                    {|#2:lease|}.Dispose();
                    throw;
                }
            }

            void TryWrite(ChannelWriter<Guard> writer, Guard lease)
            {
                try
                {
                    if (!writer.TryWrite({|#5:lease|}))
                        lease.Dispose();
                }
                catch
                {
                    {|#4:lease|}.Dispose();
                    throw;
                }
            }

            void HandOverAfterTheTry(List<Guard> list, Guard lease)
            {
                try
                {
                    Prepare();
                }
                catch
                {
                    lease.Dispose();
                    throw;
                }

                list.Add(lease);
            }

            static void Prepare() { }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOffInHandler(0, "lease", "Guard", "'List<Guard>.Add(Guard)' received its value", 1),
        Verify.UsedAfterHandOffInHandler(2, "lease", "Guard", "'Dictionary<int, Guard>.TryAdd(int, Guard)' received its value", 3),
        Verify.UsedAfterHandOffInHandler(4, "lease", "Guard", "'ChannelWriter<Guard>.TryWrite(Guard)' received its value", 5));

    // The two shapes that the marker is for, with the channels of the framework.
    private const string ChannelExamples = $$"""
        using System.Threading.Channels;
        using System.Threading.Tasks;

        class C
        {
            void DisposeWhereNotWritten(ChannelWriter<Guard> writer, Guard lease)
            {
                if (!writer.TryWrite(lease))
                    lease.Dispose();
            }

            ChannelWriter<Guard> _writer = Channel.CreateBounded<Guard>(1).Writer;

            async Task WriteOrDisposeAsync(Guard lease)
            {
                while (await _writer.WaitToWriteAsync())
                {
                    if (_writer.TryWrite(lease))
                        return;
                }

                lease.Dispose();   // the channel closed
            }
        }

        {{Sources.Guard}}
        """;

    // ChannelWriter<T>.TryWrite has 'borrowswhen=false': where it returned false it has not taken the value. No handler uses the value
    // in the two shapes, so they are clean under both rule sets.
    [TestMethod]
    public Task BuiltInList_MarksChannelWriterTryWriteBorrowsWhenFalse() => Verify.AnalyzerAsync(ChannelExamples);

    [TestMethod]
    public Task BuiltInList_MarksChannelWriterTryWriteBorrowsWhenFalseUnderTheUpdatedRules() => Verify.UpdatedRulesAsync(ChannelExamples);

    // A line of a file that says what the built-in entry says changes nothing. One that disagrees, 'borrowswhen=true', leaves the member
    // with no marker, since the entry that claims less applies, and the use where it returned false is reported; the outcome that it
    // adds gets a warning. A line with no marker takes the marker away.
    [TestMethod]
    [DataRow("M:System.Threading.Channels.ChannelWriter`1.TryWrite(`0);borrowswhen=false", false, null)]
    [DataRow("M:System.Threading.Channels.ChannelWriter`1.TryWrite(`0);borrowswhen=true", true, "the outcome 'true'")]
    [DataRow("M:System.Threading.Channels.ChannelWriter`1.TryWrite(`0)", true, null)]
    public async Task BuiltInList_ChannelWriterTryWriteMergesWithALineOfAFile(string line, bool reported, string? outcomesBeyond)
    {
        var test = new Verify.Test {
            TestCode = $$"""
                using System.Threading.Channels;

                class C
                {
                    void DisposeWhereNotWritten(ChannelWriter<Guard> writer, Guard lease)
                    {
                        if (!writer.TryWrite({|#1:lease|}))
                            {|#0:lease|}.Dispose();
                    }
                }

                {{Sources.Guard}}
                """,
        };

        test.TestState.AdditionalFiles.Add((Verify.ListFileName, line));

        if (reported)
            test.ExpectedDiagnostics.Add(Verify.UsedAfterHandOff(0, "lease", "Guard", "'ChannelWriter<Guard>.TryWrite(Guard)' received its value", 1));

        if (outcomesBeyond is not null)
            test.ExpectedDiagnostics.Add(Verify.SafeListClaimsMoreOutcomesThanBuiltIn(1, line.Length + 1, line[..line.IndexOf(';', StringComparison.Ordinal)], outcomesBeyond));

        await test.RunAsync();
    }

    // A line of a file that lists an outcome which the built-in entry of the member does not list gets a warning in the build that owns
    // the file, as a line that names a member for more type parameters does: the merge keeps the outcomes that both list. A line that
    // lists the same outcome is no claim of more, and a member that the built-in list does not name is the file's own to describe. A
    // file that came in a package gets nothing, and the line is merged all the same.
    [TestMethod]
    [DataRow("M:System.Threading.Channels.ChannelWriter`1.TryWrite(`0);borrowswhen=true", "the outcome 'true'", false)]
    [DataRow("M:System.Collections.Generic.Dictionary`2.TryAdd(`0,`1);borrowswhen=true;for=TValue", "the outcome 'true'", false)]
    [DataRow("M:System.Threading.Tasks.TaskCompletionSource`1.TrySetResult(`0);borrowswhen=false", null, false)]
    [DataRow("M:System.Collections.Generic.Dictionary`2.TryAdd(`0,`1);borrowswhen=false;for=TValue", null, false)]
    [DataRow("M:Writer`1.Write(`0);borrowswhen=false", null, false)]
    [DataRow("M:System.Threading.Channels.ChannelWriter`1.TryWrite(`0);borrowswhen=true", null, true)]
    public async Task BuiltInList_ALineThatListsMoreOutcomesGetsAWarning(string line, string? outcomesBeyond, bool inPackage)
    {
        string path = inPackage ? "/packages/lib/1.0.0/build/NonCopyableSymbols.Lib.txt" : Verify.ListFileName;
        var test = new Verify.Test {
            TestCode = $$"""
                class C
                {
                }

                {{Writer}}

                {{Sources.Guard}}
                """,
        };

        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", "is_global = true" + "\n" + "build_property.NuGetPackageRoot = /packages/" + "\n"));
        test.TestState.AdditionalFiles.Add((path, line));

        if (outcomesBeyond is not null)
            test.ExpectedDiagnostics.Add(Verify.SafeListClaimsMoreOutcomesThanBuiltIn(1, line.Length + 1, line[..line.IndexOf(';', StringComparison.Ordinal)], outcomesBeyond));

        await test.RunAsync();
    }

    // A 'borrows' line of a file for a member that the built-in list names with 'borrowswhen' or with no marker claims more than the
    // built-in entry, and the merge keeps the built-in entry, so it gets a warning in the build that owns the file. A line with 'for='
    // that covers what the built-in entry covers is the same line. These get no such warning: a line whose 'for=' leaves out a type
    // parameter that the built-in entry covers, which takes the entry away for it; a line over a built-in entry with another marker,
    // such as 'new', which it takes away; a line that repeats a built-in 'borrows'; a member that the built-in list does not name; and a
    // file that came in a package. A line that names the member for more type parameters than the built-in entry gets that warning
    // alone.
    [TestMethod]
    [DataRow("M:System.Threading.Channels.ChannelWriter`1.TryWrite(`0);borrows", "with 'borrowswhen=false'", false)]
    [DataRow("M:System.Collections.Generic.List`1.Add(`0);borrows", "with no marker", false)]
    [DataRow("M:System.Threading.Tasks.TaskCompletionSource`1.TrySetResult(`0);borrows;for=TResult", "with 'borrowswhen=false'", false)]
    [DataRow("M:System.Collections.Generic.Dictionary`2.TryAdd(`0,`1);borrows;for=TValue", "with 'borrowswhen=false'", false)]
    [DataRow("M:System.Threading.Channels.ChannelWriter`1.WriteAsync(`0,System.Threading.CancellationToken);borrows", "with no marker", false)]
    [DataRow("M:System.Collections.Generic.Dictionary`2.TryGetValue(`0,`1@);borrows;for=TValue", "with no marker", false)]
    [DataRow("M:System.Collections.Generic.KeyValuePair.Create``2(``0,``1);borrows", "with no marker", false)]
    [DataRow("M:System.Collections.Generic.KeyValuePair.Create``2(``0,``1);borrows;for=TKey,TValue", "with no marker", false)]
    [DataRow("M:System.Collections.Generic.KeyValuePair.Create``2(``0,``1);borrows;for=TKey", null, false)]
    [DataRow("M:System.Collections.Concurrent.ConcurrentStack`1.TryPop(`0@);borrows", null, false)]
    [DataRow("M:System.Collections.Concurrent.ConcurrentStack`1.TryPopRange(`0[]);borrows", null, false)]
    [DataRow("M:Writer`1.Write(`0);borrows", null, false)]
    [DataRow("M:System.Collections.Generic.Dictionary`2.TryAdd(`0,`1);borrows", "widens", false)]
    [DataRow("M:System.Threading.Channels.ChannelWriter`1.TryWrite(`0);borrows", null, true)]
    [DataRow("M:System.Threading.Channels.ChannelWriter`1.WriteAsync(`0,System.Threading.CancellationToken);borrows", null, true)]
    public async Task BuiltInList_ABorrowsLineThatClaimsMoreGetsAWarning(string line, string? builtIn, bool inPackage)
    {
        string path = inPackage ? "/packages/lib/1.0.0/build/NonCopyableSymbols.Lib.txt" : Verify.ListFileName;
        string id = line[..line.IndexOf(';', StringComparison.Ordinal)];
        var test = new Verify.Test {
            TestCode = $$"""
                class C
                {
                }

                {{Writer}}

                {{Sources.Guard}}
                """,
        };

        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", "is_global = true" + "\n" + "build_property.NuGetPackageRoot = /packages/" + "\n"));
        test.TestState.AdditionalFiles.Add((path, line));

        if (builtIn is "widens")
            test.ExpectedDiagnostics.Add(Verify.SafeListWidensBuiltIn(1, line.Length + 1, id, "every type parameter", "TValue"));
        else if (builtIn is not null)
            test.ExpectedDiagnostics.Add(Verify.SafeListBorrowsBeyondBuiltIn(1, line.Length + 1, id, builtIn));

        await test.RunAsync();
    }

    // Where two lines for one member disagree, the one that claims less applies: the entry lists the outcomes that both lines list, where
    // 'borrows' lists every outcome and a line with any other marker, or none, lists none. An outcome that one line has and the analyzer
    // does not know counts as not listed.
    [TestMethod]
    [DataRow("borrowswhen=false", "borrowswhen=false", "BorrowsWhen", "False")]
    [DataRow("borrowswhen=true", "borrowswhen=true", "BorrowsWhen", "True")]
    [DataRow("borrowswhen=false", "borrowswhen=true", "None", "None")]
    [DataRow("borrows", "borrowswhen=false", "BorrowsWhen", "False")]
    [DataRow("borrows", "borrowswhen=true", "BorrowsWhen", "True")]
    [DataRow("borrows", "borrows", "Borrows", "True, False")]
    [DataRow("borrowswhen=false", "new", "None", "None")]
    [DataRow("borrowswhen=false", "", "None", "None")]
    [DataRow("borrowswhen=false,later", "borrowswhen=false", "BorrowsWhen", "False")]
    [DataRow("borrowswhen=later", "borrowswhen=false", "None", "None")]
    [DataRow("borrowswhen=false,throws", "borrows", "BorrowsWhen", "False")]
    [DataRow("borrowswhen=throws", "borrows", "None", "None")]
    public async Task Merge_TheLineThatClaimsLessApplies(string first, string second, string marker, string borrowed)
    {
        string[] files = [Line(first), Line(second)];

        var forward = await GetMergedEntryAsync("M:Writer`1.TryWrite(`0)", files);
        var backward = await GetMergedEntryAsync("M:Writer`1.TryWrite(`0)", [.. files.Reverse()]);

        Assert.AreEqual((marker, borrowed), forward);
        Assert.AreEqual(forward, backward);

        static string Line(string marker) => marker.Length is 0 ? "M:Writer`1.TryWrite(`0)" : $"M:Writer`1.TryWrite(`0);{marker}";
    }

    // The built-in entry of ChannelWriter<T>.TryWrite, alone, merged with a line of a file that says the same, and merged with one that
    // claims more.
    [TestMethod]
    [DataRow("")]
    [DataRow("M:System.Threading.Channels.ChannelWriter`1.TryWrite(`0);borrowswhen=false")]
    [DataRow("M:System.Threading.Channels.ChannelWriter`1.TryWrite(`0);borrows")]
    public async Task Merge_TheBuiltInEntryOfChannelWriterTryWriteKeepsItsClaim(string line) => Assert.AreEqual(
        ("BorrowsWhen", "False"),
        await GetMergedEntryAsync("M:System.Threading.Channels.ChannelWriter`1.TryWrite(`0)", line.Length is 0 ? [] : [line]));

    // The marker and the outcomes in which the member keeps nothing of the value of the entry that the list of a compilation has for a
    // member, with the given lines as symbol list files of the project. The list is internal to the analyzer, so it is reached by
    // reflection.
    private static async Task<(string Marker, string Borrowed)> GetMergedEntryAsync(string id, string[] files)
    {
        var references = await Verify.Test.NetReferenceAssemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var compilation = CSharpCompilation.Create(
            "Merged",
            [CSharpSyntaxTree.ParseText(Writer + "\n" + Sources.Guard)],
            references.Add(MetadataReference.CreateFromFile(typeof(NonCopyableAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var listType = typeof(DoNotCopyValueAnalyzer).Assembly.GetType("Singulink.CodeAnalysis.Analyzers.SafeMemberList", throwOnError: true)!;
        IReadOnlyList<SourceText> texts = [.. files.Select(f => SourceText.From(f))];
        object list = listType.GetMethod("Create")!.Invoke(null, [texts, true, compilation])!;
        object? entry = listType.GetMethod("GetMember", [typeof(string)])!.Invoke(list, [id]);

        Assert.IsNotNull(entry, $"The list has no entry for '{id}'.");
        return (
            entry.GetType().GetProperty("Marker")!.GetValue(entry)!.ToString()!,
            entry.GetType().GetProperty("BorrowedOutcomes")!.GetValue(entry)!.ToString()!);
    }
}
