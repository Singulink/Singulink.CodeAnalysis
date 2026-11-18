using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of what copies a value without an assignment being written: a record, <c>with</c> and <c>MemberwiseClone</c>.
/// </summary>
/// <remarks>
/// Docs: articles/concepts/non-copyable-types.md.
/// </remarks>
[PrefixTestClass]
public class RecordAndCloneTests
{
    [TestMethod]
    public Task Record_CannotHaveAFieldOrAnAutoPropertyOfANonCopyableType() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        // A record copies its fields in 'with', in its copy constructor and in Equals, GetHashCode and ToString, which the compiler writes.
        record class WithField
        {
            public Guard {|#0:Field|};
            Guard? {|#1:_maybe|};
            ValueTask<Guard> {|#2:_pending|};
        }

        record class WithPositionalAndField(int Number)
        {
            public Guard {|#3:Field|} = default;
        }

        record struct StructWithField
        {
            public Guard {|#4:Field|};
        }

        // Marking the record non-copyable does not help: the members that the compiler writes still copy the field.
        [NonCopyable]
        record struct MarkedStructWithField
        {
            public Guard {|#5:Field|};
        }

        // An auto-property and a positional parameter are reported once, as an auto-property.
        record class WithProperty
        {
            public Guard {|#6:Property|} { get; init; }
        }

        record class Positional(Guard {|#7:Guard|}, int Number);

        [NonCopyable]
        record struct PositionalStruct(Guard {|#8:Guard|});

        // A static field is not copied with the record, a container is a reference that the copy shares, and a property with a body has no
        // field.
        record class Fine
        {
            static Guard s_guard;
            List<Guard> _list = new();
            Guard[] _array = new Guard[1];
            Task<Guard>? _task;
            public Guard Computed => default;
            public ref Guard First => ref _array[0];
        }

        // A class that is not a record can have the field.
        class NotARecord
        {
            public Guard Field;
        }

        {{Sources.Guard}}
        """,
        Verify.FieldOfRecord(0, "Guard", "WithField.Field"),
        Verify.FieldOfRecord(1, "Guard?", "WithField._maybe"),
        Verify.FieldOfRecord(2, "System.Threading.Tasks.ValueTask<Guard>", "WithField._pending"),
        Verify.FieldOfRecord(3, "Guard", "WithPositionalAndField.Field"),
        Verify.FieldOfRecord(4, "Guard", "StructWithField.Field"),
        Verify.FieldOfRecord(5, "Guard", "MarkedStructWithField.Field"),
        Verify.AutoProperty(6, "Guard", "WithProperty.Property"),
        Verify.AutoProperty(7, "Guard", "Positional.Guard"),
        Verify.AutoProperty(8, "Guard", "PositionalStruct.Guard"));

    [TestMethod]
    public Task With_CopiesAStoredValueAndMovesANewOne() => Verify.AnalyzerAsync(
        """
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Options
        {
            public int Size;
            public int Count;
        }

        class C
        {
            Options _field;

            static Options Create() => default;

            static void Take(Options options) { }

            void M(Options parameter, (Options options, int number) tuple)
            {
                var local = Create();

                // 'with' on a value of a non-copyable type is a copy of that value, and follows the rules for one.
                var a = {|#0:local|} with { Size = 1 };
                var b = {|#1:_field|} with { Size = 1 };
                _field = {|#2:local|} with { };
                Take({|#3:local|} with { Count = 3 });

                // A new value is moved into the copy, and what 'with' gives is a new value. A variable that is not used again
                // is one.
                var c = parameter with { Size = 1, Count = 2 };
                var d = tuple with { number = 1 };
                var e = Create() with { Size = 1 };
                var f = new Options() with { Size = 1 };
                var g = default(Options) with { Size = 1 } with { Count = 2 };
                _field = Create() with { Size = 1 };
                Take(Create() with { Size = 1 });
                var h = NonCopyable.Move(ref local) with { Size = 1 };
            }
        }
        """,
        Verify.CopyOfStoredValue(0, "'with' changes", "Options"),
        Verify.CopyOfStoredValue(1, "'with' changes", "Options"),
        Verify.CopyOfStoredValue(2, "'with' changes", "Options"),
        Verify.CopyOfStoredValue(3, "'with' changes", "Options"));

    [TestMethod]
    public Task MemberwiseClone_CopiesEveryFieldOfANonCopyableType() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        class Holder
        {
            Guard _guard;

            public Holder Clone() => (Holder){|#0:MemberwiseClone()|};

            public Holder CloneOther(Holder other) => (Holder){|#1:other.MemberwiseClone()|};
        }

        class Cloneable : ICloneable
        {
            ValueTask<Guard> _pending;

            public object Clone() => {|#2:this.MemberwiseClone()|};
        }

        // An inherited field counts, and so does the field behind an auto-property, which is reported where it is declared as well.
        class Derived : Holder
        {
            public Derived CloneDerived() => (Derived){|#3:MemberwiseClone()|};
        }

        class WithProperty
        {
            public Guard {|#4:Guard|} { get; set; }

            public object Clone() => {|#5:MemberwiseClone()|};
        }

        class Generic<[NonCopyable] T>
        {
            T _value = default!;

            public object Clone() => {|#6:MemberwiseClone()|};
        }

        // In a non-copyable struct the call boxes a copy of the struct and clones the box.
        [NonCopyable]
        struct Wrapper
        {
            Guard _guard;

            public object Clone() => {|#7:MemberwiseClone()|};
        }

        class GenericWithContainer<[NonCopyable] T>
        {
            List<T> _list = new();

            public object Clone() => MemberwiseClone();
        }

        // A container is a reference that the clone shares, and a static field is not cloned.
        class Fine
        {
            static Guard s_guard;
            List<Guard> _list = new();
            Guard[] _array = new Guard[1];
            Task<Guard>? _task;
            Memory<Guard> _memory;
            int _number;

            public Fine Clone() => (Fine)MemberwiseClone();
        }

        {{Sources.Guard}}
        """,
        Verify.MemberwiseCloneCall(0, "Holder", "Guard", "_guard"),
        Verify.MemberwiseCloneCall(1, "Holder", "Guard", "_guard"),
        Verify.MemberwiseCloneCall(2, "Cloneable", "System.Threading.Tasks.ValueTask<Guard>", "_pending"),
        Verify.MemberwiseCloneCall(3, "Derived", "Guard", "_guard"),
        Verify.AutoProperty(4, "Guard", "WithProperty.Guard"),
        Verify.MemberwiseCloneCall(5, "WithProperty", "Guard", "Guard"),
        Verify.MemberwiseCloneCall(6, "Generic<T>", "T", "_value"),
        Verify.MemberwiseCloneCallOnValue(7, "Wrapper"));

    [TestMethod]
    public Task MemberwiseClone_CalledOnBaseClonesTheObjectAllTheSame() => Verify.AnalyzerAsync(
        $$"""
        class WithGuard
        {
            public Guard Guard;

            public WithGuard CloneThroughBase() => (WithGuard){|#0:base.MemberwiseClone()|};

            public WithGuard CloneThroughThis() => (WithGuard){|#1:this.MemberwiseClone()|};
        }

        class WithNumber
        {
            public int Number;

            public WithNumber Clone() => (WithNumber)base.MemberwiseClone();
        }

        {{Sources.Guard}}
        """,
        Verify.MemberwiseCloneCall(0, "WithGuard", "Guard", "Guard"),
        Verify.MemberwiseCloneCall(1, "WithGuard", "Guard", "Guard"));
}
