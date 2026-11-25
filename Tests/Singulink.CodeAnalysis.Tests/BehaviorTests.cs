using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the rules for values of a non-copyable type in cases that Roslyn's tests do not cover. The rules behave as Roslyn's rule does unless a
/// test says that they differ: where Roslyn's rule misses a copy or reports one that is not there and this one does the same, the test says so and
/// checks that behavior.
/// </summary>
/// <remarks>
/// Docs: articles/rules/new-and-stored-values.md and articles/rules/roslyn-differences.md.
/// </remarks>
[PrefixTestClass]
public class BehaviorTests
{
    private const string Guard = """
        [Singulink.CodeAnalysis.NonCopyable]
        struct Guard : System.IDisposable
        {
            public int Value;
            public void Dispose() { }
            public void Use() { }
            public readonly void Read() { }
        }
        """;

    [TestMethod]
    public Task FieldInStruct_MustBeInNonCopyableStruct() => Verify.AnalyzerAsync(
        $$"""
        class InClass
        {
            Guard _guard;
        }

        [Singulink.CodeAnalysis.NonCopyable]
        struct InNonCopyableStruct
        {
            Guard _guard;
        }

        [Singulink.CodeAnalysis.NonCopyable]
        ref struct InNonCopyableRefStruct
        {
            Guard _guard;
        }

        struct InStruct
        {
            Guard {|#0:_guard|};
        }

        ref struct InRefStruct
        {
            Guard {|#1:_guard|};
        }

        record struct InRecordStruct
        {
            Guard {|#2:_guard|};
        }

        ref struct WithRefField
        {
            // A ref field stores a reference to the value and not the value.
            public ref Guard Reference;
        }

        struct WithStaticField
        {
            // Not a copy: a static field is not copied with the struct. Roslyn's rule reports it.
            static Guard s_guard;
        }

        record struct RecordWithStaticField
        {
            static Guard s_guard;
        }

        {{Guard}}
        """,
        Verify.FieldOfCopyableType(0, "Guard", "InStruct._guard"),
        Verify.FieldOfCopyableType(1, "Guard", "InRefStruct._guard"),
        Verify.FieldOfRecord(2, "Guard", "InRecordStruct._guard"));

    [TestMethod]
    public Task FieldInStruct_ContainingStructIsCheckedOnlyIfMarked() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            void M(Marked marked, Unmarked unmarked)
            {
                var markedCopy = {|#0:marked|};
                var last = {|#2:marked|};

                // The struct that is not marked is reported for its field, not for its copies.
                var unmarkedCopy = unmarked;
            }
        }

        [Singulink.CodeAnalysis.NonCopyable]
        struct Marked
        {
            Guard _guard;
        }

        struct Unmarked
        {
            Guard {|#1:_guard|};
        }

        {{Guard}}
        """,
        Verify.UsedAfterHandOff(2, "marked", "Marked", "its value was assigned to \'markedCopy\'", 0),
        Verify.FieldOfCopyableType(1, "Guard", "Unmarked._guard"));

    [TestMethod]
    public Task RefKinds() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            void M(ref Guard r, in Guard i, ref readonly Guard rr, scoped ref Guard sr, Guard v, out Guard o)
            {
                o = new Guard();

                TakeRef(ref r);
                TakeRef(ref sr);
                TakeRef(ref v);
                TakeIn(in r);
                TakeIn(in i);
                TakeIn(i);
                TakeIn(in rr);
                TakeRefReadOnly(in rr);
                TakeRefReadOnly(ref r);
                TakeIn(new Guard());

                ref Guard refLocal = ref r;
                ref readonly Guard refReadOnlyLocal = ref i;
                scoped ref Guard scopedLocal = ref sr;

                TakeValue({|#0:r|});
                TakeValue({|#1:i|});
                TakeValue({|#2:rr|});

                // A parameter passed by value is a copy that the method owns. Here it is used for the last time, so it is moved on.
                TakeValue(v);

                Guard copy = {|#4:r|};
                Guard copy2 = {|#5:refReadOnlyLocal|};
                copy = {|#6:scopedLocal|};
            }

            void TakeRef(ref Guard guard) { }
            void TakeIn(in Guard guard) { }
            void TakeRefReadOnly(ref readonly Guard guard) { }
            void TakeValue(Guard guard) { }
        }

        {{Guard}}
        """,
        Verify.CopyOfStoredValue(0, "'C.TakeValue(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(1, "'C.TakeValue(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(2, "'C.TakeValue(Guard)' receives", "Guard"),
        Verify.CopyByAssignment(4, "Guard"),
        Verify.CopyByAssignment(5, "Guard"),
        Verify.CopyByAssignment(6, "Guard"));

    [TestMethod]
    public Task Members_CallsOnReadOnlyReferences() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            readonly Guard _readOnlyField;
            Guard _field;

            void M(in Guard i)
            {
                // A call of a member that is not read-only on a read-only reference is made on a defensive copy.
                {|#0:i|}.Use();
                i.Read();
                _ = i.Value;

                _field.Use();
                _readOnlyField.Read();
                _ = _readOnlyField.Value;

                {|#1:_readOnlyField|}.Use();
            }
        }

        {{Guard}}
        """,
        Verify.MemberOnReadOnlyReference(0, "Guard", "Guard.Use()"),
        Verify.MemberOnReadOnlyReference(1, "Guard", "Guard.Use()"));

    [TestMethod]
    public Task Using() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            Guard _field;

            void M(Guard parameter)
            {
                using var declared = new Guard();
                using (var statement = new Guard()) { }
                using (new Guard()) { }

                var local = new Guard();
                local.Dispose();
                _field.Dispose();

                // The using statement stores the value of an expression in a hidden local: a copy, unless the variable is not used again.
                using ({|#0:local|}) { }
                using (parameter) { }
                using var last = {|#1:local|};
            }
        }

        {{Guard}}
        """,
        Verify.UsedAfterHandOff(1, "local", "Guard", "\'using\' disposed its value", 0));

    [TestMethod]
    public Task AsyncMethodsAndLambdas() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Threading.Tasks;

        class C
        {
            async Task MAsync()
            {
                // Locals are hoisted into the state machine, not copied.
                using var guard = new Guard();
                await Task.Yield();
                guard.Use();
            }

            async Task<Guard> CreateAsync()
            {
                await Task.Yield();
                return new Guard();
            }

            async Task UseCreateAsync()
            {
                // The result of awaiting a task that is not stored anywhere is a new value. Awaiting a stored task is reported, since the task
                // keeps its result, unless the task is the method's own and is not used again.
                var created = await CreateAsync();
                var created2 = await new ValueTask<Guard>(new Guard());

                var task = CreateAsync();
                var created3 = {|#0:await task|};
                var created4 = await task;
            }

            void M()
            {
                var local = new Guard();

                // A captured local moves into the closure; it is not copied.
                Action use = () => local.Use();
                Func<Guard> create = () => new Guard();

                // The lambda returns a copy of the captured local, which still exists after the lambda returns. Roslyn's rule misses it.
                Func<Guard> copy = () => {|#2:local|};

                Func<Guard> copy2 = () => {|#1:_field|};

                void LocalFunction() => local.Use();
                LocalFunction();
            }

            Guard _field;
        }

        {{Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'await'", "Guard"),
        Verify.CopyByReturn(1, "Guard"),
        Verify.CopyByReturnOfLivingVariable(2, "Guard", "local"));

    [TestMethod]
    public Task BoxingAndUnboxing() => Verify.AnalyzerAsync(
        """
        using System;

        class C
        {
            static Guard Create() => default;

            void M()
            {
                var local = new Guard();

                // Boxing a new value is not reported: the box holds the only instance. Roslyn's rule reports the result of a call.
                object boxed = new Guard();
                object boxedResult = Create();
                IDisposable boxedDefault = default(Guard);

                object box2 = {|#0:local|};
                IDisposable disposable = {|#1:local|};
                string text = $"{{|#2:local|}}";
                {|#5:local|}.Use();

                Guard unboxed = {|#3:(Guard)boxed|};

                // Unboxing by a pattern copies the value out of the box. Roslyn's rule misses it.
                if (boxed is Guard {|#4:matched|})
                    matched.Use();
            }
        }

        """ + Guard,
        Verify.UsedAfterHandOff(1, "local", "Guard", "its value was converted to \'object\'", 0),
        Verify.UsedAfterHandOff(2, "local", "Guard", "its value was converted to \'IDisposable\'", 1),
        Verify.UsedAfterHandOff(5, "local", "Guard", "the interpolated string took its value", 2),
        Verify.CopyByAssignmentOutOfBox(3, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(4, "Guard", "object"));

    [TestMethod]
    public Task DefaultAndObjectCreation() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            Guard _field = new Guard();
            Guard _field2 = default;

            void M()
            {
                Guard a = default;
                Guard b = default(Guard);
                Guard c = new();
                var d = new Guard { Value = 1 };

                a = new Guard();
                a = default;
                _field = new();

                Take(new Guard());
                Take(default);
            }

            void Take(Guard guard) { }
        }

        {{Guard}}
        """);

    [TestMethod]
    public Task TuplesAndDeconstruction() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            void M()
            {
                var local = new Guard();

                // A tuple of a non-copyable type is a non-copyable type, so a copy of it is reported. Roslyn's rule misses it.
                var tuple = (new Guard(), 1);
                var tupleCopy = {|#3:tuple|};

                var tuple2 = ({|#0:local|}, 1);
                var item = {|#1:{|#4:tuple|}.Item1|};

                // The variable is a copy of the element of a stored tuple. Roslyn's rule reports the declared local as an unsupported use.
                var ({|#2:deconstructed|}, number) = {|#5:tuple|};

                // Both are used again here, so none of the reads above is the last use of its variable.
                _ = ({|#6:local|}, {|#7:tuple|});
            }
        }

        {{Guard}}
        """,
        Verify.UsedAfterHandOff(6, "local", "Guard", "the tuple took its value", 0),
        Verify.UsedAfterHandOff(4, "tuple", "(Guard, int)", "its value was assigned to \'tupleCopy\'", 3),
        Verify.UsedAfterHandOff(5, "tuple", "(Guard, int)", "its field \'Item1\' was read", 4),
        Verify.UsedAfterHandOff(7, "tuple", "(Guard, int)", "its value was deconstructed", 5));

    [TestMethod]
    public Task WithExpression() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            void M()
            {
                var local = new Guard();
                // 'with' copies its operand, which is reported once for a stored value. Roslyn's rule reports it three times.
                var changed = {|#0:local|} with { Value = 1 };
                {|#1:local|}.Dispose();

                // A new value is moved into the copy.
                var fromNew = new Guard() with { Value = 1 };
                var fromCall = Create() with { Value = 2 };
            }

            static Guard Create() => default;
        }

        {{Guard}}
        """,
        Verify.UsedAfterHandOff(1, "local", "Guard", "\'with\' copied its value", 0));

    [TestMethod]
    public Task RefStruct() => Verify.AnalyzerAsync(
        """
        using Singulink.CodeAnalysis;

        class C
        {
            void M(ref RefGuard r)
            {
                var local = new RefGuard();
                var copy = {|#0:local|};
                var last = {|#2:local|};
                var copy2 = {|#1:r|};
                ref RefGuard reference = ref r;
                using var guard = new RefGuard();
            }
        }

        [NonCopyable]
        ref struct RefGuard
        {
            public void Dispose() { }
        }
        """,
        Verify.UsedAfterHandOff(2, "local", "RefGuard", "its value was assigned to \'copy\'", 0),
        Verify.CopyByAssignment(1, "RefGuard"));

    [TestMethod]
    public Task Returns() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            Guard _field;

            Guard Property => {|#0:_field|};
            ref Guard RefProperty => ref _field;
            ref readonly Guard RefReadOnlyProperty => ref _field;

            Guard ReturnLocal()
            {
                var local = new Guard();
                return local;
            }

            Guard ReturnParameter(Guard parameter) => parameter;

            Guard ReturnRefParameter(ref Guard parameter) => {|#1:parameter|};

            Guard ReturnNew() => new Guard();

            void M()
            {
                var fromProperty = Property;
                var fromRefProperty = {|#2:RefProperty|};
                ref var refFromRefProperty = ref RefProperty;
            }
        }

        [Singulink.CodeAnalysis.NonCopyable]
        struct Self
        {
            Self Clone() => {|#3:this|};
        }

        {{Guard}}
        """,
        Verify.CopyByReturn(0, "Guard"),
        Verify.CopyByReturn(1, "Guard"),
        Verify.CopyByAssignment(2, "Guard"),
        Verify.CopyByReturn(3, "Self"));

    // System.Threading.SpinLock and System.Runtime.InteropServices.GCHandle are entries of the built-in list.
    [TestMethod]
    public Task BuiltInTypes() => Verify.AnalyzerAsync(
        """
        using System.Runtime.InteropServices;
        using System.Threading;

        class C
        {
            SpinLock _lock;
            GCHandle _handle;

            void M()
            {
                var spinLock = {|#0:_lock|};
                var handle = {|#1:_handle|};
            }
        }
        """,
        Verify.CopyByAssignment(0, "System.Threading.SpinLock"),
        Verify.CopyByAssignment(1, "System.Runtime.InteropServices.GCHandle"));

    [TestMethod]
    public Task SwitchExpression_GivesANewValueIfEveryArmDoes() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;

        class C
        {
            Guard _field;

            static Guard Create() => default;
            static ValueTask<Guard> CreateAsync() => default;
            static void Take(Guard guard) { }

            async Task M(int form, Guard parameter)
            {
                // Every arm gives a new value, so nothing is copied. Roslyn's rule reports these.
                Guard assigned;
                assigned = form switch { 0 => Create(), 1 => new Guard(), 2 => default, _ => throw null! };
                var declared = form switch { 0 => await CreateAsync(), _ => Create() };
                using var used = form switch { 0 => Create(), _ => form == 1 ? Create() : new Guard() };
                _field = form switch { 0 => Create(), _ => form switch { 1 => Create(), _ => new Guard() } };
                Take(form switch { 0 => Create(), _ => Create() });
                assigned = (form switch { 0 => Create(), _ => Create() });

                // An arm that gives a value that exists already makes it a copy, which is reported once for the whole expression.
                assigned = {|#0:form switch { 0 => Create(), _ => _field }|};
                assigned = {|#2:form == 0 ? Create() : {|#5:parameter|}|};

                // A variable that an arm uses for the last time is a new value as well.
                var fromLocal = form switch { 0 => declared, _ => Create() };
                Take(form switch { 0 => Create(), _ => {|#6:parameter|} });

                // A new value is moved into a hole of an interpolated string, as into a parameter.
                string text = $"{ form switch { 0 => Create(), _ => Create() } }";

                // A new value is moved into a box, as into a parameter.
                object boxed = form switch { 0 => Create(), _ => Create() };
            }

            Guard Returned(int form) => form switch { 0 => Create(), _ => new Guard() };

            Guard ReturnedFromField(int form) => {|#4:form switch { 0 => Create(), _ => _field }|};
        }

        {{Guard}}
        """,
        Verify.CopyByAssignment(0, "Guard"),
        Verify.UsedAfterHandOff(6, "parameter", "Guard", "its value was assigned", 5),
        Verify.CopyByReturn(4, "Guard"));

    [TestMethod]
    public Task ArgList() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            void M()
            {
                var local = new Guard();
                ArgList(__arglist({|#0:local|}));
                ArgList(__arglist(new Guard()));
            }

            void ArgList(__arglist) { }
        }

        {{Guard}}
        """,
        Verify.CopyOfStoredValue(0, "An argument list takes", "Guard"));

    [TestMethod]
    public Task PrimaryConstructorParameter_UsedByMembersOfACopyableStructIsAField() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        // The compiler keeps a parameter that a member uses in a field. Copying the struct copies it.
        struct Captures(Guard {|#0:guard|}, int number)
        {
            public void Use() => guard.Dispose();

            public int Number => number;
        }

        ref struct RefCaptures(Guard {|#1:guard|})
        {
            public int Value => guard.Value;
        }

        // A parameter that only the initializers use is not kept.
        struct UsesInInitializer(Guard guard)
        {
            public int Value = guard.Value;
        }

        // A field of a non-copyable struct or of a class is fine, and so is one that holds a container.
        [Singulink.CodeAnalysis.NonCopyable]
        struct MarkedCaptures(Guard guard)
        {
            public void Use() => guard.Dispose();
        }

        class ClassCaptures(Guard guard)
        {
            public void Use() => guard.Dispose();
        }

        struct CapturesContainer(List<Guard> guards)
        {
            public int Count => guards.Count;
        }

        {{Guard}}
        """,
        Verify.FieldByPrimaryConstructor(0, "Guard", "guard", "Captures"),
        Verify.FieldByPrimaryConstructor(1, "Guard", "guard", "RefCaptures"));

    [TestMethod]
    public Task DefaultInterfaceMember_MustBeImplementedByAStructOfANonCopyableType() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        interface IRelease
        {
            void Release() { }

            int Count => 0;

            void Reset();

            static void Helper() { }
        }

        interface ISealed
        {
            sealed void Help() { }
        }

        interface IBase
        {
            void Release();
        }

        interface IGivesDefault : IBase
        {
            void IBase.Release() { }
        }

        // A default that a derived interface gives for a member of its base interface counts.
        [NonCopyable]
        struct {|#4:LeavesToDerivedInterface|} : IGivesDefault { }

        [NonCopyable]
        struct ImplementsOverDerivedInterface : IGivesDefault
        {
            public void Release() { }
        }

        // A call of a member that the struct leaves to the interface runs on a boxed copy, also through a constrained type parameter.
        [NonCopyable]
        struct {|#0:Leaves|} : IRelease
        {
            public void Reset() { }
        }

        [NonCopyable]
        struct {|#1:LeavesOne|} : IRelease
        {
            public void Release() { }

            public void Reset() { }
        }

        struct {|#2:Generic|}<[NonCopyable] T> : IRelease
        {
            public void Reset() { }
        }

        // A member that is not virtual cannot be implemented.
        [NonCopyable]
        struct {|#3:CannotImplement|} : ISealed { }

        [NonCopyable]
        struct Implements : IRelease
        {
            public void Release() { }

            public int Count => 1;

            public void Reset() { }
        }

        [NonCopyable]
        struct ImplementsExplicitly : IRelease
        {
            void IRelease.Release() { }

            int IRelease.Count => 1;

            void IRelease.Reset() { }
        }

        // Copying a copyable struct or a reference is fine.
        struct Copyable : IRelease
        {
            public void Reset() { }
        }

        class AClass : IRelease
        {
            public void Reset() { }
        }
        """,
        Verify.DefaultInterfaceMember(0, "Leaves", "IRelease.Count"),
        Verify.DefaultInterfaceMember(0, "Leaves", "IRelease.Release()"),
        Verify.DefaultInterfaceMember(1, "LeavesOne", "IRelease.Count"),
        Verify.DefaultInterfaceMember(2, "Generic<T>", "IRelease.Count"),
        Verify.DefaultInterfaceMember(2, "Generic<T>", "IRelease.Release()"),
        Verify.DefaultInterfaceMember(3, "CannotImplement", "ISealed.Help()"),
        Verify.DefaultInterfaceMember(4, "LeavesToDerivedInterface", "IBase.Release()"));

    [TestMethod]
    public Task ReadOnlyReference_AFieldThatIsReachedThroughOneIsReadOnlyToo() => Verify.AnalyzerAsync(
        """
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Inner
        {
            public int Count;

            public void Touch() => Count++;

            public readonly int Read() => Count;

            public bool Bump
            {
                get
                {
                    Count++;
                    return true;
                }
            }
        }

        [NonCopyable]
        struct Outer
        {
            public Inner Inner;

            // In a readonly member 'this' is a read-only reference, and so are its fields.
            public readonly void ReadOnlyMember()
            {
                {|#0:Inner|}.Touch();
                int count = Inner.Read();
            }

            public void Member() => Inner.Touch();
        }

        class C
        {
            readonly Outer _fixed;
            Outer _outer;

            void M(in Outer byIn, ref readonly Outer byRefReadonly, ref Outer byRef, in (Inner inner, int number) tuple)
            {
                // The compiler runs a member that is not readonly on a defensive copy of the field.
                {|#1:byIn.Inner|}.Touch();
                {|#2:byRefReadonly.Inner|}.Touch();
                {|#3:_fixed.Inner|}.Touch();
                {|#4:tuple.inner|}.Touch();
                bool bumped = {|#5:byIn.Inner|}.Bump;

                // A readonly member needs no copy, and neither does a field.
                int a = byIn.Inner.Read();
                int b = _fixed.Inner.Count;

                // A reference that can be written through is used in place.
                _outer.Inner.Touch();
                byRef.Inner.Touch();
            }
        }
        """,
        Verify.MemberOnReadOnlyReference(0, "Inner", "Inner.Touch()"),
        Verify.MemberOnReadOnlyReference(1, "Inner", "Inner.Touch()"),
        Verify.MemberOnReadOnlyReference(2, "Inner", "Inner.Touch()"),
        Verify.MemberOnReadOnlyReference(3, "Inner", "Inner.Touch()"),
        Verify.MemberOnReadOnlyReference(4, "Inner", "Inner.Touch()"),
        Verify.MemberOnReadOnlyReference(5, "Inner", "Inner.Bump"));

    [TestMethod]
    public Task StoredValue_TheFindingSaysWhatTakesTheCopy() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;

        class C
        {
            Guard _guard;

            static void Consume(Guard guard) { }

            static void ConsumeMany(__arglist) { }

            void M()
            {
                Consume({|#0:_guard|});
                var tuple = ({|#1:_guard|}, 1);
                var array = new[] { {|#2:_guard|} };
                Action dispose = {|#3:_guard|}.Dispose;
                string text = $"{ {|#4:_guard|} }";
                var list = new List<Guard> { {|#5:_guard|} };
                List<Guard> collection = [{|#6:_guard|}];
                Span<Guard> memory = stackalloc Guard[] { {|#7:_guard|} };
                ConsumeMany(__arglist({|#8:_guard|}));
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "'C.Consume(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(1, "The tuple takes", "Guard"),
        Verify.CopyOfStoredValue(2, "The array takes", "Guard"),
        Verify.CopyOfStoredValue(3, "The delegate for 'Guard.Dispose()' captures", "Guard"),
        Verify.CopyOfStoredValue(4, "The interpolated string takes", "Guard"),
        Verify.CopyOfStoredValue(5, "'List<Guard>.Add(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(6, "The collection takes", "Guard"),
        Verify.CopyOfStoredValue(7, "The stackalloc memory takes", "Guard"),
        Verify.CopyOfStoredValue(8, "An argument list takes", "Guard"));
}
