using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the place of a copy that a hand-off makes: a read by value of a local or a by-value parameter that is no last use only
/// because the variable is used after it is reported at the first use after it on each path (SCA1016), with the read as an additional
/// location. Every other copy stays where it is made.
/// </summary>
[PrefixTestClass]
public class HandOffTests
{
    private const string Take = "'C.Take(Guard)' received its value";

    private const string Members = """
        Guard _field;
        Guard[] _array = new Guard[1];

        static void Take(Guard guard) { }
        static bool TryTake(Guard guard) => true;
        static void Keep(Guard guard) { }
        static void Touch(ref Guard guard) { }
        static bool Check(Guard guard) => true;
        """;

    [TestMethod]
    public Task HandOff_IsReportedAtTheUseAfterIt() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            {{Members}}

            void OneUse(Guard guard)
            {
                if (!TryTake({|#1:guard|}))
                    {|#0:guard|}.Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOff(0, "guard", "Guard", "'C.TryTake(Guard)' received its value", 1));

    // A use after the first one on a path is not reported.
    // A 'using' statement over a variable takes a copy of its value, which it disposes at its end. A use of the variable inside the
    // statement, or after it, is a use after that hand-off, and the message says what the 'using' did, and how to write it instead.
    [TestMethod]
    public Task HandOff_ToAUsingStatementSaysThatItTookACopy() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            {{Members}}

            void Inside()
            {
                var guard = new Guard();

                using ({|#1:guard|})
                    _ = {|#0:guard|}.Value;
            }

            void After(Guard guard)
            {
                using ({|#3:guard|})
                {
                }

                _ = {|#2:guard|}.Value;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterUsing(0, "guard", "Guard", 1),
        Verify.UsedAfterUsing(2, "guard", "Guard", 3));

    [TestMethod]
    public Task HandOff_IsReportedAtTheFirstUseOnAPathOnly() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            {{Members}}

            void SeveralUses(RwLock rwLock)
            {
                var guard = rwLock.Enter();
                Take({|#1:guard|});
                {|#0:guard|}.Value = 1;
                guard.Value = 2;
                guard.Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOff(0, "guard", "Guard", Take, 1));

    [TestMethod]
    public Task HandOff_IsReportedAtTheFirstUseOnEachBranch() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            {{Members}}

            void Branches(RwLock rwLock, bool condition)
            {
                var guard = rwLock.Enter();
                Take({|#2:guard|});

                if (condition)
                    {|#0:guard|}.Dispose();
                else
                    {|#1:guard|}.Value = 1;
            }

            // A branch that assigns the variable first has no use after the hand-off.
            void AssignedOnOneBranch(RwLock rwLock, bool condition)
            {
                var guard = rwLock.Enter();
                Take({|#4:guard|});

                if (condition)
                    guard = rwLock.Enter();
                else
                    {|#3:guard|}.Value = 1;

                guard.Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOff(0, "guard", "Guard", Take, 2),
        Verify.UsedAfterHandOff(1, "guard", "Guard", Take, 2),
        Verify.UsedAfterHandOff(3, "guard", "Guard", Take, 4));

    // A use that two hand-offs reach is reported once, with both of them, and the message describes the first.
    [TestMethod]
    public Task HandOff_AUseThatTwoHandOffsReachIsReportedOnce() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            {{Members}}

            void TwoHandOffs(RwLock rwLock, bool condition)
            {
                var guard = rwLock.Enter();

                if (condition)
                    Take({|#1:guard|});
                else
                    Keep({|#2:guard|});

                {|#0:guard|}.Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOff(0, "guard", "Guard", Take, 1, 2));

    // A struct with a field, properties, an indexer and a field of a struct, and helpers that assign a variable as a whole.
    private const string Lease = """
        [Singulink.CodeAnalysis.NonCopyable]
        public struct Lease
        {
            public int Count;
            public Part Inner;
            public Total Sum;
            public int Uses { get => Count; set => Count = value; }
            public string? Name { get => null; set { } }
            public int this[int index] { get => Count; set => Count = value; }
        }

        public struct Part
        {
            public int Count;
            public int Uses { get => Count; set => Count = value; }
        }

        public struct Total
        {
            public void operator -=(int value) { }
        }

        public sealed class Leases
        {
            public static void Take(Lease lease) { }
            public static int Reset(out Lease lease) { lease = default; return 1; }
            public static string? ResetName(out Lease lease) { lease = default; return null; }
            public static bool TryTake(Lease lease) => true;
            public static void Hold(Lease lease) { }
            public static void Log() { }
        }
        """;

    private const string LeaseListFile = """
        M:Leases.TryTake(Lease);borrowswhen=false
        """;

    private const string TakeLease = "'Leases.Take(Lease)' received its value";

    // A compound assignment reads its target before its value runs: the getter of a property or an indexer, or the field, is used
    // on the value that was handed over before the value assigns the variable, also where the graph captures the target before a
    // branch in the value, where '??=' runs the value on one branch only, and where the target is a member of a field.
    [TestMethod]
    public Task HandOff_ACompoundAssignmentReadsItsTargetBeforeItsValue() => Verify.AnalyzerAsync(
        $$"""
        using static Leases;

        class C
        {
            void Field(Lease lease)
            {
                Take({|#1:lease|});
                {|#0:lease|}.Count += Reset(out lease);
            }

            void Property(Lease lease)
            {
                Take({|#3:lease|});
                {|#2:lease|}.Uses -= Reset(out lease);
            }

            void PropertyWithTheAssignmentInAnIndex(Lease lease, int[] values)
            {
                Take({|#5:lease|});
                {|#4:lease|}.Uses += values[Reset(out lease)];
            }

            void Bitwise(Lease lease)
            {
                Take({|#7:lease|});
                {|#6:lease|}.Uses &= Reset(out lease);
            }

            void Shift(Lease lease)
            {
                Take({|#9:lease|});
                {|#8:lease|}.Uses >>>= Reset(out lease);
            }

            void Indexer(Lease lease)
            {
                Take({|#11:lease|});
                {|#10:lease|}[0] |= Reset(out lease);
            }

            void PropertyOfAField(Lease lease)
            {
                Take({|#13:lease|});
                {|#12:lease|}.Inner.Uses += Reset(out lease);
            }

            void FieldOfAField(Lease lease)
            {
                Take({|#15:lease|});
                {|#14:lease|}.Inner.Count *= Reset(out lease);
            }

            void PropertyWithABranchInTheValue(Lease lease, bool condition)
            {
                Take({|#17:lease|});
                {|#16:lease|}.Uses += condition ? Reset(out lease) : Reset(out lease);
            }

            void FieldWithABranchInTheValue(Lease lease, bool condition)
            {
                Take({|#19:lease|});
                {|#18:lease|}.Count += condition ? Reset(out lease) : Reset(out lease);
            }

            void CoalescingProperty(Lease lease)
            {
                Take({|#21:lease|});
                {|#20:lease|}.Name ??= ResetName(out lease);
            }

            void Increment(Lease lease)
            {
                Take({|#23:lease|});
                {|#22:lease|}.Uses++;
            }
        }

        {{Lease}}
        """,
        Verify.UsedAfterHandOff(0, "lease", "Lease", TakeLease, 1),
        Verify.UsedAfterHandOff(2, "lease", "Lease", TakeLease, 3),
        Verify.UsedAfterHandOff(4, "lease", "Lease", TakeLease, 5),
        Verify.UsedAfterHandOff(6, "lease", "Lease", TakeLease, 7),
        Verify.UsedAfterHandOff(8, "lease", "Lease", TakeLease, 9),
        Verify.UsedAfterHandOff(10, "lease", "Lease", TakeLease, 11),
        Verify.UsedAfterHandOff(12, "lease", "Lease", TakeLease, 13),
        Verify.UsedAfterHandOff(14, "lease", "Lease", TakeLease, 15),
        Verify.UsedAfterHandOff(16, "lease", "Lease", TakeLease, 17),
        Verify.UsedAfterHandOff(18, "lease", "Lease", TakeLease, 19),
        Verify.UsedAfterHandOff(20, "lease", "Lease", TakeLease, 21),
        Verify.UsedAfterHandOff(22, "lease", "Lease", TakeLease, 23));

    // What runs after the value of an assignment runs on the value that it assigned: a setter, a store into a field, an operator of
    // the target's own type that is called on it in place, and the accessors of an indexer whose argument assigns the variable.
    // The setter of a compound assignment is a use of its own after the value, as a hand-off in the value shows.
    [TestMethod]
    public Task HandOff_WhatRunsAfterTheValueOfAnAssignmentUsesTheValueThatItAssigned() => Verify.AnalyzerAsync(
        $$"""
        using static Leases;

        class C
        {
            void Setter(Lease lease)
            {
                Take(lease);
                lease.Uses = Reset(out lease);
            }

            void Store(Lease lease)
            {
                Take(lease);
                lease.Count = Reset(out lease);
            }

            void IndexerSetter(Lease lease)
            {
                Take(lease);
                lease[0] = Reset(out lease);
            }

            void AssignedInTheIndex(Lease lease)
            {
                Take(lease);
                lease[Reset(out lease)] += 1;
                lease[Reset(out lease)]++;
            }

            void Deconstruction(Lease lease)
            {
                Take(lease);
                (lease.Uses, lease.Count) = (Reset(out lease), 1);
            }

            void OperatorOfTheTarget(Lease lease)
            {
                Take(lease);
                lease.Sum -= Reset(out lease);
            }

            void HandOffInTheValue(Lease lease)
            {
                {|#0:lease|}.Uses += Count({|#1:lease|});
            }

            static int Count(Lease lease) => 0;
        }

        {{Lease}}
        """,
        Verify.UsedAfterHandOff(0, "lease", "Lease", "'C.Count(Lease)' received its value", 1));

    // The same after a hand-off to a member that has taken the value only where its result says so, on the branch where it has, and
    // in a 'catch' after a hand-off, where the getter runs on the value that the member may have taken before it threw.
    [TestMethod]
    public Task HandOff_ACompoundAssignmentReadsItsTargetBeforeItsValueWhereTheMemberHasTakenIt() => Verify.AnalyzerAsync(
        $$"""
        using static Leases;

        class C
        {
            void WhereTaken(Lease lease)
            {
                if (!TryTake({|#1:lease|}))
                    return;

                {|#0:lease|}.Uses += Reset(out lease);
            }

            void WhereNotTaken(Lease lease)
            {
                if (!TryTake(lease))
                    lease.Uses += Reset(out lease);
            }

            void InCatch(Lease lease)
            {
                try
                {
                    Hold({|#3:lease|});
                    Log();
                }
                catch
                {
                    {|#2:lease|}.Uses += Reset(out lease);
                }
            }
        }

        {{Lease}}
        """,
        LeaseListFile,
        Verify.UsedAfterHandOff(0, "lease", "Lease", "'Leases.TryTake(Lease)' received its value", 1),
        Verify.UsedAfterHandOffInHandler(2, "lease", "Lease", "'Leases.Hold(Lease)' received its value", 3));

    [TestMethod]
    public Task HandOff_ACompoundAssignmentReadsItsTargetBeforeItsValueUnderTheUpdatedRules() => Verify.UpdatedRulesAsync(
        $$"""
        using static Leases;

        class C
        {
            void Field(Lease lease)
            {
                Take({|#1:lease|});
                {|#0:lease|}.Count += Reset(out lease);
            }

            void Property(Lease lease)
            {
                Take({|#3:lease|});
                {|#2:lease|}.Uses -= Reset(out lease);
            }

            void CoalescingProperty(Lease lease)
            {
                Take({|#5:lease|});
                {|#4:lease|}.Name ??= ResetName(out lease);
            }
        }

        {{Sources.AsUnsafeToCopy(Lease)}}
        """,
        Verify.UsedAfterHandOff(0, "lease", "Lease", TakeLease, 1).AsError(),
        Verify.UsedAfterHandOff(2, "lease", "Lease", TakeLease, 3).AsError(),
        Verify.UsedAfterHandOff(4, "lease", "Lease", TakeLease, 5).AsError());

    // A later use that is itself a hand-off is reported as a use after the first, and is the hand-off for what follows it.
    [TestMethod]
    public Task HandOff_AChainOfHandOffs() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            {{Members}}

            void Chain(RwLock rwLock)
            {
                var guard = rwLock.Enter();
                Take({|#2:guard|});
                Keep({|#1:guard|});
                {|#0:guard|}.Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOff(1, "guard", "Guard", Take, 2),
        Verify.UsedAfterHandOff(0, "guard", "Guard", "'C.Keep(Guard)' received its value", 1));

    // A 'catch' and a 'finally' that use the variable run after a hand-off in the 'try' block.
    [TestMethod]
    public Task HandOff_AUseInAHandlerIsAUseAfterIt() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            {{Members}}

            void InCatch(RwLock rwLock)
            {
                var guard = rwLock.Enter();

                try
                {
                    Take({|#1:guard|});
                }
                catch
                {
                    {|#0:guard|}.Dispose();
                    throw;
                }
            }

            void InFinally(RwLock rwLock)
            {
                var guard = rwLock.Enter();

                try
                {
                    Take({|#3:guard|});
                }
                finally
                {
                    {|#2:guard|}.Dispose();
                }
            }

            // The value that a 'finally' uses after a 'return' has been handed over by the return.
            Guard Returned(RwLock rwLock)
            {
                var guard = rwLock.Enter();

                try
                {
                    return {|#5:guard|};
                }
                finally
                {
                    {|#4:guard|}.Dispose();
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOffInHandler(0, "guard", "Guard", Take, 1),
        Verify.UsedAfterHandOffInHandler(2, "guard", "Guard", Take, 3),
        Verify.UsedAfterHandOffInHandler(4, "guard", "Guard", "its value was returned", 5));

    // A read that is its own later use, the next time round a loop, keeps the report of the copy. Another later use is reported as a
    // use after it.
    [TestMethod]
    public Task HandOff_InALoop() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            {{Members}}

            void Again(Guard guard)
            {
                for (int i = 0; i < 2; i++)
                    Take({|#0:guard|});
            }

            void AgainAndAfter(Guard guard, bool condition)
            {
                while (condition)
                    Take({|#1:guard|});

                {|#2:guard|}.Dispose();
            }

            void OtherUseNextTime(RwLock rwLock, bool condition)
            {
                var guard = rwLock.Enter();

                while (condition)
                {
                    guard.Value = 1;
                    Take(guard);
                    guard = rwLock.Enter();
                }

                guard.Dispose();
            }

            void OtherUseNextTimeWithoutAssignment(Guard guard, bool condition)
            {
                while (condition)
                {
                    {|#5:guard|}.Value = 1;
                    Take({|#6:guard|});
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(1, "'C.Take(Guard)' receives", "Guard"),
        Verify.UsedAfterHandOff(2, "guard", "Guard", Take, 1),
        Verify.UsedAfterHandOff(5, "guard", "Guard", Take, 6));

    // Each hand-off is described as it is made.
    [TestMethod]
    public Task HandOff_IsDescribed() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            {{Members}}

            void Assigned(RwLock rwLock)
            {
                var guard = rwLock.Enter();
                _field = {|#1:guard|};
                {|#0:guard|}.Dispose();
            }

            void Declared(RwLock rwLock)
            {
                var guard = rwLock.Enter();
                var copy = {|#3:guard|};
                {|#2:guard|}.Dispose();
                copy.Dispose();
            }

            void Boxed(RwLock rwLock)
            {
                var guard = rwLock.Enter();
                object box = {|#5:guard|};
                {|#4:guard|}.Dispose();
            }

            void InATuple(RwLock rwLock)
            {
                var guard = rwLock.Enter();
                var tuple = ({|#7:guard|}, 1);
                {|#6:guard|}.Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOff(0, "guard", "Guard", "its value was assigned", 1),
        Verify.UsedAfterHandOff(2, "guard", "Guard", "its value was assigned to \'copy\'", 3),
        Verify.UsedAfterHandOff(4, "guard", "Guard", "its value was converted to \'object\'", 5),
        Verify.UsedAfterHandOff(6, "guard", "Guard", "the tuple took its value", 7));

    // What is not a local or a by-value parameter, or can be used by something else after the read, is copied by the read, which is
    // reported where it is.
    [TestMethod]
    public Task HandOff_EveryOtherCopyStaysAtTheRead() => Verify.AnalyzerAsync(
        $$"""
        using System;

        class C
        {
            {{Members}}

            void Field()
            {
                Take({|#0:_field|});
                _field.Dispose();
            }

            void Element()
            {
                Take({|#1:_array[0]|});
                _array[0].Dispose();
            }

            void RefParameter(ref Guard guard)
            {
                Take({|#2:guard|});
                guard.Dispose();
            }

            void InParameter(in Guard guard)
            {
                Take({|#3:guard|});
                _ = guard.Value;
            }

            void Captured(RwLock rwLock)
            {
                var guard = rwLock.Enter();
                Action dispose = () => guard.Dispose();
                Take({|#4:guard|});
                dispose();
            }

            void Using(RwLock rwLock)
            {
                using var guard = rwLock.Enter();
                Take({|#5:guard|});
            }

            void Referenced(RwLock rwLock)
            {
                var guard = rwLock.Enter();
                ref Guard reference = ref guard;
                Take({|#6:guard|});
                reference.Dispose();
            }

            void Filter(RwLock rwLock)
            {
                var guard = rwLock.Enter();

                try
                {
                }
                catch when (Check({|#7:guard|}))
                {
                }

                guard.Dispose();
            }

            void OutParameter(out Guard guard)
            {
                guard = default;
                Take({|#8:guard|});
                guard.Dispose();
            }

            // The receiver and the 'ref' argument are used by the call itself.
            void TwoUsesInOneOperation(RwLock rwLock)
            {
                var guard = rwLock.Enter();
                {|#9:guard|}.With(ref guard);
            }
        }

        static class Extensions
        {
            extension(Guard guard)
            {
                public void With(ref Guard other) { }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(1, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(2, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(3, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(4, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(5, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(6, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValueInFilter(7, "'C.Check(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(8, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(9, "'Extensions.extension(Guard).With(ref Guard)' receives", "Guard"));

    // With the moves turned off no read is a last use, and every copy stays where it is made.
    [TestMethod]
    public Task HandOff_WithTheMovesTurnedOffStaysAtTheRead() => new Verify.Test {
        TestCode = $$"""
            class C
            {
                {{Members}}

                void OneUse(Guard guard)
                {
                    if (!TryTake({|#0:guard|}))
                        guard.Dispose();
                }
            }

            {{Sources.Guard}}
            """,
        ExpectedDiagnostics = { Verify.CopyOfStoredValue(0, "'C.TryTake(Guard)' receives", "Guard") },
    }.WithBuildProperty("NonCopyableAllowImplicitMoves", "false").RunAsync();

    // The place of the use decides whether it is an error, under the updated rules: a use after the hand-off in an unsafe context is
    // a warning, and an unsafe context around the hand-off alone changes nothing.
    [TestMethod]
    public Task HandOff_UnderTheUpdatedRulesThePlaceOfTheUseDecides() => Verify.UpdatedRulesAsync(
        $$"""
        class C
        {
            {{Members}}

            void UseInUnsafeExpression(Guard guard)
            {
                if (!TryTake({|#1:guard|}))
                    Keep(unsafe({|#0:guard|}));
            }

            void UseInUnsafeBlock(Guard guard)
            {
                if (!TryTake({|#3:guard|}))
                {
                    unsafe
                    {
                        {|#2:guard|}.Dispose();
                    }
                }
            }

            void HandOffInUnsafeExpression(Guard guard)
            {
                if (!TryTake(unsafe({|#5:guard|})))
                    {|#4:guard|}.Dispose();
            }

            void HandOffInUnsafeBlock(Guard guard)
            {
                bool taken;

                unsafe
                {
                    taken = TryTake({|#7:guard|});
                }

                if (!taken)
                    {|#6:guard|}.Dispose();
            }
        }

        {{Sources.UnsafeGuard}}
        """,
        Verify.UsedAfterHandOff(0, "guard", "Guard", "'C.TryTake(Guard)' received its value", 1),
        Verify.UsedAfterHandOff(2, "guard", "Guard", "'C.TryTake(Guard)' received its value", 3),
        Verify.UsedAfterHandOff(4, "guard", "Guard", "'C.TryTake(Guard)' received its value", 5).AsError(),
        Verify.UsedAfterHandOff(6, "guard", "Guard", "'C.TryTake(Guard)' received its value", 7).AsError());

    // With a plain non-copyable type a use after the hand-off is the ordinary warning under the updated rules, wherever it is.
    [TestMethod]
    public Task HandOff_OfAPlainTypeIsTheOrdinaryWarningUnderTheUpdatedRules() => Verify.UpdatedRulesAsync(
        $$"""
        class C
        {
            {{Members}}

            void UseInUnsafeExpression(Guard guard)
            {
                if (!TryTake({|#1:guard|}))
                    Keep(unsafe({|#0:guard|}));
            }

            void UseInUnsafeBlock(Guard guard)
            {
                if (!TryTake({|#3:guard|}))
                {
                    unsafe
                    {
                        {|#2:guard|}.Dispose();
                    }
                }
            }

            void HandOffInUnsafeExpression(Guard guard)
            {
                if (!TryTake(unsafe({|#5:guard|})))
                    {|#4:guard|}.Dispose();
            }

            void HandOffInUnsafeBlock(Guard guard)
            {
                bool taken;

                unsafe
                {
                    taken = TryTake({|#7:guard|});
                }

                if (!taken)
                    {|#6:guard|}.Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOff(0, "guard", "Guard", "'C.TryTake(Guard)' received its value", 1),
        Verify.UsedAfterHandOff(2, "guard", "Guard", "'C.TryTake(Guard)' received its value", 3),
        Verify.UsedAfterHandOff(4, "guard", "Guard", "'C.TryTake(Guard)' received its value", 5),
        Verify.UsedAfterHandOff(6, "guard", "Guard", "'C.TryTake(Guard)' received its value", 7));
}
