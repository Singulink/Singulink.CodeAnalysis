using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of generic structs over a non-copyable type, which are non-copyable types themselves, and of views, which are not.
/// </summary>
/// <remarks>
/// Docs: articles/concepts/non-copyable-types.md.
/// </remarks>
[PrefixTestClass]
public class GenericStructTests
{
    [TestMethod]
    public Task GenericStructOverANonCopyableType_IsNonCopyable() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class C
        {
            ValueTask<Guard> _pendingField;

            static KeyValuePair<int, Guard> MakePair() => default;

            static void Take(ValueTask<Guard> pending) { }

            static void TakeRef(ref ValueTask<Guard> pending) { }

            void M(RwLock rwLock)
            {
                ValueTask<Guard> pending = rwLock.EnterAsync();
                ValueTask<Guard> copy {|#0:= pending|};

                KeyValuePair<int, Guard> pair = MakePair();
                var other {|#1:= pair|};

                Guard? maybe = rwLock.TryEnter();
                Guard? maybeCopy {|#2:= maybe|};

                var tuple = rwLock.TryEnterTuple();
                var tupleCopy {|#3:= tuple|};

                // At any depth.
                ((Guard guard, int number) inner, string name) nested = default;
                var nestedCopy {|#4:= nested|};
                KeyValuePair<int, Guard?> pairOfNullable = default;
                var pairOfNullableCopy {|#5:= pairOfNullable|};

                // Each is read again here, so none of the reads above is the last use of its variable.
                _ = (pair, maybe, tuple, nested, pairOfNullable);

                // The existing rules for values apply.
                Take({|#6:pending|});
                Take(rwLock.EnterAsync());
                TakeRef(ref pending);
                _pendingField = rwLock.EnterAsync();
                _pendingField = {|#7:pending|};
                ref ValueTask<Guard> reference = ref pending;
                bool completed = pending.IsCompleted;
            }

            ValueTask<Guard> Return(RwLock rwLock) => rwLock.EnterAsync();

            ValueTask<Guard> ReturnField() => {|#8:_pendingField|};
        }

        struct Copyable
        {
            ValueTask<Guard> {|#9:_pending|};
            Guard? {|#10:_maybe|};
            List<Guard> _list;
        }

        class WithProperties
        {
            public ValueTask<Guard> {|#11:Pending|} { get; set; }
            public List<Guard> Guards { get; set; }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignment(0, "System.Threading.Tasks.ValueTask<Guard>"),
        Verify.CopyByAssignment(1, "System.Collections.Generic.KeyValuePair<int, Guard>"),
        Verify.CopyByAssignment(2, "Guard?"),
        Verify.CopyByAssignment(3, "(Guard guard, bool ok)"),
        Verify.CopyByAssignment(4, "((Guard guard, int number) inner, string name)"),
        Verify.CopyByAssignment(5, "System.Collections.Generic.KeyValuePair<int, Guard?>"),
        Verify.CopyOfStoredValue(6, "'C.Take(ValueTask<Guard>)' receives", "System.Threading.Tasks.ValueTask<Guard>"),
        Verify.CopyByAssignment(7, "System.Threading.Tasks.ValueTask<Guard>"),
        Verify.CopyByReturn(8, "System.Threading.Tasks.ValueTask<Guard>"),
        Verify.FieldOfCopyableType(9, "System.Threading.Tasks.ValueTask<Guard>", "Copyable._pending"),
        Verify.FieldOfCopyableType(10, "Guard?", "Copyable._maybe"),
        Verify.AutoProperty(11, "System.Threading.Tasks.ValueTask<Guard>", "WithProperties.Pending"));

    [TestMethod]
    public Task View_IsAContainerAndCanBeCopied() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;

        class C
        {
            Memory<Guard> _memory;

            void Process(Span<Guard> guards) { }

            void M(Span<Guard> span, ReadOnlySpan<Guard> readOnlySpan, [Singulink.CodeAnalysis.Kept] Memory<Guard> memory, ReadOnlyMemory<Guard> readOnlyMemory, ArraySegment<Guard> segment)
            {
                Process(span);

                var spanCopy = span;
                var readOnlySpanCopy = readOnlySpan;
                var memoryCopy = memory;
                var readOnlyMemoryCopy = readOnlyMemory;
                var segmentCopy = segment;
                _memory = memory;
            }

            // Containers that are reference types can be copied as well.
            void N(List<Guard> list, Guard[] array, List<Guard>[] lists)
            {
                var listCopy = list;
                var arrayCopy = array;
                var listsCopy = lists;
                var tupleOfContainers = (list, array);
                var tupleCopy = tupleOfContainers;
            }
        }

        struct Copyable
        {
            Memory<Guard> _memory;
            ArraySegment<Guard> _segment;
        }

        {{Sources.Guard}}
        """);

    [TestMethod]
    public Task Nullable_HasNoSpecialRule() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            Guard? _field;

            static void TakeIn(in Guard? maybe) { }

            Guard? Wrap(RwLock rwLock) => rwLock.Enter();

            Guard? WrapLocal(RwLock rwLock)
            {
                var guard = rwLock.Enter();
                return guard;
            }

            Guard? None() => null;

            void M(RwLock rwLock, Guard parameter)
            {
                Guard? maybe = rwLock.TryEnter();
                Guard? wrapped = rwLock.Enter();
                Guard? none = null;
                maybe = null;
                maybe = default;
                _field = rwLock.TryEnter();
                TakeIn(in maybe);

                // Tests of the value read nothing out of it.
                bool hasValue = maybe.HasValue;
                bool isNull = maybe == null;
                bool isNotNull = maybe != null;
                bool isNullByPattern = maybe is null;
                bool isNotNullByPattern = maybe is not null;
                bool isSomething = maybe is { };

                // Wrapping a stored value copies it. The last use of the parameter moves it in.
                Guard? copy {|#0:= parameter|};
                maybe = parameter;

                // Reading the value of a stored nullable copies it. Read from a new one it is a new value.
                Guard value = {|#2:maybe.Value|};
                Guard valueOrDefault = {|#3:maybe.GetValueOrDefault()|};
                Guard coalesced {|#4:= maybe ?? rwLock.Enter()|};
                Guard fromNew = rwLock.TryEnter().Value;
                Guard coalescedFromNew = rwLock.TryEnter() ?? rwLock.Enter();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignment(0, "Guard?"),
        Verify.ReadFromStoredContainer(2, "'Nullable<T>.Value'", "Guard"),
        Verify.ReadFromStoredContainer(3, "'Nullable<T>.GetValueOrDefault()'", "Guard"),
        Verify.CopyByAssignment(4, "Guard"));
}
