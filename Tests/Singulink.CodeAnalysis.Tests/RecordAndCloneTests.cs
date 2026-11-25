using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of what copies a value without an assignment being written: a record, <c>with</c> and <c>MemberwiseClone</c>.
/// </summary>
/// <remarks>
/// Docs: articles/rules/non-copyable-types.md.
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
                var h = NonCopyable.Move(ref {|#4:local|}) with { Size = 1 };
            }
        }
        """,
        Verify.UsedAfterHandOff(2, "local", "Options", "\'with\' copied its value", 0),
        Verify.UsedAfterHandOff(3, "local", "Options", "\'with\' copied its value", 2),
        Verify.UsedAfterHandOff(4, "local", "Options", "\'with\' copied its value", 3),
        Verify.CopyOfStoredValue(1, "'with' changes", "Options"));

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

    // MarshalByRefObject.MemberwiseClone(bool), which Stream and Component inherit, clones the object as well, and a method group of
    // either clones when the delegate is invoked.
    [TestMethod]
    public Task MemberwiseClone_OfMarshalByRefObjectAndAsAMethodGroupIsReported() => Verify.AnalyzerAsync(
        $$"""
        using System;

        class Holder : MarshalByRefObject
        {
            private Guard _guard;

            public object Copy() => {|#0:MemberwiseClone(false)|};

            public Func<object> Cloner() => {|#1:MemberwiseClone|};

            public Func<bool, MarshalByRefObject> RemoteCloner() => {|#2:MemberwiseClone|};
        }

        {{Sources.Guard}}
        """,
        Verify.MemberwiseCloneCall(0, "Holder", "Guard", "_guard"),
        Verify.MemberwiseCloneCall(1, "Holder", "Guard", "_guard"),
        Verify.MemberwiseCloneCall(2, "Holder", "Guard", "_guard"));

    // Where the class of the receiver has no field of a non-copyable type, the object that is cloned can be of a derived class that has
    // one: a derived class of the compilation is a definite copy (SCA1005), a class that another assembly can derive from a possible one
    // (SCA1008). A sealed class and a struct have no derived class, and a class that no other assembly can derive from has none there.
    [TestMethod]
    public Task MemberwiseClone_OfAClassWhoseDerivedClassCanHoldANonCopyableValue() => Verify.AnalyzerAsync(
        $$"""
        public class Open
        {
            public object Copy() => {|#0:MemberwiseClone()|};
        }

        public class Base
        {
            public object Copy() => {|#1:MemberwiseClone()|};
        }

        internal class Middle : Base { }

        internal class Derived : Middle
        {
            private Guard _guard;
        }

        public sealed class Sealed
        {
            public object Copy() => MemberwiseClone();
        }

        public struct Plain
        {
            public object Copy() => MemberwiseClone();
        }

        internal class Hidden
        {
            public object Copy() => MemberwiseClone();
        }

        public class OnlyPrivateConstructor
        {
            private OnlyPrivateConstructor() { }

            public object Copy() => MemberwiseClone();
        }

        internal class Outer
        {
            public class Nested
            {
                public object Copy() => MemberwiseClone();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MemberwiseCloneOfOpenClass(0, "Open"),
        Verify.MemberwiseCloneOfDerivedClass(1, "Base", "Derived", "Guard", "_guard"));

    // An assembly with InternalsVisibleTo counts its internal types and constructors as reachable from another assembly.
    [TestMethod]
    public Task MemberwiseClone_OfAnInternalClassOfAnAssemblyWithInternalsVisibleTo() => Verify.AnalyzerAsync(
        """
        [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Friend")]

        internal class Hidden
        {
            internal Hidden() { }

            public object Copy() => {|#0:MemberwiseClone()|};
        }
        """,
        Verify.MemberwiseCloneOfOpenClass(0, "Hidden"));

    // An object of a type parameter is of the class that the type parameter is constrained to, or of one derived from it.
    [TestMethod]
    public Task MemberwiseClone_OnAnObjectOfATypeParameterIsJudgedByItsConstraintClass() => Verify.AnalyzerAsync(
        $$"""
        using System;

        internal class Holder
        {
            private Guard _guard;

            public static T Clone<T>(T t) where T : Holder => (T){|#0:t.MemberwiseClone()|};

            public static Func<object> Cloner<T>(T t) where T : Holder => {|#1:t.MemberwiseClone|};

            public static T CloneThrough<T, U>(T t) where T : U where U : Holder => (T){|#2:t.MemberwiseClone()|};
        }

        public class Open
        {
            public static T Clone<T>(T t) where T : Open => (T){|#3:t.MemberwiseClone()|};
        }

        internal class Closed
        {
            public static T Clone<T>(T t) where T : Closed => (T)t.MemberwiseClone();
        }

        {{Sources.Guard}}
        """,
        Verify.MemberwiseCloneCall(0, "Holder", "Guard", "_guard"),
        Verify.MemberwiseCloneCall(1, "Holder", "Guard", "_guard"),
        Verify.MemberwiseCloneCall(2, "Holder", "Guard", "_guard"),
        Verify.MemberwiseCloneOfOpenClass(3, "Open"));

    // A class can be derived from in another assembly through a class of the compilation that derives from it, and a protected nested
    // class can be reached there only where the class that contains it can be derived from.
    [TestMethod]
    public Task MemberwiseClone_OfAClassThatAnotherAssemblyCanDeriveFromThroughASubclass() => Verify.AnalyzerAsync(
        """
        public abstract class Node
        {
            internal Node() { }

            public Node Clone() => (Node){|#0:MemberwiseClone()|};
        }

        public class Element : Node { }

        public class Outer
        {
            private Outer() { }

            public object Copy() => {|#1:MemberwiseClone()|};

            public class Derived : Outer { }
        }

        public class Shut
        {
            private Shut() { }

            public object Copy() => MemberwiseClone();

            protected class Inner : Shut
            {
                public Inner() { }

                public object CopyInner() => MemberwiseClone();
            }

            protected internal class Both
            {
                public object Copy() => MemberwiseClone();
            }
        }

        public class Open
        {
            protected class Inner
            {
                public object Copy() => {|#2:MemberwiseClone()|};
            }
        }
        """,
        Verify.MemberwiseCloneOfOpenClass(0, "Node"),
        Verify.MemberwiseCloneOfOpenClass(1, "Outer"),
        Verify.MemberwiseCloneOfOpenClass(2, "Open.Inner"));

    // A file-local class is never reachable from another assembly, and a private protected nested class only where internals are visible
    // and the class that contains it can be derived from there.
    [TestMethod]
    public Task MemberwiseClone_OfAFileLocalClassOrAShutNestedClassOfAnAssemblyWithInternalsVisibleTo() => Verify.AnalyzerAsync(
        """
        [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Friend")]

        file class Local
        {
            public object Copy() => MemberwiseClone();
        }

        public class Shut
        {
            private Shut() { }

            private protected class Inner
            {
                public object Copy() => MemberwiseClone();
            }
        }

        public class Open
        {
            private protected class Inner
            {
                public object Copy() => {|#0:MemberwiseClone()|};
            }
        }
        """,
        Verify.MemberwiseCloneOfOpenClass(0, "Open.Inner"));

    // A derived class holds what its constructed base classes declare, and a captured parameter of a primary constructor is named as
    // the parameter. A closed constructed base class of another instantiation is not the class that is cloned.
    [TestMethod]
    public Task MemberwiseClone_OfAClassWhoseDerivedClassHoldsAValueThroughItsBaseOrItsPrimaryConstructor() => Verify.AnalyzerAsync(
        $$"""
        internal class Base
        {
            public object Copy() => {|#0:MemberwiseClone()|};
        }

        internal class Generic<T> : Base
        {
            public T Value = default!;
        }

        internal sealed class Derived : Generic<Guard> { }

        internal class Other
        {
            public object Copy() => {|#1:MemberwiseClone()|};
        }

        internal class Captures(Guard guard) : Other
        {
            public override string ToString() => guard.Value.ToString();
        }

        internal class Own(Guard guard)
        {
            public object Copy() => {|#2:MemberwiseClone()|};

            public override string ToString() => guard.Value.ToString();
        }

        internal class Instantiated<T>
        {
            public object Copy(Instantiated<string> other) => other.MemberwiseClone();

            public object CopyAny(Instantiated<T> other) => {|#3:other.MemberwiseClone()|};
        }

        internal sealed class OfInt : Instantiated<int>
        {
            private Guard _guard;
        }

        {{Sources.Guard}}
        """,
        Verify.MemberwiseCloneOfDerivedClass(0, "Base", "Derived", "Guard", "Value"),
        Verify.MemberwiseCloneOfDerivedClass(1, "Other", "Captures", "Guard", "guard", isParameter: true),
        Verify.MemberwiseCloneCall(2, "Own", "Guard", "guard", isParameter: true),
        Verify.MemberwiseCloneOfDerivedClass(3, "Instantiated<T>", "OfInt", "Guard", "_guard"));

    // RuntimeHelpers.GetObjectValue clones a boxed struct: every use is reported, a call and a method group, whatever the argument.
    [TestMethod]
    public Task GetObjectValue_EveryUseIsReported() => Verify.AnalyzerAsync(
        """
        using System;
        using System.Runtime.CompilerServices;

        class C
        {
            object M(object box, string text)
            {
                object a = {|#0:RuntimeHelpers.GetObjectValue(box)|};
                object b = {|#1:RuntimeHelpers.GetObjectValue(text)|};
                Func<object, object> clone = {|#2:RuntimeHelpers.GetObjectValue|};
                return a;
            }
        }
        """,
        Verify.GetObjectValueCall(0),
        Verify.GetObjectValueCall(1),
        Verify.GetObjectValueCall(2));
}
