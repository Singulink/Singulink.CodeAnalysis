using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of marked type parameters, which are checked exactly, of <c>[RequiresCopyable]</c> and of the marks that must be kept.
/// </summary>
/// <remarks>
/// Docs: articles/guides/marking-a-type-parameter.md.
/// </remarks>
[PrefixTestClass]
public class TypeParameterTests
{
    [TestMethod]
    public Task MarkedTypeParameter_IsANonCopyableTypeInsideTheGeneric() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        public sealed class Pool<[NonCopyable] T> where T : struct
        {
            private readonly T[] _items = new T[16];
            private T _spare;

            public ref T this[int index] => ref _items[index];

            public T Get(int index) => {|#0:_items[index]|};

            public T Create() => default;

            public void Swap(int index)
            {
                T copy = {|#1:_items[index]|};
                Take({|#2:_spare|});
                TakeRef(ref _spare);
                ref T element = ref _items[index];
                int length = _items.Length;
            }

            private void Take(T value) { }

            private void TakeRef(ref T value) { }
        }

        static class Methods
        {
            public static T Dup<[NonCopyable] T>(in T value) => {|#3:value|};

            public static ref readonly T Same<[NonCopyable] T>(in T value) => ref value;

            // A type parameter that is not marked is not checked.
            public static T Unchecked<T>(in T value) => value;
        }

        class C
        {
            void M(RwLock rwLock)
            {
                var pool = new Pool<Guard>();
                ref Guard g = ref pool[0];
                pool[1].Dispose();
                pool[2] = rwLock.Enter();

                // A by-value result of checked code is a new value.
                Guard created = pool.Create();
                Guard taken = pool.Get(0);

                Guard copy = {|#4:pool[0]|};

                var a = rwLock.Enter();
                ref readonly Guard same = ref Methods.Same(in a);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByReturn(0, "T"),
        Verify.CopyByAssignment(1, "T"),
        Verify.CopyOfStoredValue(2, "'Pool<T>.Take(T)' receives", "T"),
        Verify.CopyByReturn(3, "T"),
        Verify.CopyByAssignment(4, "Guard"));

    [TestMethod]
    public Task MarkedTypeParameter_TakingAValueOutNeedsAMove() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        public sealed class Stack<[NonCopyable] T>
        {
            private T[] _items = new T[16];
            private int _count;

            // A by-value parameter is moved from at its last use.
            public void Push(T value) => _items[_count++] = value;

            public void PushMoved(T value) => _items[_count++] = NonCopyable.Move(ref value);

            public void PushTwice(T value)
            {
                _items[_count++] = {|#0:value|};
                _items[_count++] = {|#1:value|};
            }

            public T Pop() => NonCopyable.Move(ref _items[--_count]);

            public ref T Peek() => ref _items[_count - 1];
        }

        class C
        {
            void M(Stack<Guard> stack, RwLock rwLock)
            {
                stack.PushMoved(rwLock.Enter());
                Guard top = stack.Pop();
                ref Guard peeked = ref stack.Peek();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOff(1, "value", "T", "its value was assigned", 0));

    [TestMethod]
    public Task MarkedTypeParameter_StructAndFields() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        // A generic struct over its own marked type parameter is non-copyable, so the field is allowed.
        struct Holder<[NonCopyable] T>
        {
            public T Value;
        }

        class Box<[NonCopyable] T>
        {
            public T Value;
            public T {|#0:Property|} { get; set; }

            [RequiresCopyable]
            public T CopyableProperty { get; set; }
        }

        class C
        {
            void M(Holder<int> numbers, Holder<Guard> guards)
            {
                var numbersCopy = numbers;
                var guardsCopy = {|#1:guards|};
                var last = {|#2:guards|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.AutoProperty(0, "T", "Box<T>.Property"),
        Verify.UsedAfterHandOff(2, "guards", "Holder<Guard>", "its value was assigned to \'guardsCopy\'", 1));

    [TestMethod]
    public Task RequiresCopyable_MemberIsNotCheckedAndCannotBeUsedWithANonCopyableType() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        public sealed class Pool<[NonCopyable] T> where T : struct
        {
            private readonly T[] _items = new T[16];

            public Pool() { }

            [RequiresCopyable]
            public Pool(T[] items) => _items = (T[])items.Clone();

            [RequiresCopyable]
            public T[] ToArray() => (T[])_items.Clone();

            [RequiresCopyable]
            public T First => _items[0];

            public T Last
            {
                [RequiresCopyable]
                get => _items[^1];
            }

            [RequiresCopyable]
            public event Action<T> Changed { add { T copy = _items[0]; } remove { } }

            // Inside a member that requires a copyable type, the type parameter counts as copyable.
            [RequiresCopyable]
            public T[] ToArrayTwice() => ToArray();

            // A checked member cannot use it: the type parameter is a non-copyable type there.
            public int Count() => {|#0:ToArray()|}.Length;
        }

        class C
        {
            void M(Pool<Guard> guards, Pool<int> numbers, [Kept] Guard[] guardArray, int[] numberArray)
            {
                Guard[] all = {|#1:guards.ToArray()|};
                int[] copy = numbers.ToArray();

                var guardPool = {|#2:new Pool<Guard>(guardArray)|};
                var numberPool = new Pool<int>(numberArray);

                Guard first = {|#3:guards.First|};
                int firstNumber = numbers.First;

                Guard last = {|#4:guards.Last|};

                {|#5:guards.Changed|} += g => { };
                numbers.Changed += n => { };

                Func<Guard[]> toArray = {|#6:guards.ToArray|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.RequiresCopyableMember(0, "Pool<T>.ToArray()", "T", "T"),
        Verify.RequiresCopyableMember(1, "Pool<T>.ToArray()", "Guard", "T"),
        Verify.RequiresCopyableMember(2, "Pool<T>.Pool(T[])", "Guard", "T"),
        Verify.RequiresCopyableMember(3, "Pool<T>.First", "Guard", "T"),
        Verify.RequiresCopyableMember(4, "Pool<T>.Last", "Guard", "T"),
        Verify.RequiresCopyableMember(5, "Pool<T>.Changed", "Guard", "T"),
        Verify.RequiresCopyableMember(6, "Pool<T>.ToArray()", "Guard", "T"));

    [TestMethod]
    public Task RequiresCopyable_GenericMethod() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        static class Methods
        {
            [RequiresCopyable]
            public static T[] Repeat<[NonCopyable] T>(in T value, int count)
            {
                var result = new T[count];

                for (int i = 0; i < count; i++)
                    result[i] = value;

                return result;
            }
        }

        class C
        {
            void M(RwLock rwLock)
            {
                var guard = rwLock.Enter();
                var guards = {|#0:Methods.Repeat(in guard, 2)|};
                var numbers = Methods.Repeat(1, 2);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.RequiresCopyableMember(0, "Methods.Repeat<T>(in T, int)", "Guard", "T"));

    [TestMethod]
    public Task MarksMustBeKept_TypeParameterPassedToABaseType() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        class Base<[NonCopyable] T> { }

        interface IPool<[NonCopyable] T> { }

        interface IPlain<T> { }

        class Outer<[NonCopyable] T>
        {
            public class Inner { }
        }

        class Kept<[NonCopyable] T> : Base<T>, IPool<T> { }

        class Dropped<{|#0:T|}> : Base<T> { }

        class DroppedForInterface<{|#1:T|}> : IPool<T> { }

        class DroppedForContainingType<{|#2:T|}> : Outer<T>.Inner { }

        class Two<[NonCopyable] T, {|#3:U|}> : Base<U> { }

        // Nothing is promised for the type parameter of the interface, and a type that is not a type parameter needs no mark.
        class Plain<[NonCopyable] T> : IPlain<T> { }

        class Closed : Base<Guard> { }

        class OfArray<T> : Base<T[]> { }

        class Container<T>
        {
            class {|#4:Nested|} : Base<T> { }
        }

        {{Sources.Guard}}
        """,
        Verify.TypeParameterMarkNotPassedOn(0, "T", "T", "Base<T>"),
        Verify.TypeParameterMarkNotPassedOn(1, "T", "T", "IPool<T>"),
        Verify.TypeParameterMarkNotPassedOn(2, "T", "T", "Outer<T>"),
        Verify.TypeParameterMarkNotPassedOn(3, "U", "T", "Base<T>"),
        Verify.TypeParameterMarkNotPassedOn(4, "T", "T", "Base<T>"));

    [TestMethod]
    public Task MarksMustBeKept_OverridesAndImplementations() => Verify.AnalyzerAsync(
        """
        using Singulink.CodeAnalysis;

        abstract class Base
        {
            public abstract void Marked<[NonCopyable] T>(ref T value);

            public abstract void Plain<T>(ref T value);

            public abstract void Checked();

            [RequiresCopyable]
            public abstract void Copying();

            public abstract int Property { get; }
        }

        class Kept : Base
        {
            public override void Marked<[NonCopyable] T>(ref T value) { }

            public override void Plain<T>(ref T value) { }

            public override void Checked() { }

            // A member may be stricter than the one it overrides.
            public override void Copying() { }

            public override int Property => 0;
        }

        class Changed : Base
        {
            public override void Marked<{|#0:T|}>(ref T value) { }

            // Marking more than the overridden member is allowed: it only makes the body stricter.
            public override void Plain<[NonCopyable] T>(ref T value) { }

            [RequiresCopyable]
            public override void {|#2:Checked|}() { }

            [RequiresCopyable]
            public override void Copying() { }

            [RequiresCopyable]
            public override int {|#3:Property|} => 0;
        }

        interface IWork
        {
            void Marked<[NonCopyable] T>(ref T value);

            void Checked();

            [RequiresCopyable]
            void Copying();
        }

        class Implicit : IWork
        {
            public void Marked<{|#4:T|}>(ref T value) { }

            [RequiresCopyable]
            public void {|#5:Checked|}() { }

            [RequiresCopyable]
            public void Copying() { }
        }

        class Explicit : IWork
        {
            void IWork.Marked<{|#6:T|}>(ref T value) { }

            [RequiresCopyable]
            void IWork.{|#7:Checked|}() { }

            void IWork.Copying() { }
        }
        """,
        Verify.TypeParameterMarkNotKept(0, "T", "Changed.Marked<T>(ref T)", "T", "Base.Marked<T>(ref T)"),
        Verify.RequiresCopyableNotOnBaseMember(2, "Changed.Checked()", "Base.Checked()"),
        Verify.RequiresCopyableNotOnBaseMember(3, "Changed.Property", "Base.Property"),
        Verify.TypeParameterMarkNotKept(4, "T", "Implicit.Marked<T>(ref T)", "T", "IWork.Marked<T>(ref T)"),
        Verify.RequiresCopyableNotOnBaseMember(5, "Implicit.Checked()", "IWork.Checked()"),
        Verify.TypeParameterMarkNotKept(6, "T", "Explicit.IWork.Marked<T>(ref T)", "T", "IWork.Marked<T>(ref T)"),
        Verify.RequiresCopyableNotOnBaseMember(7, "Explicit.IWork.Checked()", "IWork.Checked()"));

    [TestMethod]
    public Task MarksMustBeKept_ImplementationThatABaseClassDeclares() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        // A member that implements an interface member without saying so, which a derived class brings to the interface. Nothing at the
        // member says that it implements anything, so the finding is on the class that makes it one.
        interface IDup { T Dup<[NonCopyable] T>(in T value); }

        class Unmarked { public T Dup<T>(in T value) => value; }

        class {|#0:BringsUnmarked|} : Unmarked, IDup { }

        class Marked { public T Dup<[NonCopyable] T>(in T value) => throw null!; }

        class BringsMarked : Marked, IDup { }

        // The same for a member of a base class whose type parameter is not marked: it is not checked code for the type that the interface
        // promises not to copy.
        interface ISource<[NonCopyable] T> { T Get(); }

        class UnmarkedSource<T> { protected T _value = default!; public T Get() => _value; }

        class {|#1:Brings|}<[NonCopyable] T> : UnmarkedSource<T>, ISource<T> { }

        class {|#2:BringsForGuard|} : UnmarkedSource<Guard>, ISource<Guard> { }

        class BringsForCopyable : UnmarkedSource<int>, ISource<int> { }

        class MarkedSource<[NonCopyable] T> { public T Get() => default!; }

        class BringsMarkedSource<[NonCopyable] T> : MarkedSource<T>, ISource<T> { }

        // And for [RequiresCopyable].
        interface IWork { void Run(); }

        class Copying { [RequiresCopyable] public void Run() { } }

        class {|#3:BringsCopying|} : Copying, IWork { }

        // A class that implements the interface itself answers for it, and a class that derives from it has nothing to add.
        class Own : IDup { public T Dup<{|#4:T|}>(in T value) => value; }

        class Derived : Own { }

        class DerivedAgain : Own, IDup { }

        {{Sources.Guard}}
        """,
        Verify.TypeParameterMarkNotKept(0, "T", "Unmarked.Dup<T>(in T)", "T", "IDup.Dup<T>(in T)"),
        Verify.ImplementationNotChecked(1, "UnmarkedSource<T>.Get()", "ISource<T>.Get()", "Brings<T>", "T", "T"),
        Verify.ErasableBaseNotKept(
            1, "Brings<T>", "ISource<T>", "'UnmarkedSource<T>' receives 'T' through type parameter 'T', which is not marked [NonCopyable]"),
        Verify.ImplementationNotChecked(2, "UnmarkedSource<T>.Get()", "ISource<T>.Get()", "BringsForGuard", "Guard", "T"),
        Verify.ErasableBaseNotKept(
            2, "BringsForGuard", "ISource<Guard>", "'UnmarkedSource<T>' receives 'Guard' through type parameter 'T', which is not marked [NonCopyable]"),
        Verify.RequiresCopyableNotOnBaseMember(3, "Copying.Run()", "IWork.Run()"),
        Verify.TypeParameterMarkNotKept(4, "T", "Own.Dup<T>(in T)", "T", "IDup.Dup<T>(in T)"));

    [TestMethod]
    public Task MarkedTypeParameter_OfAReferencedProjectIsTrusted()
    {
        var test = new Verify.Test {
            TestCode = $$"""
                class C
                {
                    void M(Library.Pool<Guard> pool, Library.Bag<Guard> bag)
                    {
                        ref Guard g = ref pool[0];
                        Guard taken = pool.Take(0);
                        Guard[] all = {|#0:pool.ToArray()|};

                        Guard peeked = {|#1:bag.Peek()|};
                    }
                }

                {{Sources.Guard}}
                """,
        };

        var library = test.TestState.AdditionalProjects["Library"];
        library.AdditionalReferences.Add(typeof(NonCopyableAttribute).Assembly);
        library.Sources.Add("""
            using Singulink.CodeAnalysis;

            // The project is built with the analyzer.
            [assembly: System.Reflection.AssemblyMetadata("Singulink.CodeAnalysis.Analyzed", "1")]

            namespace Library
            {
                public sealed class Pool<[NonCopyable] T>
                {
                    private readonly T[] _items = new T[16];

                    public ref T this[int index] => ref _items[index];

                    public T Take(int index) => NonCopyable.Move(ref _items[index]);

                    [RequiresCopyable]
                    public T[] ToArray() => (T[])_items.Clone();
                }

                // Not marked: it makes no difference that the generic is in the same solution.
                public sealed class Bag<T>
                {
                    private readonly T[] _items = new T[16];

                    public T Peek() => _items[0];
                }
            }
            """);

        test.TestState.AdditionalProjectReferences.Add("Library");
        test.ExpectedDiagnostics.Add(Verify.RequiresCopyableMember(0, "Pool<T>.ToArray()", "Guard", "T"));
        test.ExpectedDiagnostics.Add(Verify.UnlistedMember(1, "Bag<T>.Peek()", "Guard", "T", "M:Library.Bag`1.Peek"));

        return test.RunAsync();
    }

    [TestMethod]
    public Task MarksMustBeKept_WhereAnInterfaceIsImplementedAgain() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        interface IMove { T Take<[NonCopyable] T>(ref T value); }

        interface IMoveMore : IMove { }

        class Base : IMove { public T Take<[NonCopyable] T>(ref T value) => NonCopyable.Move(ref value); }

        // A derived class that names the interface again implements it again, with a member of its own.
        class Explicit : Base, IMove { T IMove.Take<{|#0:T|}>(ref T value) => value; }

        // And with a member that hides the one of the base class.
        class Hides : Base, IMove { public new T Take<{|#1:T|}>(ref T value) => value; }

        // An interface that inherits the interface names it as well.
        class ThroughDerivedInterface : Base, IMoveMore { T IMove.Take<{|#2:T|}>(ref T value) => value; }

        // Nothing changes hands here: the base class still answers for the interface.
        class NamesItOnly : Base, IMove { }

        class HidesOnly : Base { public new T Take<T>(ref T value) => value; }

        class KeepsTheMark : Base, IMove { T IMove.Take<[NonCopyable] T>(ref T value) => NonCopyable.Move(ref value); }

        // A derived interface can give a default implementation of a member of its base interface.
        interface IDefault { T Take<[NonCopyable] T>(ref T value) => NonCopyable.Move(ref value); }

        interface IReplaces : IDefault { T IDefault.Take<{|#3:T|}>(ref T value) => value; }

        interface IReplacesAndKeeps : IDefault { T IDefault.Take<[NonCopyable] T>(ref T value) => NonCopyable.Move(ref value); }

        interface IWork { void Run(); }

        interface IWorkCopying : IWork { [RequiresCopyable] void IWork.{|#4:Run|}() { } }
        """,
        Verify.TypeParameterMarkNotKept(0, "T", "Explicit.IMove.Take<T>(ref T)", "T", "IMove.Take<T>(ref T)"),
        Verify.TypeParameterMarkNotKept(1, "T", "Hides.Take<T>(ref T)", "T", "IMove.Take<T>(ref T)"),
        Verify.TypeParameterMarkNotKept(2, "T", "ThroughDerivedInterface.IMove.Take<T>(ref T)", "T", "IMove.Take<T>(ref T)"),
        Verify.TypeParameterMarkNotKept(3, "T", "IReplaces.IDefault.Take<T>(ref T)", "T", "IDefault.Take<T>(ref T)"),
        Verify.RequiresCopyableNotOnBaseMember(4, "IWorkCopying.IWork.Run()", "IWork.Run()"));

    [TestMethod]
    public Task MarkedTypeParameter_MemberThroughAReadOnlyReferenceRunsOnACopy() => Verify.AnalyzerAsync(
        """
        using System;
        using Singulink.CodeAnalysis;

        interface IGuard : IDisposable
        {
            bool IsHeld { get; }

            void Release();
        }

        sealed class Marked<[NonCopyable] T> where T : IGuard
        {
            private T _value = default!;
            private readonly T _fixed = default!;

            void M(in T byIn, ref readonly T byRefReadonly, ref T byRef, T byValue)
            {
                // The compiler cannot know that a member of a type parameter is readonly, so it runs every member on a copy.
                {|#0:byIn|}.Release();
                {|#1:_fixed|}.Release();
                bool held = {|#2:byIn|}.IsHeld;
                {|#3:byRefReadonly|}.Dispose();
                string text = {|#4:byIn|}.ToString();

                // A reference that can be written through is used in place.
                _value.Release();
                byRef.Release();
                byValue.Release();
                bool alsoHeld = _value.IsHeld;
            }
        }

        // A type parameter that is constrained to a class is a reference.
        sealed class OfReferences<[NonCopyable] T> where T : class, IGuard
        {
            private readonly T _fixed = default!;

            void M(in T byIn)
            {
                byIn.Release();
                _fixed.Release();
            }
        }
        """,
        Verify.MemberOnReadOnlyTypeParameter(0, "T", "IGuard.Release()"),
        Verify.MemberOnReadOnlyTypeParameter(1, "T", "IGuard.Release()"),
        Verify.MemberOnReadOnlyTypeParameter(2, "T", "IGuard.IsHeld"),
        Verify.MemberOnReadOnlyTypeParameter(3, "T", "IDisposable.Dispose()"),
        Verify.MemberOnReadOnlyTypeParameter(4, "T", "object.ToString()"));

    [TestMethod]
    public Task MarkedTypeParameter_ValueMustNotBeConvertedToATypeThatHidesIt() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        // A container can be bound to a marked type parameter. A new value of the type parameter that is converted to object is then no
        // value that moves into a box: it is the container, handed out as a type that hides its elements.
        sealed class Holder<[NonCopyable] T> where T : IDisposable
        {
            private T _value = default!;

            static T Create() => default!;

            public object Take() => {|#0:NonCopyable.Move(ref _value)|};

            public IDisposable TakeDisposable() => {|#1:Create()|};

            void M()
            {
                if (Create() is {|#2:IDisposable disposable|}) { }
                dynamic late = {|#3:Create()|};

                // A stored value is a boxed copy, as before, and a member on the safe list may receive the value.
                object boxed = {|#4:_value|};
                bool same = ReferenceEquals(Create(), null);

                // As itself the value goes anywhere.
                T typed = Create();
                T[] array = { Create() };
            }
        }

        abstract class Base
        {
            public abstract object Get();
        }

        // A type parameter that is constrained to a class converts with no conversion written.
        sealed class OfReferences<[NonCopyable] T> : Base where T : class
        {
            static T Make() => null!;

            public override T {|#5:Get|}() => Make();

            void M()
            {
                Func<object> untyped = {|#6:Make|};
                Func<T> typed = {|#8:Make|};
            }
        }

        class C
        {
            void M(Holder<Guards> holder)
            {
                // Without the finding in Take, this would take a boxed copy of a guard out of the list that the holder holds. The constraint
                // of the holder is one more way to use the list as a type that hides its guards, which is reported where the list is bound.
                IList list = (IList){|#7:holder.Take()|};
            }
        }

        class Guards : List<Guard>, IDisposable
        {
            public void Dispose() { }
        }

        {{Sources.Guard}}
        """,
        Verify.OpaqueTypeParameterConversion(0, "T", "object"),
        Verify.OpaqueTypeParameterConversion(1, "T", "System.IDisposable"),
        Verify.OpaqueTypeParameterConversion(2, "T", "System.IDisposable"),
        Verify.OpaqueTypeParameterConversion(3, "T", "dynamic"),
        Verify.Boxing(4, "T", "object"),
        Verify.OpaqueTypeParameterConversion(4, "T", "object"),
        Verify.OpaqueReturn(5, "T", "object", "OfReferences<T>.Get()", "Base.Get()"),
        Verify.OpaqueMethodGroup(6, "T", "object", "OfReferences<T>.Make()", "System.Func<object>"),
        Verify.OpaqueConstraint(7, "Holder<T>.Take()", "Guards", "T", "System.IDisposable"),
        Verify.DelegateOverTypeParameter(8, "OfReferences<T>.Make()", "T", "T"));

    [TestMethod]
    public Task MarkedTypeParameter_ConstraintMustNotHideAContainerThatIsBoundToIt() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        // The constraint lets the generic use what is bound to T as an IList, with no conversion anywhere.
        sealed class Untyped<[NonCopyable] T> where T : IList
        {
            public object First(ref T list) => list[0];
        }

        sealed class Opaque<[NonCopyable] T> where T : ICloneable { }

        // These constraints keep the non-copyable type visible, or hide it inside an erasable type.
        sealed class Typed<[NonCopyable] T> where T : IEnumerable<Guard> { }

        sealed class OfReferences<[NonCopyable] T> where T : class { }

        sealed class OfTasks<[NonCopyable] T> where T : Task { }

        sealed class Disposable<[NonCopyable] T> where T : IDisposable { }

        // A constraint that names another type parameter is judged with the type arguments filled in.
        interface IProducer<out TResult> { TResult Take(); }

        sealed class Source : IProducer<List<Guard>>
        {
            public List<Guard> Take() => new();
        }

        sealed class Uses<[NonCopyable] T, [NonCopyable] TResult> where T : IProducer<TResult> { }

        sealed class Comparable<[NonCopyable] T> where T : IEquatable<T> { }

        sealed class OfArrays<[NonCopyable] T, [NonCopyable] TElement> where T : IEnumerable<TElement[]> { }

        class C
        {
            static void Use<[NonCopyable] T>(ref T value) where T : ICollection { }

            void M(List<Guard> list, Guard[] guards, Guard guard)
            {
                var a = {|#0:new Untyped<List<Guard>>()|};
                var b = {|#1:new Opaque<Guard[]>()|};
                {|#2:Use(ref {|#6:list|})|};

                var c = new Typed<List<Guard>>();
                var d = new OfReferences<List<Guard>>();
                var e = new OfTasks<Task<Guard>>();

                var h = {|#3:new Uses<Source, object>()|};
                var i = new Uses<Source, List<Guard>>();
                var j = new Comparable<(Guard, int)>();

                // It is reported when it cannot be judged.
                var k = {|#4:new OfArrays<List<Guard[]>, Guard>()|};

                // A generic struct that is non-copyable only through its type arguments runs unchecked code behind the constraint.
                var l = {|#5:new Disposable<List<Guard>.Enumerator>()|};

                // A value of a non-copyable type is checked as one in the generic, whatever the constraint.
                var f = new Disposable<Guard>();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UntypedCollectionConstraint(0, "Untyped<T>.Untyped()", "System.Collections.Generic.List<Guard>", "T", "System.Collections.IList"),
        Verify.OpaqueConstraint(1, "Opaque<T>.Opaque()", "Guard[]", "T", "System.ICloneable"),
        Verify.UntypedCollectionConstraint(2, "C.Use<T>(ref T)", "System.Collections.Generic.List<Guard>", "T", "System.Collections.ICollection"),
        Verify.KeepsUnmarkedParameter(6, "C.M(List<Guard>, Guard[], Guard)", "list", "System.Collections.Generic.List<Guard>"),
        Verify.OpaqueConstraint(3, "Uses<T, TResult>.Uses()", "Source", "T", "IProducer<object>"),
        Verify.OpaqueConstraint(
            4, "OfArrays<T, TElement>.OfArrays()", "System.Collections.Generic.List<Guard[]>", "T", "System.Collections.Generic.IEnumerable<TElement[]>"),
        Verify.OpaqueConstraint(5, "Disposable<T>.Disposable()", "System.Collections.Generic.List<Guard>.Enumerator", "T", "System.IDisposable"));

    [TestMethod]
    public Task RequiresCopyable_ConstructorIsNotReachedThroughTheNewConstraint() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        class Copies<[NonCopyable] T>
        {
            [RequiresCopyable]
            public Copies() { }
        }

        class Plain<[NonCopyable] T>
        {
            public Plain() { }
        }

        class C
        {
            static T Make<[NonCopyable] T>() where T : new() => new T();

            void M()
            {
                // The 'new()' constraint lets the generic call the constructor, with no use of it for anyone to see.
                var a = {|#0:Make<Copies<Guard>>()|};
                var b = {|#1:new Copies<Guard>()|};

                var c = Make<Copies<int>>();
                var d = Make<Plain<Guard>>();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.RequiresCopyableMember(0, "Copies<T>.Copies()", "Guard", "T"),
        Verify.RequiresCopyableMember(1, "Copies<T>.Copies()", "Guard", "T"));

    [TestMethod]
    public Task GenericStruct_ThatLeavesADefaultMemberIsReportedWhereItIsBound() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        interface IReleasable<[NonCopyable] TSelf>
        {
            void Release() { }
        }

        // A generic struct that is non-copyable only through its type arguments cannot be judged where it is declared.
        struct Leaves<T> : IReleasable<Leaves<T>>
        {
            public T Value;
        }

        struct Implements<T> : IReleasable<Implements<T>>
        {
            public T Value;

            public void Release() { }
        }

        class C
        {
            static void Release<[NonCopyable] T>(ref T value) where T : IReleasable<T> => value.Release();

            void M(ref Leaves<Guard> leaves, ref Implements<Guard> implements, ref Leaves<int> copyable)
            {
                // The call through the constraint would run the default implementation on a boxed copy.
                {|#0:Release(ref leaves)|};

                Release(ref implements);
                Release(ref copyable);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.DefaultInterfaceMemberBound(0, "Leaves<Guard>", "IReleasable<TSelf>.Release()", "T", "C.Release<T>(ref T)", "IReleasable<T>"));

    [TestMethod]
    public Task Message_NamesATypeParameterWithoutANullableAnnotation() => Verify.AnalyzerAsync(
        """
        #nullable enable
        using Singulink.CodeAnalysis;

        sealed class Holder<[NonCopyable] T>
        {
            private T _value = default!;

            public T Get() => {|#0:_value|};

            public void Copy()
            {
                T copy = {|#1:_value|};
            }
        }
        """,
        Verify.CopyByReturn(0, "T"),
        Verify.CopyByAssignment(1, "T"));

    // A class with no constructor, and a constructor with no initializer, call the parameterless constructor of the base class.
    [TestMethod]
    public Task RequiresCopyable_BaseConstructorCalledWithNothingWrittenIsAUse() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        class Cell<[NonCopyable] T>
            where T : struct
        {
            public static T Seed;
            public T Held;

            [RequiresCopyable]
            public Cell() { Held = Seed; }

            public Cell(int size) { }
        }

        // Reported at the name of a class that has no constructor, for the type parameter that it passes on.
        sealed class {|#9:QuietCell|}<[NonCopyable] T> : Cell<T>
            where T : struct
        {
        }

        sealed class {|#0:GuardCell|} : Cell<Guard>
        {
        }

        sealed class WrittenCell<[NonCopyable] T> : Cell<T>
            where T : struct
        {
            public {|#1:WrittenCell|}() { }

            public {|#2:WrittenCell|}(string name) { }

            // A call that is written is reported where it stands, or names a constructor that needs no copy.
            public WrittenCell(bool flag) {|#3:: base()|} { }

            public WrittenCell(int size) : base(size) { }

            public WrittenCell(long size) : this((int)size) { }
        }

        sealed class {|#4:PrimaryCell|}<[NonCopyable] T>(int size) : Cell<T>
            where T : struct
        {
            public int Size => size;
        }

        sealed class PrimaryWrittenCell<[NonCopyable] T>(int size) : Cell<T>(size)
            where T : struct
        {
        }

        // A class that is marked itself passes the requirement on to whoever creates it, and is fine with a copyable type.
        sealed class CopyingCell<[NonCopyable] T> : Cell<T>
            where T : struct
        {
            [RequiresCopyable]
            public CopyingCell() { }
        }

        sealed class IntCell : Cell<int>
        {
        }

        // Each class is reported for the constructor that it calls itself.
        class MiddleCell<[NonCopyable] T> : InnerCell<T>
            where T : struct
        {
        }

        class {|#10:InnerCell|}<[NonCopyable] T> : Cell<T>
            where T : struct
        {
        }

        static class Factory
        {
            static T Make<[NonCopyable] T>()
                where T : new() => new T();

            static void Create()
            {
                // The class was reported where it is declared, so creating an object of it is not.
                var quiet = new QuietCell<Guard>();
                var copying = {|#5:new CopyingCell<Guard>()|};

                // A 'new()' constraint lets the generic create the object, with the constructors that this runs.
                var made = {|#6:Make<QuietCell<Guard>>()|};
                var middle = {|#7:Make<MiddleCell<Guard>>()|};
                var marked = {|#8:Make<CopyingCell<Guard>>()|};
                var ofInt = Make<QuietCell<int>>();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.RequiresCopyableMember(9, "Cell<T>.Cell()", "T", "T"),
        Verify.RequiresCopyableMember(0, "Cell<T>.Cell()", "Guard", "T"),
        Verify.RequiresCopyableMember(1, "Cell<T>.Cell()", "T", "T"),
        Verify.RequiresCopyableMember(2, "Cell<T>.Cell()", "T", "T"),
        Verify.RequiresCopyableMember(3, "Cell<T>.Cell()", "T", "T"),
        Verify.RequiresCopyableMember(4, "Cell<T>.Cell()", "T", "T"),
        Verify.RequiresCopyableMember(10, "Cell<T>.Cell()", "T", "T"),
        Verify.RequiresCopyableMember(5, "CopyingCell<T>.CopyingCell()", "Guard", "T"),
        Verify.RequiresCopyableMember(6, "Cell<T>.Cell()", "Guard", "T"),
        Verify.RequiresCopyableMember(7, "Cell<T>.Cell()", "Guard", "T"),
        Verify.RequiresCopyableMember(8, "CopyingCell<T>.CopyingCell()", "Guard", "T"));

    // Nothing calls a static constructor or a finalizer, so there is no use at which the copy could be reported.
    [TestMethod]
    public Task RequiresCopyable_OnAStaticConstructorOrAFinalizerIsReportedAndTheBodyIsChecked() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        static class Source<[NonCopyable] T>
            where T : struct
        {
            public static Func<T>? Make;
        }

        static class Pair<[NonCopyable] T>
            where T : struct
        {
            public static T First;
            public static T Second;

            [RequiresCopyable]
            static {|#0:Pair|}()
            {
                First = Source<T>.Make!();
                Second = {|#1:First|};
            }
        }

        sealed class Holder<[NonCopyable] T>
            where T : struct
        {
            public static T Last;

            private T _held;

            [RequiresCopyable]
            ~{|#2:Holder|}() => Last = {|#3:_held|};

            // On a member that code calls, the mark is what it is.
            [RequiresCopyable]
            public T Copy() => _held;
        }

        {{Sources.Guard}}
        """,
        Verify.RequiresCopyableOnMemberNeverCalled(0, "Pair<T>.Pair()"),
        Verify.CopyByAssignment(1, "T"),
        Verify.RequiresCopyableOnMemberNeverCalled(2, "Holder<T>.~Holder()"),
        Verify.CopyByAssignment(3, "T"));

    // A marked type parameter that is constrained to a container is a container: a value of it is a reference that the caller may
    // still hold, so what is read out of it is stored, whatever the last use of the parameter is. Using its elements in place is free.
    [TestMethod]
    public Task MarkedTypeParameter_ConstrainedToAContainer_IsAStoredContainer() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Runtime.InteropServices;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        static class C
        {
            static void ReleaseFirst<[NonCopyable] TList>(TList guards) where TList : IReadOnlyList<Guard>
            {
                Guard first = {|#0:guards[0]|};
                first.Dispose();
            }

            static void ReleaseFirstOfAList<[NonCopyable] TList>(TList guards) where TList : List<Guard>
            {
                Guard first = {|#1:guards[0]|};
                first.Dispose();
            }

            static TResult GetResult<[NonCopyable] TTask, [NonCopyable] TResult>(TTask task) where TTask : Task<TResult> =>
                {|#2:task.GetAwaiter().GetResult()|};

            static Guard GetGuard<[NonCopyable] TTask>(TTask task) where TTask : Task<Guard> => {|#3:task.Result|};

            static async Task<Guard> Await<[NonCopyable] TTask>(TTask task) where TTask : Task<Guard> => {|#4:await task|};

            static void Each<[NonCopyable] TList>(TList guards) where TList : List<Guard>
            {
                foreach (Guard guard in {|#5:guards|})
                    guard.Dispose();
            }

            // A constraint to another type parameter takes that one's constraints.
            static void Through<[NonCopyable] TList, [NonCopyable] TOther>(TOther guards) where TList : List<Guard> where TOther : TList
            {
                Guard first = {|#6:guards[0]|};
                first.Dispose();
            }

            // The elements are used in place.
            static void InPlace<[NonCopyable] TList>(TList guards) where TList : List<Guard>
            {
                CollectionsMarshal.AsSpan(guards)[0].Dispose();
            }

            static void EachInPlace<[NonCopyable] TList>(TList guards) where TList : List<Guard>
            {
                foreach (ref Guard guard in CollectionsMarshal.AsSpan(guards))
                    guard.Dispose();
            }

            static int Count<[NonCopyable] TList>(TList guards) where TList : IReadOnlyList<Guard> => guards.Count;

            // A marked type parameter with no container in its constraints holds its value alone, as before.
            static void Alone<[NonCopyable] T>(T value, List<T> into) where T : struct, System.IDisposable
            {
                into.Add(value);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'IReadOnlyList<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(2, "'TaskAwaiter<TResult>.GetResult()'", "TResult"),
        Verify.ReadFromStoredContainer(3, "'Task<TResult>.Result'", "Guard"),
        Verify.ReadFromStoredContainer(4, "'await'", "Guard"),
        Verify.ReadFromStoredContainer(5, "'foreach'", "Guard"),
        Verify.ReadFromStoredContainer(6, "'List<T>.this[int]'", "Guard"));

    // 'with' on a record class runs its copy constructor, and that of every base record. A [RequiresCopyable] one is a use like a
    // call that is written, and is reported at the 'with'.
    [TestMethod]
    public Task RequiresCopyable_WithOnARecordUsesItsCopyConstructors() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        public record class Batch<[NonCopyable] T>
            where T : struct
        {
            public T[] Items = new T[1];

            public Batch() { }

            [RequiresCopyable]
            protected Batch(Batch<T> other)
            {
                Items = new T[other.Items.Length];
                other.Items.CopyTo(Items, 0);
            }
        }

        public sealed record class NamedBatch<[NonCopyable] T>(string Name) : Batch<T>
            where T : struct;

        public record class Plain<[NonCopyable] T>(int Count)
            where T : struct;

        static class C
        {
            static void M(Batch<Guard> batch, NamedBatch<Guard> named, Batch<int> numbers, Plain<Guard> plain)
            {
                var a = {|#0:batch with { }|};
                var b = {|#1:named with { Name = "b" }|};

                // A copyable type argument, and a record whose copy constructor is the compiler's.
                var c = numbers with { };
                var d = plain with { Count = 2 };
            }

            static Batch<T> Inside<[NonCopyable] T>(Batch<T> batch)
                where T : struct => {|#2:batch with { }|};
        }

        {{Sources.Guard}}
        """,
        Verify.RequiresCopyableMember(0, "Batch<T>.Batch(Batch<T>)", "Guard", "T"),
        Verify.RequiresCopyableMember(1, "Batch<T>.Batch(Batch<T>)", "Guard", "T"),
        Verify.RequiresCopyableMember(2, "Batch<T>.Batch(Batch<T>)", "T", "T"));

    // Which base constructor a constructor calls with nothing written is the compiler's choice. It is taken from the compiler where
    // the constructor has a body. Where it has to be worked out, for the constructor that the compiler declares and for a primary
    // constructor, it is worked out as the language does: no parameters, then all optional, then 'params'.
    [TestMethod]
    public Task RequiresCopyable_BaseConstructorCalledWithNothingWritten_IsTheCompilersChoice() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        // The compiler takes the constructor with the optional parameter, which applies as it is written.
        class Cell<[NonCopyable] T>
            where T : struct
        {
            public static T Seed;
            public T Held;

            [RequiresCopyable]
            public Cell(int capacity = 0) { Held = Seed; }

            public Cell(params long[] values) { }
        }

        sealed class {|#0:QuietCell|}<[NonCopyable] T> : Cell<T>
            where T : struct
        {
        }

        sealed class WrittenCell<[NonCopyable] T> : Cell<T>
            where T : struct
        {
            public {|#1:WrittenCell|}() { }

            public {|#2:WrittenCell|}(string name) => Held = default;

            public WrittenCell(long value) : base(value) { }
        }

        sealed class {|#3:PrimaryCell|}<[NonCopyable] T>(int size) : Cell<T>
            where T : struct
        {
            public int Size => size;
        }

        // Here the marked constructor is the one that the compiler does not take.
        class Sparse<[NonCopyable] T>
            where T : struct
        {
            public Sparse(int capacity = 0) { }

            [RequiresCopyable]
            public Sparse(params long[] values) { }
        }

        sealed class WrittenSparse<[NonCopyable] T> : Sparse<T>
            where T : struct
        {
            public WrittenSparse() { }
        }

        // Worked out, the result is the same: the constructor that applies as it is written comes before the 'params' one.
        sealed class QuietSparse<[NonCopyable] T> : Sparse<T>
            where T : struct
        {
        }

        // The constructor with no parameters comes before both, marked or not.
        class Plain<[NonCopyable] T>
            where T : struct
        {
            public Plain() { }

            [RequiresCopyable]
            public Plain(int capacity = 0) { }
        }

        sealed class QuietPlain<[NonCopyable] T> : Plain<T>
            where T : struct
        {
        }

        class Copying<[NonCopyable] T>
            where T : struct
        {
            [RequiresCopyable]
            public Copying() { }

            public Copying(int capacity = 0) { }
        }

        sealed class {|#4:QuietCopying|}<[NonCopyable] T> : Copying<T>
            where T : struct
        {
        }

        // Only a 'params' constructor is left.
        class Listed<[NonCopyable] T>
            where T : struct
        {
            [RequiresCopyable]
            public Listed(params long[] values) { }

            public Listed(string name) { }
        }

        sealed class {|#7:QuietListed|}<[NonCopyable] T> : Listed<T>
            where T : struct
        {
        }

        // Where the language does not decide, the call does not compile, and every candidate counts.
        class Undecided<[NonCopyable] T>
            where T : struct
        {
            public Undecided(int capacity = 0) { }

            [RequiresCopyable]
            public Undecided(long size = 0) { }
        }

        sealed class {|#8:{|CS0121:QuietUndecided|}|}<[NonCopyable] T> : Undecided<T>
            where T : struct
        {
        }

        // A constructor that the class cannot call is none.
        class Locked<[NonCopyable] T>
            where T : struct
        {
            [RequiresCopyable]
            private Locked() { }

            public Locked(int capacity = 0) { }
        }

        sealed class QuietLocked<[NonCopyable] T> : Locked<T>
            where T : struct
        {
        }

        static class Factory
        {
            static T Make<[NonCopyable] T>()
                where T : new() => new T();

            static void Create()
            {
                var quiet = {|#5:Make<QuietCell<Guard>>()|};
                var written = {|#6:Make<WrittenCell<Guard>>()|};
                var locked = Make<QuietLocked<Guard>>();
                var ofInt = Make<QuietCell<int>>();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.RequiresCopyableMember(0, "Cell<T>.Cell(int)", "T", "T"),
        Verify.RequiresCopyableMember(1, "Cell<T>.Cell(int)", "T", "T"),
        Verify.RequiresCopyableMember(2, "Cell<T>.Cell(int)", "T", "T"),
        Verify.RequiresCopyableMember(3, "Cell<T>.Cell(int)", "T", "T"),
        Verify.RequiresCopyableMember(4, "Copying<T>.Copying()", "T", "T"),
        Verify.RequiresCopyableMember(7, "Listed<T>.Listed(params long[])", "T", "T"),
        Verify.RequiresCopyableMember(8, "Undecided<T>.Undecided(long)", "T", "T"),
        Verify.RequiresCopyableMember(5, "Cell<T>.Cell(int)", "Guard", "T"),
        Verify.RequiresCopyableMember(6, "Cell<T>.Cell(int)", "Guard", "T"));

    // 'with' copies a record through a virtual method, so on a variable of the base record it runs the copy constructor of the
    // derived one. A derived record whose copy constructor is [RequiresCopyable] where that of its base is not adds the mark like an
    // override does, and is reported where it is declared.
    [TestMethod]
    public Task RequiresCopyable_OnTheCopyConstructorOfADerivedRecord_MustBeOnThatOfItsBase() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        public record class Plain<[NonCopyable] T>
            where T : struct
        {
            public T[] Items = new T[1];
        }

        public record class Copying<[NonCopyable] T> : Plain<T>
            where T : struct
        {
            public Copying() { }

            [RequiresCopyable]
            protected {|#0:Copying|}(Copying<T> other) : base(other)
            {
                Items = new T[1];
                Items[0] = other.Items[0];
            }
        }

        // The mark is kept from there on.
        public record class Named<[NonCopyable] T> : Copying<T>
            where T : struct
        {
            public Named() { }

            [RequiresCopyable]
            protected Named(Named<T> other) : base(other) { }
        }

        // A record with no base record has nothing to add to.
        public record class Root<[NonCopyable] T>
            where T : struct
        {
            public Root() { }

            [RequiresCopyable]
            protected Root(Root<T> other) { }
        }

        static class C
        {
            // The 'with' that the mark on the derived record would not be reported at.
            static Plain<Guard> Copy(Plain<Guard> plain) => plain with { };
        }

        {{Sources.Guard}}
        """,
        Verify.RequiresCopyableNotOnBaseCopyConstructor(0, "Copying<T>.Copying(Copying<T>)", "Plain<T>", "Copying<T>"));
}
