using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// The test cases of the defensive-copy rules of hamarb123.Analyzers (HAM0001 and HAM0003), with every struct and every type parameter
/// marked [NonCopyable]. A defensive copy is the copy that the compiler makes of a value behind a read-only reference to run a member
/// that could change it. For a non-copyable type that is a copy like any other, so each place where that analyzer reports one with a
/// difference in behaviour (HAM0001) must be reported here as well.
/// </summary>
/// <remarks>
/// <para>Docs: articles/concepts/read-only-references.md.</para>
/// <para>
/// The sources are taken from https://github.com/hamarb123/hamarb123.Analyzers (folder hamarb123.Analyzers.Test/DefensiveCopies, MIT
/// license, see THIRD-PARTY-NOTICES.md). The markup <c>{|#n:...|}</c> is that analyzer's, at the places where it expects a diagnostic.
/// A place that it reports as unnecessary only (HAM0003, a member that is readonly in fact) is listed in <c>unnecessary</c>: the copy is
/// only the receiver of a member that cannot change it, which is not reported here. The exceptions are listed in <c>reportedThough</c>.</para>
/// <para>
/// The markup <c>{|ours:...|}</c> is at each place where only this analyzer reports a member that runs on a copy (SCA1006). The other
/// findings for these sources, which are copies that the other analyzer does not look for, are not checked. The two tests of primitive
/// types are left out, since an <c>int</c>, an <c>int?</c> or an enum cannot be non-copyable.</para>
/// </remarks>
[PrefixTestClass]
public class ThirdPartyDefensiveCopyTests
{
    [TestMethod]
    public Task CompilerGenerated_CompilerGenerated() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        public class C
        {
            // The ref we will be acting on
            public static ref readonly S1 X() => ref Unsafe.NullRef<S1>();

            // All defensive copies
            [CompilerGenerated] public C() => {|ours:X()|}.M1();
            [CompilerGenerated] public C(int a) { {|ours:X()|}.M1(); }
            [CompilerGenerated] public void M() => {|ours:X()|}.M1();
            [CompilerGenerated] public void M(int a) { {|ours:X()|}.M1(); }
            [CompilerGenerated] public int P1 => {|ours:X()|}.M2();
            public int P2
            {
                [CompilerGenerated] get => {|ours:X()|}.M2();
                [CompilerGenerated] set => {|ours:X()|}.M1();
            }
            [CompilerGenerated] public int P3
            {
                get { return {|ours:X()|}.M2(); }
                set { {|ours:X()|}.M1(); }
            }
            public event Action E1
            {
                [CompilerGenerated] add => {|ours:X()|}.M1();
                [CompilerGenerated] remove => {|ours:X()|}.M1();
            }
            [CompilerGenerated] public event Action E2
            {
                add { {|ours:X()|}.M1(); }
                remove { {|ours:X()|}.M1(); }
            }
            [CompilerGenerated] public int F1 = {|ours:X()|}.M2();
            [CompilerGenerated] public static int F2 = {|ours:X()|}.M2();
            [CompilerGenerated] public static int operator +(C a) => {|ours:X()|}.M2();
            [CompilerGenerated] public static int operator +(C a, C b) => {|ours:X()|}.M2();
            [CompilerGenerated] public static implicit operator int(C a) => {|ours:X()|}.M2();
            [CompilerGenerated] public static explicit operator long(C a) => {|ours:X()|}.M2();
            [CompilerGenerated] public int this[int a] => {|ours:X()|}.M2();
            public int this[int a, int b]
            {
                [CompilerGenerated] get => {|ours:X()|}.M2();
                [CompilerGenerated] set => {|ours:X()|}.M1();
            }
            [CompilerGenerated] public int this[int a, int b, int c]
            {
                get { return {|ours:X()|}.M2(); }
                set { {|ours:X()|}.M1(); }
            }
            [CompilerGenerated] static C() { {|ours:X()|}.M1(); }
            [CompilerGenerated] ~C() { {|ours:X()|}.M1(); }
            [CompilerGenerated] class D
            {
                static D() => {|ours:X()|}.M1();
                ~D() => {|ours:X()|}.M1();
            }
            delegate void DelType1(in S1 val);
            delegate void DelType2(S2 val);
            [CompilerGenerated] public void LocalMethodTest()
            {
                DelType1 a1 = (in S1 x) => {|ours:x|}.M1();
                DelType1 b1 = (in S1 x) =>
                {
                    {|ours:x|}.M1();
                };
                DelType1 d1 = delegate (in S1 x)
                {
                    {|ours:x|}.M1();
                };
                DelType2 a2 = x => {|ours:x.X|}.M1();
                DelType2 b2 = x =>
                {
                    {|ours:x.X|}.M1();
                };
                DelType2 c2 = delegate (S2 x)
                {
                    {|ours:x.X|}.M1();
                };
                void M1(in S1 x) { {|ours:x|}.M1(); }
                void M2(in S1 x) => {|ours:x|}.M1();
            }
            public void LocalMethodTest2()
            {
                DelType1 a1 = [CompilerGenerated] (in S1 x) => {|ours:x|}.M1();
                DelType1 b1 = [CompilerGenerated] (in S1 x) =>
                {
                    {|ours:x|}.M1();
                };
                DelType2 a2 = [CompilerGenerated] (x) => {|ours:x.X|}.M1();
                DelType2 b2 = [CompilerGenerated] (x) =>
                {
                    {|ours:x.X|}.M1();
                };
                [CompilerGenerated] void M1(in S1 x) { {|ours:x|}.M1(); }
                [CompilerGenerated] void M2(in S1 x) => {|ours:x|}.M1();
            }
            [CompilerGenerated] public int P4 { get; set; } = {|ours:X()|}.M2();
            [CompilerGenerated] public int F3 = {|ours:X()|}.M2(), F4 = {|ours:X()|}.M2(), F5;
        }

        [CompilerGenerated]
        public class D
        {
            private static ref readonly S1 s1 => throw null;
            public int a = {|ours:s1|}.M2();
            public void X(in S1 x) => {|ours:x|}.M1();
            public class D2
            {
                public void X(in S1 x) => {|ours:x|}.M1();
            }
        }

        public class E
        {
            [CompilerGenerated]
            public class E2
            {
                public void X(in S1 x) => {|ours:x|}.M1();
            }
        }

        [NonCopyable] [CompilerGenerated]
        struct F
        {
            public void X(in S1 x) => {|ours:x|}.M1();
        }

        [CompilerGenerated]
        interface G
        {
            public static void X(in S1 x) => {|ours:x|}.M1();
        }

        [CompilerGenerated]
        record H()
        {
            public void X(in S1 x) => {|ours:x|}.M1();
        }

        [CompilerGenerated]
        record struct I()
        {
            public void X(in S1 x) => {|ours:x|}.M1();
        }

        [CompilerGenerated]
        record class J()
        {
            public void X(in S1 x) => {|ours:x|}.M1();
        }

        [NonCopyable] public struct S1
        {
            public void M1() { }
            public int M2() => 0;
        }

        [NonCopyable] public ref struct S2
        {
            public ref readonly S1 X => throw null;
        }
        """);

    [TestMethod]
    public Task CompilerGenerated_Assembly() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        [assembly: CompilerGenerated]

        public class C
        {
            public void M1(in S1 value)
            {
                {|ours:value|}.M1();
            }
        }

        [NonCopyable] public struct S1
        {
            public void M1() { }
        }
        """);

    [TestMethod]
    public Task CompilerGenerated_Module() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        [module: CompilerGenerated]

        public class C
        {
            public void M1(in S1 value)
            {
                {|ours:value|}.M1();
            }
        }

        [NonCopyable] public struct S1
        {
            public void M1() { }
        }
        """);

    [TestMethod]
    public Task CompilerGenerated_Ignored() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        public class C
        {
            [CompilerGenerated] delegate void DelType(in S1 value);

            [return: CompilerGenerated]
            public void M1<[NonCopyable] [CompilerGenerated] T>([CompilerGenerated] in S1 value1, in T value2)
            {
                {|#0:value1.M1()|};
                {|#1:value2.ToString()|};
                DelType d = (in S1 value) => {|#2:value.M1()|};
            }
        }

        [NonCopyable] public struct S1
        {
            public void M1() { }
        }
        """);

    [TestMethod]
    public Task ImplicitCall_StringAddition() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        public class C
        {
            public void M1(in S1 value1, in S3 value2, in S4 value3, in S5<S1> value4, in S5<S3> value5, in S5<S4> value6, in S6 value7, in S7 value8, in S8 value9)
            {
                // No defensive copies necessary (all operations are readonly)
                string result;

                result = "" + {|#0:value1|};
                result = "" + {|#1:value2|};
                result = "" + value3;

                result = "" + 0 + {|#2:value2|};
                result += {|#3:value2|};

                result = "" + {|#4:value4.Value|};
                result = "" + {|#5:value5.Value|};
                result = "" + value6.Value;

                result = "" + 0 + {|#6:value5.Value|};
                result += {|#7:value5.Value|};

                // Readonly struct doesn't emit defensive copy

                result = "" + value7;
                result += value7;

                result = "" + value8;
                result += value8;

                result = "" + value9;
                result += value9;
            }

            public void M2(in S2 value1, in S5<S2> value2)
            {
                // All defensive copies
                string result;

                result = "" + {|#8:value1|};
                result = "" + 0 + {|#9:value1|};
                result += {|#10:value1|};

                result = "" + {|#11:value2.Value|};
                result = "" + 0 + {|#12:value2.Value|};
                result += {|#13:value2.Value|};

                result += {|#14:(value2).Value|};
                result += ({|#15:value2.Value|});
                result += ({|#16:(value2).Value|});
            }

            public void M3<[NonCopyable] T1, [NonCopyable] T2, [NonCopyable] T3>(in T1 value1, in T2 value2, in T3 value3) where T2 : class where T3 : struct
            {
                string result;

                // All defensive copies
                result = "" + {|#17:value1|};
                result = "" + 0 + {|#18:value1|};
                result += {|#19:value1|};

                // No defensive copies necessary
                result = "" + value2;
                result = "" + 0 + value2;
                result += value2;

                // All defensive copies
                result = "" + {|#20:value3|};
                result = "" + 0 + {|#21:value3|};
                result += {|#22:value3|};
            }

            public void M4(in S3 value1, in S6 value2)
            {
                // Reversed order should behave the same
                _ = {|#23:value1|} + "";
                _ = value2 + "";
            }
        }

        [NonCopyable] public struct S1
        {
        }

        [NonCopyable] public struct S2
        {
            public override string ToString() => "";
        }

        [NonCopyable] public struct S3
        {
            public readonly override string ToString() => "";
        }

        [NonCopyable] public struct S4
        {
            public override string ToString() => "";
            public static string operator+(string left, S4 right) => "";
        }

        [NonCopyable] public struct S5<[NonCopyable] T>
        {
            public T Value;
        }

        [NonCopyable] public readonly struct S6
        {
            public override string ToString() => "";
        }

        [NonCopyable] public readonly struct S7
        {
            public readonly override string ToString() => "";
        }

        [NonCopyable] public readonly struct S8
        {
        }
        """,
        unnecessary: [0, 1, 2, 3, 4, 5, 6, 7, 23]);

    [TestMethod]
    public Task ImplicitCall_UsingStatement() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        public class C
        {
            public void M1(
                in S1 val1, in S2 val2, in S3 val3, in S4 val4,
                in S5 val5, in S6 val6, in S7 val7, in S8 val8)
            {
                // No defensive copies

                using (var x1 = val1) { }
                using (var x2 = val2) { }
                using (var x3 = val3) { }
                using (var x4 = val4) { }
                using (var x5 = val5) { }
                using (var x6 = val6) { }
                using (var x7 = val7) { }
                using (var x8 = val8) { }

                using var y1 = val1;
                using var y2 = val2;
                using var y3 = val3;
                using var y4 = val4;
                using var y5 = val5;
                using var y6 = val6;
                using var y7 = val7;
                using var y8 = val8;

                using (var z1 = RO1()) { }
                using (var z2 = RO2()) { }
                using (var z3 = RO3()) { }
                using (var z4 = RO4()) { }
                using (var z5 = RO5()) { }
                using (var z6 = RO6()) { }
                using (var z7 = RO7()) { }
                using (var z8 = RO8()) { }

                using var w1 = RO1();
                using var w2 = RO2();
                using var w3 = RO3();
                using var w4 = RO4();
                using var w5 = RO5();
                using var w6 = RO6();
                using var w7 = RO7();
                using var w8 = RO8();
            }

            public unsafe ref readonly S1 RO1() => ref *(S1*)null;
            public unsafe ref readonly S2 RO2() => ref *(S2*)null;
            public unsafe ref readonly S3 RO3() => ref *(S3*)null;
            public unsafe ref readonly S4 RO4() => ref *(S4*)null;
            public unsafe ref readonly S5 RO5() => ref *(S5*)null;
            public unsafe ref readonly S6 RO6() => ref *(S6*)null;
            public unsafe ref readonly S7 RO7() => ref *(S7*)null;
            public unsafe ref readonly S8 RO8() => ref *(S8*)null;
        }

        [NonCopyable] public struct S1 : IDisposable
        {
            public void Dispose() { }
        }
        [NonCopyable] public struct S2 : IDisposable
        {
            public readonly void Dispose() { }
        }
        [NonCopyable] public readonly struct S3 : IDisposable
        {
            public void Dispose() { }
        }
        [NonCopyable] public struct S4 : IDisposable
        {
            void IDisposable.Dispose() { }
        }
        [NonCopyable] public struct S5 : IDisposable
        {
            readonly void IDisposable.Dispose() { }
        }
        [NonCopyable] public ref struct S6
        {
            public void Dispose() { }
        }
        [NonCopyable] public ref struct S7
        {
            public readonly void Dispose() { }
        }
        [NonCopyable] public readonly ref struct S8
        {
            public void Dispose() { }
        }
        """);

    [TestMethod]
    public Task ImplicitCall_Foreach() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Collections;
        using System.Collections.Generic;

        public class C
        {
            public void M1<[NonCopyable] T>
            (
                in S4 value4, in S5 value5, in S6 value6, in S8 value8, in S9<S4> _value4, in S9<S5> _value5, in S9<S6> _value6, in S9<S8> _value8, in S9<List<object>> value9, in S10 value10,
                in S11 value11, in S12 value12, in S14 value14, ref T value15
            )
                where T : IEnumerable<S1>
            {
                // No defensive copies necessary (compiler still emits for S4 and S5 though)
                foreach (var x in {|#0:value4|}) { }
                foreach (var x in {|#1:value5|}) { }
                foreach (var x in value6) { }
                foreach (var x in value8) { }
                foreach (var x in {|#2:_value4.Field|}) { }
                foreach (var x in {|#3:_value5.Field|}) { }
                foreach (var x in _value6.Field) { }
                foreach (var x in _value8.Field) { }
                foreach (var x in value9.Field) { }
                foreach (var x in value9.Prop) { }
                foreach (var x in value10.Field) { }
                foreach (var x in value11) { }
                foreach (var x in value12) { }
                foreach (var x in value14) { }
                foreach (var x in value15) { }
                foreach (var x in value15!) { }
            }
            public void M2<[NonCopyable] T>
            (
                in S1 value1, in S2 value2, in S3 value3, in S7 value7, in S9<S1> _value1, in S9<S2> _value2, in S9<S3> _value3, in S9<S7> _value7,
                in S13 value13, in T value15
            )
                where T : IEnumerable<S1>
            {
                // All defensive copies
                foreach (var x in {|#4:value1|}) { }
                foreach (var x in {|#5:value2|}) { }
                foreach (var x in {|#6:value3|}) { }
                foreach (var x in {|#7:value7|}) { }
                foreach (var x in {|#8:_value1.Field|}) { }
                foreach (var x in {|#9:_value2.Field|}) { }
                foreach (var x in {|#10:_value3.Field|}) { }
                foreach (var x in {|#11:_value7.Field|}) { }
                foreach (var x in {|#12:value13|}) { }
                foreach (var x in {|#13:value15|}) { }
                foreach (var x in {|#14:value15|}!) { }
            }
        }

        [NonCopyable] public struct EnumImpl : IEnumerator<int>
        {
            public int Current => 0;
            object IEnumerator.Current => 0;
            public bool MoveNext() => false;
            public void Reset() { }
            public void Dispose() { }
            public static readonly EnumImpl Instance;
        }
        [NonCopyable] public struct S1 : IEnumerable
        {
            IEnumerator IEnumerable.GetEnumerator() => new EnumImpl();
        }
        [NonCopyable] public struct S2 : IEnumerable<int>
        {
            IEnumerator<int> IEnumerable<int>.GetEnumerator() => new EnumImpl();
            IEnumerator IEnumerable.GetEnumerator() => new EnumImpl();
        }
        [NonCopyable] public struct S3 : IEnumerable<int>
        {
            IEnumerator<int> IEnumerable<int>.GetEnumerator() => new EnumImpl();
            IEnumerator IEnumerable.GetEnumerator() => new EnumImpl();
            public EnumImpl GetEnumerator() => new EnumImpl();
        }
        [NonCopyable] public struct S4 : IEnumerable
        {
            readonly IEnumerator IEnumerable.GetEnumerator() => new EnumImpl();
        }
        [NonCopyable] public struct S5 : IEnumerable<int>
        {
            readonly IEnumerator<int> IEnumerable<int>.GetEnumerator() => new EnumImpl();
            readonly IEnumerator IEnumerable.GetEnumerator() => new EnumImpl();
        }
        [NonCopyable] public struct S6 : IEnumerable<int>
        {
            readonly IEnumerator<int> IEnumerable<int>.GetEnumerator() => new EnumImpl();
            readonly IEnumerator IEnumerable.GetEnumerator() => new EnumImpl();
            readonly public EnumImpl GetEnumerator() => new EnumImpl();
        }
        [NonCopyable] public struct S7 : IEnumerable<int>
        {
            readonly IEnumerator<int> IEnumerable<int>.GetEnumerator() => new EnumImpl();
            readonly IEnumerator IEnumerable.GetEnumerator() => new EnumImpl();
            public ref readonly EnumImpl GetEnumerator() => ref EnumImpl.Instance;
        }
        [NonCopyable] public struct S8 : IEnumerable<int>
        {
            readonly IEnumerator<int> IEnumerable<int>.GetEnumerator() => new EnumImpl();
            readonly IEnumerator IEnumerable.GetEnumerator() => new EnumImpl();
            public readonly ref readonly EnumImpl GetEnumerator() => ref EnumImpl.Instance;
        }
        [NonCopyable] public struct S9<[NonCopyable] T>
        {
            public T Field;
            public T Prop { get; }
        }
        [NonCopyable] public struct S10
        {
            public List<object> Field;
        }
        [NonCopyable] public struct S11
        {
        }
        [NonCopyable] public struct S12
        {
        }
        [NonCopyable] public ref struct S13
        {
            public IEnumerator<int> GetEnumerator() => throw null;
        }
        [NonCopyable] public ref struct S14
        {
            public readonly IEnumerator<int> GetEnumerator() => throw null;
        }
        public static class Extensions
        {
            public static IEnumerator<int> GetEnumerator(this in S11 value) => throw null;
            public static IEnumerator<int> GetEnumerator(this S12 value) => throw null;
        }
        """,
        unnecessary: [0, 1, 2, 3]);

    [TestMethod]
    public Task ImplicitCall_Deconstruction() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        public class C
        {
            public void M1(in S1 value1, in S2 value2)
            {
                // No defensive copies
                var (a1, b1) = {|ours:value1|};
                var (a2, b2) = value2;
            }
        }

        [NonCopyable] public struct S1
        {
            public void Deconstruct(out int a, out int b)
            {
                a = 0;
                b = 0;
            }
        }

        [NonCopyable] public struct S2
        {
            public readonly void Deconstruct(out int a, out int b)
            {
                a = 0;
                b = 0;
            }
        }
        """);

    [TestMethod]
    public Task ImplicitCall_Fixed() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        public class C
        {
            public unsafe void M1<[NonCopyable] T1, [NonCopyable] T2, [NonCopyable] T3, [NonCopyable] T4, [NonCopyable] T1B, [NonCopyable] T2B, [NonCopyable] T3B, [NonCopyable] RT1, [NonCopyable] RT2, [NonCopyable] RT1B, [NonCopyable] RT2B>
            (
                in S2 value2RO, ref S1 value1, ref S2 value2, ref S3 value3, in S4 value4RO, ref S4 value4, in S5 value5RO, ref S5 value5,
                ref T1 valueT1, ref T2 valueT2, in T3 valueT3RO, ref T3 valueT3, in T4 valueT4RO, ref T4 valueT4, C1 valueC1, ref C1 refValueC1, ref readonly C1 refROValueC1,
                ref T1B valueT1B, ref T2B valueT2B, in T3B valueT3BRO, ref T3B valueT3B,
                in S6 value6RO, in S7 value7RO,
                in RS2 rsValue2RO, ref RS1 rsValue1, ref RS2 rsValue2, ref RS3 rsValue3, in RS4 rsValue4RO, ref RS4 rsValue4, in RS5 rsValue5RO, ref RS5 rsValue5,
                ref RT1 rsValueRT1, ref RT2 rsValueRT2,
                ref RT1B rsValueRT1B, ref RT2B rsValueRT2B,
                in RS6 rsValue6RO, in RS7 rsValue7RO
            )
                where T1 : I1 where T2 : struct, I1 where T3 : class, I1 where T4 : C1 where T1B : I2 where T2B : struct, I2 where T3B : class, I2
                where RT1 : I1, allows ref struct where RT2 : struct, I1, allows ref struct where RT1B : I2, allows ref struct where RT2B : struct, I2, allows ref struct
            {
                // No defensive copies
                fixed (int* ptr = value2RO) { }
                fixed (int* ptr = value1) { }
                fixed (int* ptr = value2) { }
                fixed (int* ptr = value3) { }
                fixed (int* ptr = value4RO) { }
                fixed (int* ptr = value4) { }
                fixed (int* ptr = value5RO) { }
                fixed (int* ptr = value5) { }
                fixed (int* ptr = valueT1) { }
                fixed (int* ptr = valueT2) { }
                fixed (int* ptr = valueT3RO) { }
                fixed (int* ptr = valueT3) { }
                fixed (int* ptr = valueT4RO) { }
                fixed (int* ptr = valueT4) { }
                fixed (int* ptr = valueC1) { }
                fixed (int* ptr = refValueC1) { }
                fixed (int* ptr = refROValueC1) { }
                fixed (int* ptr = valueT1B) { }
                fixed (int* ptr = valueT2B) { }
                fixed (int* ptr = valueT3BRO) { }
                fixed (int* ptr = valueT3B) { }
                fixed (int* ptr = {|ours:value6RO|}) { }
                fixed (int* ptr = value7RO) { }

                fixed (int* ptr = rsValue2RO) { }
                fixed (int* ptr = rsValue1) { }
                fixed (int* ptr = rsValue2) { }
                fixed (int* ptr = rsValue3) { }
                fixed (int* ptr = rsValue4RO) { }
                fixed (int* ptr = rsValue4) { }
                fixed (int* ptr = rsValue5RO) { }
                fixed (int* ptr = rsValue5) { }
                fixed (int* ptr = rsValueRT1) { }
                fixed (int* ptr = rsValueRT2) { }
                fixed (int* ptr = rsValueRT1B) { }
                fixed (int* ptr = rsValueRT2B) { }
                fixed (int* ptr = {|ours:rsValue6RO|}) { }
                fixed (int* ptr = rsValue7RO) { }

                fixed (void* ptr = value1) { }
                fixed (int* ptr1 = value1, ptr2 = value2) { }

                // No defensive copies (pointer)
                fixed (void* ptr = &value2RO) { }
                fixed (void* ptr = &value1) { }
                fixed (void* ptr = &value2) { }
                fixed (void* ptr = &value3) { }
                fixed (void* ptr = &value4RO) { }
                fixed (void* ptr = &value4) { }
                fixed (void* ptr = &value5RO) { }
                fixed (void* ptr = &value5) { }
                fixed (void* ptr = &valueT1) { }
                fixed (void* ptr = &valueT2) { }
                fixed (void* ptr = &valueT3RO) { }
                fixed (void* ptr = &valueT3) { }
                fixed (void* ptr = &valueT4RO) { }
                fixed (void* ptr = &valueT4) { }
                // fixed (void* ptr = &valueC1) { }
                fixed (void* ptr = &refValueC1) { }
                fixed (void* ptr = &refROValueC1) { }
                fixed (void* ptr = &valueT1B) { }
                fixed (void* ptr = &valueT2B) { }
                fixed (void* ptr = &valueT3BRO) { }
                fixed (void* ptr = &valueT3B) { }
                fixed (void* ptr = &value6RO) { }
                fixed (void* ptr = &value7RO) { }

                fixed (void* ptr = &rsValue2RO) { }
                fixed (void* ptr = &rsValue1) { }
                fixed (void* ptr = &rsValue2) { }
                fixed (void* ptr = &rsValue3) { }
                fixed (void* ptr = &rsValue4RO) { }
                fixed (void* ptr = &rsValue4) { }
                fixed (void* ptr = &rsValue5RO) { }
                fixed (void* ptr = &rsValue5) { }
                fixed (void* ptr = &rsValueRT1) { }
                fixed (void* ptr = &rsValueRT2) { }
                fixed (void* ptr = &rsValueRT1B) { }
                fixed (void* ptr = &rsValueRT2B) { }
                fixed (void* ptr = &rsValue6RO) { }
                fixed (void* ptr = &rsValue7RO) { }
                fixed (void* ptr = &(rsValue7RO)) { }

                fixed (void* ptr1 = &value1, ptr2 = &value2) { }
            }

            public unsafe void M2<[NonCopyable] T1, [NonCopyable] T2, [NonCopyable] T1B, [NonCopyable] T2B, [NonCopyable] RT1, [NonCopyable] RT2, [NonCopyable] RT1B, [NonCopyable] RT2B>
            (
                in S2 value2RO, ref S1 value1, in S1 value1RO, in T1 valueT1RO, in T2 valueT2RO, in T1B valueT1BRO, in T2B valueT2BRO,
                in RS2 rsValue2RO, ref RS1 rsValue1, in RS1 rsValue1RO, in RT1 rsValueRT1RO, in RT2 rsValueRT2RO, in RT1B rsValueRT1BRO, in RT2B rsValueRT2BRO
            )
                where T1 : I1 where T2 : struct, I1 where T1B : I2 where T2B : struct, I2
                where RT1 : I1, allows ref struct where RT2 : struct, I1, allows ref struct where RT1B : I2, allows ref struct where RT2B : struct, I2, allows ref struct
            {
                // All defensive copies
                fixed (int* ptr = {|#0:value1RO|}) { }
                fixed (int* ptr = {|#1:valueT1RO|}) { }
                fixed (int* ptr = {|#2:valueT2RO|}) { }
                fixed (int* ptr = {|#3:valueT1BRO|}) { }
                fixed (int* ptr = {|#4:valueT2BRO|}) { }
                fixed (int* ptr = ({|#5:valueT1RO|})) { }
                fixed (int* ptr = {|#6:valueT1RO|}!) { }
                fixed (int* ptr = ({|#7:valueT1RO|}!)) { }

                // Some defensive copies (first declaration always, second for first line also)
                fixed (int* ptr1 = {|#8:value1RO|}, ptr2 = {|#9:valueT1RO|}) { }
                fixed (void* ptr1 = {|#10:value1RO|}) { }
                fixed (void* ptr1 = {|#11:value1RO|}, ptr2 = &value1RO) { }
                fixed (void* ptr1 = {|#12:value1RO|}, ptr2 = value1) { }
                fixed (void* ptr1 = {|#13:value1RO|}, ptr2 = value2RO) { }

                // No defensive copies (pointer)
                fixed (void* ptr = &value1RO) { }
                fixed (void* ptr = &valueT1RO) { }
                fixed (void* ptr = &valueT2RO) { }
                fixed (void* ptr = &valueT1BRO) { }
                fixed (void* ptr = &valueT2BRO) { }

                // All defensive copies
                fixed (int* ptr = {|#14:rsValue1RO|}) { }
                fixed (int* ptr = {|#15:rsValueRT1RO|}) { }
                fixed (int* ptr = {|#16:rsValueRT2RO|}) { }
                fixed (int* ptr = {|#17:rsValueRT1BRO|}) { }
                fixed (int* ptr = {|#18:rsValueRT2BRO|}) { }

                // Some defensive copies (first declaration always, second for first line also)
                fixed (int* ptr1 = {|#19:rsValue1RO|}, ptr2 = {|#20:rsValueRT1RO|}) { }
                fixed (void* ptr1 = {|#21:rsValue1RO|}) { }
                fixed (void* ptr1 = {|#22:rsValue1RO|}, ptr2 = &rsValue1RO) { }
                fixed (void* ptr1 = {|#23:rsValue1RO|}, ptr2 = rsValue1) { }
                fixed (void* ptr1 = {|#24:rsValue1RO|}, ptr2 = rsValue2RO) { }

                // No defensive copies (pointer)
                fixed (void* ptr = &rsValue1RO) { }
                fixed (void* ptr = &rsValueRT1RO) { }
                fixed (void* ptr = &rsValueRT2RO) { }
                fixed (void* ptr = &rsValueRT1BRO) { }
                fixed (void* ptr = &rsValueRT2BRO) { }
            }
        }

        [NonCopyable] public struct S1
        {
            public ref int GetPinnableReference() => throw null;
        }

        [NonCopyable] public struct S2
        {
            public readonly ref int GetPinnableReference() => throw null;
        }

        [NonCopyable] public struct S3
        {
        }

        [NonCopyable] public struct S4
        {
        }

        [NonCopyable] public struct S5
        {
        }

        [NonCopyable] public struct S6
        {
            private ref int GetPinnableReference() => throw null;
            public static unsafe void M2(in S6 value6RO)
            {
                // All defensive copies
                fixed (int* ptr = {|#25:value6RO|}) { }

                // No defensive copies (pointer)
                fixed (void* ptr = &value6RO) { }
            }
        }

        [NonCopyable] public struct S7 : I1
        {
            ref int I1.GetPinnableReference() => throw null;
        }

        [NonCopyable] public ref struct RS1
        {
            public ref int GetPinnableReference() => throw null;
        }

        [NonCopyable] public ref struct RS2
        {
            public readonly ref int GetPinnableReference() => throw null;
        }

        [NonCopyable] public ref struct RS3
        {
        }

        [NonCopyable] public ref struct RS4
        {
        }

        [NonCopyable] public ref struct RS5
        {
        }

        [NonCopyable] public ref struct RS6
        {
            private ref int GetPinnableReference() => throw null;
            public static unsafe void M2(in RS6 rsValue6RO)
            {
                // All defensive copies
                fixed (int* ptr = {|#26:rsValue6RO|}) { }

                // No defensive copies (pointer)
                fixed (void* ptr = &rsValue6RO) { }
            }
        }

        [NonCopyable] public ref struct RS7 : I1
        {
            ref int I1.GetPinnableReference() => throw null;
        }

        public static class Extensions
        {
            public static ref int GetPinnableReference(this ref S3 value) => throw null;
            public static ref int GetPinnableReference(this in S4 value) => throw null;
            public static ref int GetPinnableReference(this S5 value) => throw null;
            public static ref int GetPinnableReference(this in S6 value) => throw null;
            public static ref int GetPinnableReference(this in S7 value) => throw null;
            public static ref int GetPinnableReference(this ref RS3 value) => throw null;
            public static ref int GetPinnableReference(this in RS4 value) => throw null;
            public static ref int GetPinnableReference(this RS5 value) => throw null;
            public static ref int GetPinnableReference(this in RS6 value) => throw null;
            public static ref int GetPinnableReference(this in RS7 value) => throw null;
        }

        public interface I1
        {
            ref int GetPinnableReference();
        }

        public interface I2 : I1
        {
        }

        public class C1
        {
            public virtual ref int GetPinnableReference() => throw null;
        }
        """);

    [TestMethod]
    public Task ImplicitCall_RangeIndex() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        public class C
        {
            public void M1(ref S1 a, ref S2 b, ref S3 c, ref S4 d, ref S5 e, ref S6 f, ref S7 g, ref S8 h, Index i, Range r)
            {
                // No defensive copy
                _ = a[4..8];
                _ = a[4..^8];
                _ = a[^1];
                _ = a[0];
                _ = a[i];
                _ = a[0..i];
                _ = a[r];
                _ = a[0..(Index)1];

                // No defensive copy
                _ = b[4..8];
                _ = b[4..^8];
                _ = b[^1];
                _ = b[0];
                _ = b[i];
                _ = b[0..i];
                _ = b[r];
                _ = b[0..(Index)1];

                // No defensive copy
                _ = c[4..8];
                _ = c[4..^8];
                _ = c[^1];
                _ = c[0];
                _ = c[i];
                _ = c[0..i];
                _ = c[r];
                _ = c[0..(Index)1];

                // No defensive copy
                _ = d[4..8];
                _ = d[4..^8];
                _ = d[^1];
                _ = d[0];
                _ = d[i];
                _ = d[0..i];
                _ = d[r];
                _ = d[0..(Index)1];

                // No defensive copy
                _ = e[4..8];
                _ = e[4..^8];
                _ = e[^1];
                _ = e[0];
                _ = e[i];
                _ = e[0..i];
                _ = e[r];
                _ = e[0..(Index)1];

                // No defensive copy
                _ = f[4..8];
                _ = f[4..^8];
                _ = f[^1];
                _ = f[0];
                _ = f[i];
                _ = f[0..i];
                _ = f[r];
                _ = f[0..(Index)1];

                // No defensive copy
                _ = g[4..8];
                _ = g[4..^8];
                _ = g[^1];
                _ = g[0];
                _ = g[i];
                _ = g[0..i];
                _ = g[r];
                _ = g[0..(Index)1];

                // No defensive copy
                _ = h[4..8];
                _ = h[4..^8];
                _ = h[^1];
                _ = h[0];
                _ = h[i];
                _ = h[0..i];
                _ = h[r];
                _ = h[0..(Index)1];
            }

            public void M2(in S1 a, in S2 b, in S3 c, in S4 d, in S5 e, in S6 f, in S7 g, in S8 h, Index i, Range r)
            {
                // No defensive copy
                _ = a[4..8];
                _ = a[4..^8];
                _ = a[^1];
                _ = a[0];
                _ = a[i];
                _ = a[0..i];
                _ = a[r];
                _ = a[0..(Index)1];

                // Defensive copy
                _ = {|#0:b[4..8]|};
                _ = {|#1:b[4..^8]|};
                _ = {|#2:b[^1]|};
                _ = {|#3:b[0]|};
                _ = {|#4:b[i]|};
                _ = {|#5:b[0..i]|};
                _ = {|#6:b[r]|};
                _ = {|#7:b[0..(Index)1]|};

                // Defensive copy
                _ = {|#8:c[4..8]|};
                _ = {|#9:c[4..^8]|};
                _ = {|#10:c[^1]|};
                _ = {|#11:c[0]|};
                _ = {|#12:c[i]|};
                _ = {|#13:c[0..i]|};
                _ = {|#14:c[r]|};
                _ = {|#15:c[0..(Index)1]|};

                // Defensive copy
                _ = d[4..8]; // No defensive copy
                _ = {|#16:d[4..^8]|};
                _ = {|#17:d[^1]|};
                _ = d[0]; // No defensive copy
                _ = {|#18:d[i]|};
                _ = {|#19:d[0..i]|};
                _ = {|#20:d[r]|};
                _ = {|#21:d[0..(Index)1]|};

                // Defensive copy
                _ = {|#22:e[4..8]|};
                _ = {|#23:e[4..^8]|};
                _ = e[^1]; // No defensive copy
                _ = e[0]; // No defensive copy
                _ = e[i]; // No defensive copy
                _ = {|#24:e[0..i]|};
                _ = {|#25:e[r]|};
                _ = {|#26:e[0..(Index)1]|};

                // Defensive copy
                _ = f[4..8]; // No defensive copy
                _ = f[4..^8]; // No defensive copy
                _ = {|#27:f[^1]|};
                _ = {|#28:f[0]|};
                _ = {|#29:f[i]|};
                _ = f[0..i]; // No defensive copy
                _ = f[r]; // No defensive copy
                _ = f[0..(Index)1]; // No defensive copy

                // Defensive copy
                _ = {|#30:g[4..8]|};
                _ = {|#31:g[4..^8]|};
                _ = {|#32:g[^1]|};
                _ = g[0]; // No defensive copy
                _ = {|#33:g[i]|};
                _ = {|#34:g[0..i]|};
                _ = {|#35:g[r]|};
                _ = {|#36:g[0..(Index)1]|};

                // Defensive copy
                _ = h[4..8]; // No defensive copy
                _ = {|#37:h[4..^8]|};
                _ = {|#38:h[^1]|};
                _ = {|#39:h[0]|};
                _ = {|#40:h[i]|};
                _ = {|#41:h[0..i]|};
                _ = {|#42:h[r]|};
                _ = {|#43:h[0..(Index)1]|};
            }
        }

        [NonCopyable] public struct S1
        {
            public readonly int Length => 0;
            public readonly int this[int index] => 0;
            public readonly S1 Slice(int start, int length) => this;
        }

        [NonCopyable] public struct S2
        {
            public int Length => 0;
            public int this[int index] => 0;
            public S2 Slice(int start, int length) => this;
        }

        [NonCopyable] public struct S3
        {
            public readonly int Length => 0;
            public int this[int index] => 0;
            public S3 Slice(int start, int length) => this;
        }

        [NonCopyable] public struct S4
        {
            public int Length => 0;
            public readonly int this[int index] => 0;
            public readonly S4 Slice(int start, int length) => this;
        }

        [NonCopyable] public struct S5
        {
            public readonly int Length => 0;
            public readonly int this[int index] => 0;
            public S5 Slice(int start, int length) => this;
        }

        [NonCopyable] public struct S6
        {
            public readonly int Length => 0;
            public int this[int index] => 0;
            public readonly S6 Slice(int start, int length) => this;
        }

        [NonCopyable] public struct S7
        {
            public int Length => 0;
            public readonly int this[int index] => 0;
            public S7 Slice(int start, int length) => this;
        }

        [NonCopyable] public struct S8
        {
            public int Length => 0;
            public int this[int index] => 0;
            public readonly S8 Slice(int start, int length) => this;
        }
        """);

    [TestMethod]
    public Task Obscure_GenericParameters() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        public class C
        {
            // All defensive copies
            public static void X1<[NonCopyable] T>(in T value) where T : IDisposable
            {
                {|#0:value.Dispose()|};
            }
            public static void X2<[NonCopyable] T>(in T value)
            {
                {|#1:value.ToString()|};
            }
            public static void X3<[NonCopyable] T>(in T value) where T : struct, IDisposable
            {
                {|#2:value.Dispose()|};
            }
            public static void X4<[NonCopyable] T>(in T value) where T : struct
            {
                {|#3:value.ToString()|};
            }
            public static void X5<[NonCopyable] T>(in T value) where T : unmanaged
            {
                {|#4:value.ToString()|};
            }
            public static void X6<[NonCopyable] T>(in T value) where T : IDisposable, allows ref struct
            {
                {|#5:value.Dispose()|};
            }
            public static void X7<[NonCopyable] T>(in T value) where T : struct, IDisposable, allows ref struct
            {
                {|#6:value.Dispose()|};
            }
            public static void X8<[NonCopyable] T>(in T value) where T : unmanaged, IDisposable, allows ref struct
            {
                {|#7:value.Dispose()|};
            }

            // No defensive copies
            public static void X9<[NonCopyable] T>(in T value) where T : class, IDisposable
            {
                value.Dispose();
            }
            public static void X10<[NonCopyable] T>(in T value) where T : Disposable
            {
                value.Dispose();
                value.X = 1;
            }
            public static void X11<[NonCopyable] T>(in T value) where T : class
            {
                value.ToString();
            }
        }

        public class Disposable
        {
            public virtual void Dispose() { }
            public int X;
        }
        """);

    [TestMethod]
    public Task Obscure_ExtensionMethods() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        public class C
        {
            public void M1(in S1 value1)
            {
                // No defensive copies
                string result;

                // result = value1.X1();
                result = value1.X2();
            }
        }

        [NonCopyable] public struct S1
        {
        }

        public static class Ext
        {
            // public static string X1<T>(this in T value) where T : struct => "";
            public static string X2(this in S1 value) => "";
        }
        """);

    [TestMethod]
    public Task Obscure_Ternary() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        public class C
        {
            public void M1(bool b, ref S1 value1, ref S1 value2, in S1 value3, in S1 value4)
            {
                // No defensive copies
                Console.WriteLine((b ? ref value1 : ref value2).M1());
                Console.WriteLine(({|ours:b ? value1 : value2|}).M1());
                Console.WriteLine(({|ours:b ? value3 : value2|}).M1());
                Console.WriteLine(({|ours:b ? value1 : value4|}).M1());
                Console.WriteLine(({|ours:b ? value3 : value4|}).M1());
            }

            public void M2(bool b, ref S1 value1, ref S1 value2, in S1 value3, in S1 value4)
            {
                // All defensive copies
                Console.WriteLine({|#0:(b ? ref value3 : ref value2).M1()|});
                Console.WriteLine({|#1:(b ? ref value1 : ref value4).M1()|});
                Console.WriteLine({|#2:(b ? ref value3 : ref value4).M1()|});
            }
        }

        [NonCopyable] public struct S1
        {
            public int M1() => 0;
        }
        """);

    [TestMethod]
    public Task Obscure_RefAssign() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        public class C
        {
            public void M1(in S1 value1, ref S1 value2, in S1 c, ref readonly S1 d, ref S1 e, out S1 f)
            {
                // No defensive copies
                ref readonly S1 a = ref value1;
                ref S1 b = ref value2;
                f = default;
                scoped RS1 g = default;

                (a = ref value1).M2();
                (a = ref value2).M2();
                (b = ref value2).M1();
                (b = ref value2).M2();
                (b = ref value2).M2();
                (b = ref value2).M2();
                (c = ref value1).M2();
                (c = ref value2).M2();
                (d = ref value1).M2();
                (d = ref value2).M2();
                (e = ref value2).M1();
                (e = ref value2).M2();
                (e = ref value2).M2();
                (e = ref value2).M2();
                (f = ref value2).M1();
                (f = ref value2).M2();
                (f = ref value2).M2();
                (f = ref value2).M2();
                (g.F1 = ref value2).M1();
                (g.F1 = ref value2).M2();
                (g.F1 = ref value2).M2();
                (g.F1 = ref value2).M2();
                (g.F2 = ref value1).M2();
                (g.F2 = ref value2).M2();
            }

            public void M2(in S1 value1, ref S1 value2, in S1 c, ref readonly S1 d)
            {
                // All defensive copies
                ref readonly S1 a = ref value1;
                scoped RS1 g = default;

                {|#0:(a = ref value1).M1()|};
                {|#1:(a = ref value2).M1()|};
                {|#2:(c = ref value1).M1()|};
                {|#3:(c = ref value2).M1()|};
                {|#4:(d = ref value1).M1()|};
                {|#5:(d = ref value2).M1()|};
                {|#6:(g.F2 = ref value1).M1()|};
                {|#7:(g.F2 = ref value2).M1()|};
            }
        }

        [NonCopyable] public struct S1
        {
            public void M1() { }
            public readonly void M2() { }
        }

        [NonCopyable] public ref struct RS1
        {
            public ref S1 F1;
            public ref readonly S1 F2;
        }
        """);

    [TestMethod]
    public Task Obscure_BaseMethodCall() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        [NonCopyable] public struct S1
        {
            public readonly string ToString1()
            {
                return {|#0:ToString()|};
            }

            public readonly string ToString2()
            {
                return base.ToString();
            }

            public readonly string ToString3()
            {
                return {|#1:this.ToString()|};
            }
        }

        [NonCopyable] public struct S2
        {
            public readonly override string ToString()
            {
                return base.ToString();
            }

            public readonly string ToString1()
            {
                return ToString();
            }

            public readonly string ToString2()
            {
                return this.ToString();
            }
        }

        [NonCopyable] public struct S3
        {
            public override string ToString()
            {
                return base.ToString();
            }

            public readonly string ToString1()
            {
                return {|#2:ToString()|};
            }

            public readonly string ToString2()
            {
                return {|#3:this.ToString()|};
            }
        }
        """,
        unnecessary: [0, 1]);

    [TestMethod]
    public Task Obscure_StringInterpolation() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        public class C
        {
            public string M1(ref S1 a, ref S2 b, ref S3 c)
            {
                return $"{a}{b}{c}";
            }

            public string M2(in S1 a, in S2 b, in S3 c)
            {
                return $"{a}{b}{c}";
            }
        }

        [NonCopyable] public struct S1
        {
        }

        [NonCopyable] public struct S2
        {
            public override string ToString() => "";
        }

        [NonCopyable] public struct S3
        {
            public readonly override string ToString() => "";
        }
        """);

    [TestMethod]
    public Task Obscure_FalsePositiveLHS() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        public class C
        {
            public void M1(S1[] arr, TypedReference typedref, out S1 outParam, in S1 value)
            {
                outParam = default;

                // No defensive copies
                arr[0].X();
                __refvalue(typedref, S1).X();
                outParam.X();
                new S1().X();
                ((S1)value).X();
            }
        }

        [NonCopyable] public struct S1
        {
            public void X() { }
        }
        """);

    [TestMethod]
    public Task Obscure_Span() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        public class C
        {
            public void M1
            (
                ReadOnlySpan<S1> rospan, in ReadOnlySpan<S1> rospanIn, ref ReadOnlySpan<S1> rospanRef,
                Span<S1> span, in Span<S1> spanIn, ref Span<S1> spanRef
            )
            {
                // No defensive copies
                rospan[0].M2();
                rospanIn[0].M2();
                rospanRef[0].M2();

                span[0].M1();
                spanIn[0].M1();
                spanRef[0].M1();

                span[0].M2();
                spanIn[0].M2();
                spanRef[0].M2();

                foreach (var x in rospan) { }
                foreach (var x in rospanIn) { }
                foreach (var x in rospanRef) { }
                foreach (var x in span) { }
                foreach (var x in spanIn) { }
                foreach (var x in spanRef) { }
            }

            public void M2(ReadOnlySpan<S1> rospan, in ReadOnlySpan<S1> rospanIn, ref ReadOnlySpan<S1> rospanRef)
            {
                // All defensive copies
                {|#0:rospan[0].M1()|};
                {|#1:rospanIn[0].M1()|};
                {|#2:rospanRef[0].M1()|};
            }
        }

        [NonCopyable] public struct S1
        {
            public void M1() { }
            public readonly void M2() { }
        }
        """);

    [TestMethod]
    public Task Obscure_InlineArray() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        public class C
        {
            public void M1(in Buffer buf1, ref Buffer buf2)
            {
                // No defensive copies
                buf1[0].M2();
                buf1[^1].M2();
                buf1[2..4][^1].M2();

                buf2[0].M1();
                buf2[^1].M1();
                buf2[2..4][^1].M1();

                buf2[0].M2();
                buf2[^1].M2();
                buf2[2..4][^1].M2();

                foreach (var x in buf1) { }
                foreach (var x in buf2) { }
            }

            public void M2(in Buffer buf1)
            {
                // All defensive copies
                {|#0:buf1[0].M1()|};
                {|#1:buf1[^1].M1()|};
                {|#2:buf1[2..4][^1].M1()|};
            }
        }

        [NonCopyable] public struct S1
        {
            public void M1() { }
            public readonly void M2() { }
        }

        [NonCopyable] [InlineArray(10)]
        public struct Buffer
        {
            private S1 _element0;
        }
        """);

    [TestMethod]
    public Task Obscure_Checked() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        public class C
        {
            public void M1(in S1 value1, ref S1 value2)
            {
                // No defensive copies
                unchecked(value1).M2();
                checked(value1).M2();

                unchecked(value1).M2();
                checked(value2).M1();

                unchecked(value2).M2();
                checked(value2).M2();
            }

            public void M2(in S1 value1)
            {
                // All defensive copies
                {|#0:unchecked(value1).M1()|};
                {|#1:checked(value1).M1()|};
            }
        }

        [NonCopyable] public struct S1
        {
            public void M1() { }
            public readonly void M2() { }
        }
        """);

    [TestMethod]
    public Task Obscure_FixedField() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        public unsafe class C
        {
            public void M1(ref S1 value2)
            {
                // No defensive copies
                value2.X[0].ToString();
                (value2.X[0]).ToString();

                S1 value3 = default;
                (value3.X)[0].ToString();
            }

            public void M2(in S1 value1)
            {
                // All defensive copies (except that compiler doesn't emit them as such)
                value1.X[0].ToString();
                (value1.X[0]).ToString();

                int a = 0;
                (a == 0 ? ref a : ref value1.X[0]).ToString();
            }
        }

        [NonCopyable] public unsafe struct S1
        {
            public fixed int X[1];
        }
        """);

    [TestMethod]
    public Task Obscure_Enums() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        public class C
        {
            public void M1(ref E1 value1, E1 value2)
            {
                // No defensive copies
                value1.ToString();
                value2.ToString();
            }

            public void M2<[NonCopyable] T1, [NonCopyable] T2>(in E1 value1, in T1 value2, in T2 value3) where T1 : Enum where T2 : struct, Enum
            {
                // All defensive copies (unnecessary)
                {|#0:value1.ToString()|};
                {|#1:value2.ToString()|};
                {|#2:value3.ToString()|};
            }
        }

        public enum E1
        {
        }
        """,
        unnecessary: [0, 1, 2],
        reportedThough: [1, 2]);

    [TestMethod]
    public Task Obscure_Nameof() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Collections;
        using System.Collections.Generic;

        public class C
        {
            public unsafe void M1(in S1 value1, in RS1 value2)
            {
                // No defensive copies
                _ = nameof(S1);
                _ = nameof(value1);
                _ = nameof(value1.M1);
                _ = nameof(value1.P1);
                _ = nameof(value1.F1);
                _ = nameof(value1.E1);
                _ = nameof(value1.P2);
                _ = nameof(value1.P2.P1);
                fixed (char* ptr = nameof(value1.P2)) { }
                foreach (var c in nameof(value1.P2)) { }

                _ = nameof(RS1);
                _ = nameof(value2);
                _ = nameof(value2.M1);
                _ = nameof(value2.P1);
                _ = nameof(value2.F1);
                _ = nameof(value2.E1);
                _ = nameof(value2.P2);
                _ = nameof(value2.P2.P1);
                fixed (char* ptr = nameof(value2.P2)) { }
                foreach (var c in nameof(value2.P2)) { }
            }
        }

        [NonCopyable] public struct S1 : IEnumerable<S1>
        {
            public void M1() { }
            public int P1 => 0;
            public int F1;
            public event Action E1 { add { } remove { } }
            public ref readonly S1 P2 => throw null!;
            public ref readonly S1 GetPinnableReference() => throw null!;
            public IEnumerator<S1> GetEnumerator() => throw null!;
            IEnumerator IEnumerable.GetEnumerator() => throw null!;
        }

        [NonCopyable] public ref struct RS1 : IEnumerable<RS1>
        {
            public void M1() { }
            public int P1 => 0;
            public int F1;
            public event Action E1 { add { } remove { } }
            public ref readonly RS1 P2 => throw null!;
            public ref readonly RS1 GetPinnableReference() => throw null!;
            public IEnumerator<RS1> GetEnumerator() => throw null!;
            IEnumerator IEnumerable.GetEnumerator() => throw null!;
        }
        """);

    [TestMethod]
    public Task Obscure_RefReturnUses() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        public class C
        {
            public unsafe void M1(in S1 value)
            {
                // No defensive copies
                ref int a1 = ref Unsafe.NullRef<int>();
                ref readonly int b1 = ref Unsafe.NullRef<int>();
                ref S1 a2 = ref Unsafe.NullRef<S1>();
                ref readonly S1 b2 = ref Unsafe.NullRef<S1>();

                _ = value.ROR1;
                value.ROR1 = 1;
                value.ROR1++;
                a1 = value.ROR1;
                a1 = ref value.ROR1;

                _ = value.RORRO1;
                a1 = value.RORRO1;
                b1 = ref value.RORRO1;

                _ = value.ROR2;
                value.ROR2 = new();
                value.ROR2.M1();
                value.ROR2.M2();
                a2 = value.ROR2;
                a2 = ref value.ROR2;

                _ = value.RORRO2;
                value.RORRO2.M2();
                a2 = value.RORRO2;
                b2 = ref value.RORRO2;
            }

            public unsafe void M2(in S1 value)
            {
                // All defensive copies
                ref int a1 = ref Unsafe.NullRef<int>();
                ref readonly int b1 = ref Unsafe.NullRef<int>();
                ref S1 a2 = ref Unsafe.NullRef<S1>();
                ref readonly S1 b2 = ref Unsafe.NullRef<S1>();

                _ = {|#0:value.R1|};
                {|#1:value.R1|} = 1;
                {|#2:value.R1|}++;
                a1 = {|#3:value.R1|};
                a1 = ref {|#4:value.R1|};

                _ = {|#5:value.RRO1|};
                a1 = {|#6:value.RRO1|};
                b1 = ref {|#7:value.RRO1|};

                _ = {|#8:value.R2|};
                {|#9:value.R2|} = new();
                {|#10:value.R2|}.M1();
                {|#11:value.R2|}.M2();
                a2 = {|#12:value.R2|};
                a2 = ref {|#13:value.R2|};

                _ = {|#14:value.RRO2|};
                {|#16:{|#15:value.RRO2|}.M1()|};
                {|#17:value.RRO2|}.M2();
                a2 = {|#18:value.RRO2|};
                b2 = ref {|#19:value.RRO2|};

                {|#20:value.RORRO2.M1()|};
            }
        }

        [NonCopyable] public struct S1
        {
            public void M1() { }
            public readonly void M2() { }

            public ref int R1 => throw null!;
            public readonly ref int ROR1 => throw null!;
            public ref readonly int RRO1 => throw null!;
            public readonly ref readonly int RORRO1 => throw null!;

            public ref S1 R2 => throw null!;
            public readonly ref S1 ROR2 => throw null!;
            public ref readonly S1 RRO2 => throw null!;
            public readonly ref readonly S1 RORRO2 => throw null!;
        }
        """);

    [TestMethod]
    public Task Obscure_MethodGroupDelegateConversion() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        public class C
        {
            public unsafe void M1(in S1 value1, ref S1 value2, S1 value3)
            {
                // No defensive copies
                Consume(value1.M1);
                Consume(value1.M2);
                Consume(value2.M1);
                Consume(value2.M2);
                Consume(value3.M1);
                Consume(value3.M2);
            }

            private void Consume<[NonCopyable] T>(T value) where T : Delegate { }
        }

        [NonCopyable] public struct S1
        {
            public void M1() { }
            public readonly void M2() { }
        }
        """);

    [TestMethod]
    public Task Obscure_AsyncGetAwaiter() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;

        public class C<[NonCopyable] T> where T : I1
        {
            public readonly S1 s1RO;
            public S1 s1;
            public readonly S2 s2RO;
            public S2 s2;
            public readonly T tRO;
            public async Task M1RO() => {|#0:await s1RO|};
            public async Task M1() => await s1;
            public async Task M2RO() => await s2RO;
            public async Task M2() => await s2;
            public async Task M3RO() => {|#1:await tRO|};
            public async Task M4RO() => {|#2:await tRO!|};
        }

        [NonCopyable] public struct S1
        {
            public TaskAwaiter GetAwaiter() => default;
        }

        [NonCopyable] public readonly struct S2
        {
            public TaskAwaiter GetAwaiter() => default;
        }

        public interface I1
        {
            TaskAwaiter GetAwaiter();
        }
        """);

    [TestMethod]
    public Task Obscure_InterpolatedStringParameterInReadOnlyStruct() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Diagnostics;

        [NonCopyable] public readonly struct S1
        {
            public readonly int f1;
            public void X()
            {
                Debug.Assert(f1 <= 0, $"A: {f1}");
            }
        }
        """);

    [TestMethod]
    public Task Obscure_StructOnStructOnROClassField() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        public class C1
        {
            public S1 Field1;
            public S3 Field2;
            public static readonly C1 instance = new();

            public void M()
            {
                instance.Field1.M();
                instance.Field1.Field1.M();
                {|#0:instance.Field1.Field2.M()|};
                instance.Field2[1].M();
            }
        }

        [NonCopyable] public struct S1
        {
            public void M() { }
            public S2 Field1;
            public readonly S2 Field2;
        }

        [NonCopyable] public struct S2
        {
            public void M() { }
        }

        [NonCopyable] [InlineArray(2)]
        public struct S3
        {
            private S2 _field;
        }
        """);

    [TestMethod]
    public Task Obscure_CallMutableMemberOnReadonlyInAllowMutatingMembers() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        public class C1
        {
            private readonly S3 _field1;
            private readonly S4 _field2;
            private readonly S5 _field3;
            private readonly S6 _field4;
            public C1()
            {
                _field1.M();
                _field2[0].M();
                _field3.Field.M();
                {|#0:_field4.Field.M()|};
            }
            public int P1
            {
                init
                {
                    _field1.M();
                    _field2[0].M();
                    _field3.Field.M();
                    {|#1:_field4.Field.M()|};
                }
            }
            public int P2
            {
                set
                {
                    {|#2:_field1.M()|};
                    {|#3:_field2[0].M()|};
                    {|#4:_field3.Field.M()|};
                    {|#5:_field4.Field.M()|};
                }
            }
        }

        [NonCopyable] public struct S1
        {
            private readonly S3 _field1;
            private readonly S4 _field2;
            private readonly S5 _field3;
            private readonly S6 _field4;
            public S1()
            {
                _field1.M();
                _field2[0].M();
                _field3.Field.M();
                {|#6:_field4.Field.M()|};
            }
            public int P1
            {
                init
                {
                    _field1.M();
                    _field2[0].M();
                    _field3.Field.M();
                    {|#7:_field4.Field.M()|};
                }
            }
            public int P2
            {
                set
                {
                    {|#8:_field1.M()|};
                    {|#9:_field2[0].M()|};
                    {|#10:_field3.Field.M()|};
                    {|#11:_field4.Field.M()|};
                }
            }
        }

        [NonCopyable] public readonly struct S2(S3 _field1, S4 _field2, S5 _field3, S6 _field4)
        {
            public S2() : this(default, default, default, default)
            {
                // _field1.M();
                // _field2[0].M();
                // _field3.Field.M();
                // _field4.Field.M();
            }
            public int P1
            {
                init
                {
                    _field1.M();
                    _field2[0].M();
                    _field3.Field.M();
                    {|#12:_field4.Field.M()|};
                }
            }
            public int P2
            {
                set
                {
                    {|#13:_field1.M()|};
                    {|#14:_field2[0].M()|};
                    {|#15:_field3.Field.M()|};
                    {|#16:_field4.Field.M()|};
                }
            }
            public readonly int _fieldExtra1 = _field1.M();
            public readonly int _fieldExtra2 = _field2[0].M();
            public readonly int _fieldExtra3 = _field3.Field.M();
            public readonly int _fieldExtra4 = {|#17:_field4.Field.M()|};
            public int PropExtra1 { get; } = _field1.M();
            public int PropExtra2 { get; } = _field2[0].M();
            public int PropExtra3 { get; } = _field3.Field.M();
            public int PropExtra4 { get; } = {|#18:_field4.Field.M()|};
        }

        [NonCopyable] public struct S3
        {
            public int M() => 0;
        }

        [NonCopyable] [InlineArray(2)]
        public struct S4
        {
            private S3 _field;
        }

        [NonCopyable] public struct S5
        {
            public S3 Field;
        }

        [NonCopyable] public struct S6
        {
            public readonly S3 Field;
        }
        """);

    [TestMethod]
    public Task Obscure_MutateReadonlyStaticFieldInStaticConstructor() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        public class C1
        {
            private static readonly S1 _field1;
            private static readonly S2 _field2;
            private static readonly S3 _field3;
            private static readonly S4 _field4;
            public static int Field1 = _field1.M();
            public static int Field2 = _field2[0].M();
            public static int Field3 = _field3.Field.M();
            public static int Field4 = {|#0:_field4.Field.M()|};
            public int InstanceField1 = {|#1:_field1.M()|};
            public int InstanceField2 = {|#2:_field2[0].M()|};
            public int InstanceField3 = {|#3:_field3.Field.M()|};
            public int InstanceField4 = {|#4:_field4.Field.M()|};
            static C1()
            {
                _field1.M();
                _field2[0].M();
                _field3.Field.M();
                {|#5:_field4.Field.M()|};
            }
        }

        [NonCopyable] public struct S1
        {
            public int M() => 0;
        }

        [NonCopyable] [InlineArray(2)]
        public struct S2
        {
            private S1 _field;
        }

        [NonCopyable] public struct S3
        {
            public S1 Field;
        }

        [NonCopyable] public struct S4
        {
            public readonly S1 Field;
        }
        """);

    [TestMethod]
    public Task Obscure_ReadonlyInitAccessor() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        [NonCopyable] public struct S1
        {
            private S3 a;
            private readonly S3 b;

            public readonly int A
            {
                init => a.ToString();
            }

            public int A1
            {
                readonly get => 1;
                init => a.ToString();
            }

            public readonly int A2
            {
                get => 1;
                init => a.ToString();
            }

            public int A3
            {
                init => a.ToString();
            }

            public readonly int B
            {
                init => b.ToString();
            }

            public int B1
            {
                readonly get => 1;
                init => b.ToString();
            }

            public readonly int B2
            {
                get => 1;
                init => b.ToString();
            }

            public int B3
            {
                init => b.ToString();
            }
        }

        [NonCopyable] public readonly struct S2
        {
            private readonly S3 b;

            public readonly int B
            {
                init => b.ToString();
            }

            public int B1
            {
                readonly get => 1;
                init => b.ToString();
            }

            public readonly int B2
            {
                get => 1;
                init => b.ToString();
            }

            public int B3
            {
                init => b.ToString();
            }
        }

        [NonCopyable] public struct S3
        {
            public override string ToString() => "";
        }
        """);

    [TestMethod]
    public Task Obscure_ContrainedCallOnReadonlyStruct() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        [NonCopyable] public struct S1
        {
            private S2 a;
            private S3 b;
            public readonly string M1() => a.ToString();
            public readonly string M2() => {|#0:b.ToString()|};
        }

        [NonCopyable] public readonly struct S2
        {
            public void Test1() => ToString();
            public void Test2() => this.ToString();
            public void Test3() => base.ToString();
        }

        [NonCopyable] public struct S3
        {
        }
        """,
        unnecessary: [0]);

    [TestMethod]
    public Task Standard_AllRHS() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        public class C
        {
            public void M1(in S1 value)
            {
                // No defensive copies
                _ = value.F1;
                _ = value.rF1;
                _ = value.P1;
                _ = value.P3;
                _ = value.rP1;
                _ = value.rP2;
                value.rE1 += null;
                value.rE1 -= null;
                value.rM1();
                _ = value.rM2();
                _ = ref value.rM2();
                value.rM2() = 1;
                _ = value[(long)0];
                value[(long)0] = 0;
            }
            public void M2(in S1 value)
            {
                // All defensive copies
                _ = {|#0:value.P2|};
                _ = {|#1:value.rP3|};
                {|#2:value.E1|} += null;
                {|#3:value.E1|} -= null;
                {|#4:value.E2|} += null;
                {|#5:value.E2|} -= null;
                {|#6:value.M1()|};
                _ = {|#7:value.M2()|};
                _ = ref {|#8:value.M2()|};
                {|#9:value.M2()|} = 1;
                _ = {|#10:value[(int)0]|};
            }
        }
        [NonCopyable] public struct S1
        {
            public int F1;
            public readonly int rF1;
            public int P1 { get; set; } // readonly get
            public int P2 { get => F1; set => F1 = value; }
            public int P3 { get; } // readonly get
            public readonly int rP1 { get => 0; set { } }
            public int rP2 { readonly get => 0; set { } }
            public int rP3 { get => 0; readonly set { } }
            public event Action E1;
            public event Action E2 { add { } remove { } }
            public readonly event Action rE1 { add { } remove { } }
            public void M1() { }
            public readonly void rM1() { }
            public ref int M2() => ref Unsafe.NullRef<int>();
            public readonly ref int rM2() => ref Unsafe.NullRef<int>();
            public int this[int x] { get => 0; set { } }
            public readonly int this[long x] { get => 0; set { } }
        }
        """);

    [TestMethod]
    public Task Standard_AllRHSThroughMultipleFieldAccess() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        public class C
        {
            public void M1(in S3 value)
            {
                // No defensive copies
                _ = value.Val.Val.F1;
                _ = value.Val.Val.rF1;
                _ = value.Val.Val.P1;
                _ = value.Val.Val.P3;
                _ = value.Val.Val.rP1;
                _ = value.Val.Val.rP2;
                value.Val.Val.rE1 += null;
                value.Val.Val.rE1 -= null;
                value.Val.Val.rM1();
                _ = value.Val.Val.rM2();
                _ = ref value.Val.Val.rM2();
                value.Val.Val.rM2() = 1;
            }
            public void M2(in S3 value)
            {
                // All defensive copies
                _ = {|#0:value.Val.Val.P2|};
                _ = {|#1:value.Val.Val.rP3|};
                {|#2:value.Val.Val.E1|} += null;
                {|#3:value.Val.Val.E1|} -= null;
                {|#4:value.Val.Val.E2|} += null;
                {|#5:value.Val.Val.E2|} -= null;
                {|#6:value.Val.Val.M1()|};
                _ = {|#7:value.Val.Val.M2()|};
                _ = ref {|#8:value.Val.Val.M2()|};
                {|#9:value.Val.Val.M2()|} = 1;
            }
        }
        [NonCopyable] public struct S1
        {
            public int F1;
            public readonly int rF1;
            public int P1 { get; set; } // readonly get
            public int P2 { get => F1; set => F1 = value; }
            public int P3 { get; } // readonly get
            public readonly int rP1 { get => 0; set { } }
            public int rP2 { readonly get => 0; set { } }
            public int rP3 { get => 0; readonly set { } }
            public event Action E1;
            public event Action E2 { add { } remove { } }
            public readonly event Action rE1 { add { } remove { } }
            public void M1() { }
            public readonly void rM1() { }
            public ref int M2() => ref Unsafe.NullRef<int>();
            public readonly ref int rM2() => ref Unsafe.NullRef<int>();
        }
        [NonCopyable] public struct S2
        {
            public S1 Val;
        }
        [NonCopyable] public struct S3
        {
            public S2 Val;
        }
        """);

    [TestMethod]
    public Task Standard_BracketedAccess() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        public class C
        {
            public void M1(in S3 value)
            {
                // No defensive copies

                (value.Val.Val.rE) += null;
                (value.Val.Val.rE) -= null;
                ((((value)).Val.Val).rE) += null;
                ((((value.Val).Val)).rE) -= null;

                (value.Val.Val.rP1) = 0;
                ((((value)).Val.Val).rP1) = 0;
                _ = (value.Val.Val.rP1);
                _ = ((((value)).Val.Val).rP1);

                (value.Val.Val[(uint)0]) = 0;
                ((((value)).Val.Val)[(uint)0]) = 0;
                _ = (value.Val.Val[(uint)0]);
                _ = ((((value)).Val.Val)[(uint)0]);
            }
            public void M2(in S3 value)
            {
                // All defensive copies

                ({|#0:value.Val.Val.E|}) += null;
                ({|#1:value.Val.Val.E|}) -= null;
                (({|#2:(((value)).Val.Val).E|})) += null;
                (({|#3:(((value.Val).Val)).E|})) -= null;

                // (value.Val.Val.P1) = 0;
                // ((((value)).Val.Val).P1) = 0;
                _ = ({|#4:value.Val.Val.P1|});
                _ = ({|#5:(((value)).Val.Val).P1|});
                {|#6:(value.Val.Val).M()|};
                {|#7:((((value)).Val.Val)).M()|};

                _ = ({|#8:value.Val.Val[(long)0]|});
                _ = ({|#9:(((value)).Val.Val)[(long)0]|});
            }
        }
        [NonCopyable] public struct S1
        {
            public event Action E;
            public readonly event Action rE { add { } remove { } }
            public int P1 { get => 0; set { } }
            public readonly int rP1 { get => 0; set { } }
            public void M() { }
            public readonly void rM() { }
            public readonly int this[uint a] { get => 0; set { } }
            public int this[long a] { get => 0; set { } }
        }
        [NonCopyable] public struct S2
        {
            public S1 Val;
        }
        [NonCopyable] public struct S3
        {
            public S2 Val;
        }
        """);

    [TestMethod]
    public Task Standard_RefProperties() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Diagnostics.CodeAnalysis;
        using System.Runtime.CompilerServices;

        public class C
        {
            public void M1(in S3 value)
            {
                // No defensive copies

                _ = value.Val.Val.S_RO_R;
                _ = value.Val.Val.S_RO_R_RO;
                value.Val.Val.S_RO_R = default;

                _ = value.Val.Val.U_RO_R;
                _ = value.Val.Val.U_RO_R_RO;
                value.Val.Val.U_RO_R = default;
            }

            public void M2(in S3 value)
            {
                // All defensive copies

                _ = {|#0:value.Val.Val.S_R|};
                _ = {|#1:value.Val.Val.S_R_RO|};
                {|#2:value.Val.Val.S_R|} = default;

                _ = {|#3:value.Val.Val.U_R|};
                _ = {|#4:value.Val.Val.U_R_RO|};
                {|#5:value.Val.Val.U_R|} = default;
            }

            public void M3(in S3 value)
            {
                // No defensive copies

                value.Val.Val.S_RO_R.M();
                value.Val.Val.S_RO_R.rM();
                value.Val.Val.S_RO_R_RO.rM();

                value.Val.Val.U_RO_R.M();
                value.Val.Val.U_RO_R.rM();
                value.Val.Val.U_RO_R_RO.rM();
            }

            public void M4(in S3 value)
            {
                // All defensive copies

                {|#6:value.Val.Val.S_R|}.M(); // S1
                {|#7:value.Val.Val.S_R|}.rM(); // S1
                {|#9:{|#8:value.Val.Val.S_R_RO|}.M()|}; // S1 and S2
                {|#10:value.Val.Val.S_R_RO|}.rM(); // S1
                {|#11:value.Val.Val.S_RO_R_RO.M()|}; // S2

                {|#12:value.Val.Val.U_R|}.M(); // S1
                {|#13:value.Val.Val.U_R|}.rM(); // S1
                {|#15:{|#14:value.Val.Val.U_R_RO|}.M()|}; // S1 and S2
                {|#16:value.Val.Val.U_R_RO|}.rM(); // S1
                {|#17:value.Val.Val.U_RO_R_RO.M()|}; // S2
            }
        }

        [NonCopyable] public struct S0
        {
            public void M() { }
            public readonly void rM() { }
        }

        [NonCopyable] public struct S1
        {
            public S0 F;

            // Scoped refs
            public ref S0 S_R => ref Unsafe.NullRef<S0>();
            public readonly ref S0 S_RO_R => ref Unsafe.NullRef<S0>();
            public ref readonly S0 S_R_RO => ref Unsafe.NullRef<S0>();
            public readonly ref readonly S0 S_RO_R_RO => ref Unsafe.NullRef<S0>();

            // Unscoped refs
            [UnscopedRef] public ref S0 U_R => ref F;
            [UnscopedRef] public readonly ref S0 U_RO_R => ref Unsafe.NullRef<S0>();
            [UnscopedRef] public ref readonly S0 U_R_RO => ref F;
            [UnscopedRef] public readonly ref readonly S0 U_RO_R_RO => ref F;
        }
        [NonCopyable] public struct S2
        {
            public S1 Val;
        }
        [NonCopyable] public struct S3
        {
            public S2 Val;
        }
        """);

    [TestMethod]
    public Task Standard_Indexers() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Diagnostics.CodeAnalysis;
        using System.Runtime.CompilerServices;

        public class C
        {
            public void M1(in S3 value)
            {
                // No defensive copies

                _ = value.Val.Val[(long)0];
                _ = value.Val.Val[(ulong)0];
                _ = value.Val.Val[(sbyte)0];

                value.Val.Val[(ulong)0] = 0;
                value.Val.Val[(byte)0] = 0;
                value.Val.Val[(ushort)0] = 0;

                _ = value.Val.Val[0, 1, 2, 3];
                value.Val.Val[0, 1, 2, 3] = 0;

                _ = value.Val.Val[(object)null, (object)null];
                value.Val.Val[(object)null, (object)null].rM();
            }

            public void M2(in S3 value)
            {
                // All defensive copies

                _ = {|#0:value.Val.Val[(int)0]|};
                _ = {|#1:value.Val.Val[(uint)0]|};
                _ = {|#2:value.Val.Val[(byte)0]|};

                _ = {|#3:value.Val.Val[0, 1, 2]|};

                {|#4:value.Val.Val[(object)null, (object)null].M()|};
                _ = {|#5:value.Val.Val[(object)null, (object)null, (object)null]|};
                {|#6:value.Val.Val[(object)null, (object)null, (object)null]|} = 0;
            }
        }

        [NonCopyable] public struct S0
        {
            public void M() { }
            public readonly void rM() { }
        }
        [NonCopyable] public struct S1
        {
            public int this[int x] => 0;
            public readonly int this[long x] => 0;
            public int this[uint x] { get => 0; set { } }
            public readonly int this[ulong x] { get => 0; set { } }
            public int this[sbyte x] { readonly get => 0; set { } }
            public int this[byte x] { get => 0; readonly set { } }
            public readonly int this[ushort x] { set { } }
            public int this[int a, int b, object c] { get => 0; set { } }
            public readonly int this[int a, int b, int c, object d] { get => 0; set { } }
            public readonly ref readonly S0 this[object o1, object o2] => ref Unsafe.NullRef<S0>();
            public ref int this[object o1, object o2, object o3] => ref Unsafe.NullRef<int>();
        }
        [NonCopyable] public struct S2
        {
            public S1 Val;
        }
        [NonCopyable] public struct S3
        {
            public S2 Val;
        }
        """);

    [TestMethod]
    public Task Standard_AllLHS() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        public class C
        {
            public void M1(in S1 value1, S2 value2)
            {
                // No defensive copies
                value1.rM();

                value2.Value_R.rM();
                value2.Value_R_RO.rM();
                value2.Value_RO_R.rM();
                value2.Value_RO_R_RO.rM();
                value2.Value_R.M();
                value2.Value_RO_R.M();

                value2.Prop_R.M();
                value2.Prop_R.rM();
                value2.Prop_R_RO.rM();

                value2.M_R().M();
                value2.M_R().rM();
                value2.M_R_RO().rM();

                ref readonly S1 local1 = ref value1;
                local1.rM();

                value2.Field.rM();
                value2.Field.M();
                value2.Field_RO.rM();
            }
            public void M2(in S1 value1, S2 value2)
            {
                // All defensive copies
                {|#0:value1.M()|};

                {|#1:value2.Value_R_RO.M()|};
                {|#2:value2.Value_RO_R_RO.M()|};

                {|#3:value2.Prop_R_RO.M()|};

                {|#4:value2.M_R_RO().M()|};

                ref readonly S1 local1 = ref value1;
                {|#5:local1.M()|};

                {|#6:value2.Field_RO.M()|};
            }
        }
        [NonCopyable] public struct S1
        {
            public void M() { }
            public readonly void rM() { }
        }
        [NonCopyable] public ref struct S2
        {
            public ref S1 Value_R;
            public ref readonly S1 Value_R_RO;
            public readonly ref S1 Value_RO_R;
            public readonly ref readonly S1 Value_RO_R_RO;

            public readonly ref S1 Prop_R => ref Unsafe.NullRef<S1>();
            public readonly ref readonly S1 Prop_R_RO => ref Unsafe.NullRef<S1>();

            public readonly ref S1 M_R() => ref Unsafe.NullRef<S1>();
            public readonly ref readonly S1 M_R_RO() => ref Unsafe.NullRef<S1>();

            public S1 Field;
            public readonly S1 Field_RO;
        }
        """);

    [TestMethod]
    public Task Standard_RefFieldAssignment() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        public class C
        {
            public void M1(in S1 value1, S1 value2)
            {
                // No defensive copies
                ref int local = ref Unsafe.NullRef<int>();

                value1.Field_R = 1;
                value1.Field_RO_R = 1;
                value2.Field_R = 1;
                value2.Field_RO_R = 1;

                _ = value1.Field_R;
                _ = value1.Field_RO_R;
                _ = value2.Field_R;
                _ = value2.Field_RO_R;

                _ = value1.Field_R_RO;
                _ = value1.Field_RO_R_RO;
                _ = value2.Field_R_RO;
                _ = value2.Field_RO_R_RO;

                value2.Field_R = ref local;
                value2.Field_R_RO = ref local;

                _ = ref value1.Field_R;
                _ = ref value1.Field_RO_R;
                _ = ref value2.Field_R;
                _ = ref value2.Field_RO_R;

                _ = ref value1.Field_R_RO;
                _ = ref value1.Field_RO_R_RO;
                _ = ref value2.Field_R_RO;
                _ = ref value2.Field_RO_R_RO;
            }
        }
        [NonCopyable] public ref struct S1
        {
            public ref int Field_R;
            public ref readonly int Field_R_RO;
            public readonly ref int Field_RO_R;
            public readonly ref readonly int Field_RO_R_RO;
        }
        """);

    [TestMethod]
    public Task Standard_Delegates() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Diagnostics.CodeAnalysis;
        using System.Runtime.CompilerServices;

        public unsafe class C
        {
            public void M1(in S3 value1, in S1 value2)
            {
                // No defensive copies
                Action x;

                x = value1.Val.Val.M;
                x = value1.Val.Val.rM;

                x = value2.M;
                x = value2.rM;
            }
        }

        [NonCopyable] public struct S1
        {
            public void M() { }
            public readonly void rM() { }
        }
        [NonCopyable] public struct S2
        {
            public S1 Val;
        }
        [NonCopyable] public struct S3
        {
            public S2 Val;
        }
        """);

    [TestMethod]
    public Task Standard_Pointers() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Diagnostics.CodeAnalysis;
        using System.Runtime.CompilerServices;

        public unsafe class C
        {
            public unsafe void M1(in S3* value1, in S1* value2)
            {
                // No defensive copies

                value1->Val.Val.rM();
                value1->Val.Val.M();

                value1->Val.RO_Val->rM();
                value1->Val.RO_Val->M();

                value2->rM();
                value2->M();

                (*value1).Val.Val.rM();
                (*value1).Val.Val.M();

                (*value1).Val.RO_Val->rM();
                (*value1).Val.RO_Val->M();

                (*value2).rM();
                (*value2).M();

                value1[0].Val.Val.rM();
                value1[0].Val.Val.M();

                value1[0].Val.RO_Val->rM();
                value1[0].Val.RO_Val->M();

                value2[0].rM();
                value2[0].M();
            }
        }

        [NonCopyable] public struct S1
        {
            public void M() { }
            public readonly void rM() { }
        }
        [NonCopyable] public unsafe struct S2
        {
            public S1 Val;
            public readonly S1* RO_Val;
        }
        [NonCopyable] public struct S3
        {
            public S2 Val;
        }
        """);

    [TestMethod]
    public Task Standard_AllPositionsNeedingCheck() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        // All defensive copies
        {|#0:C.X().M1()|};

        public class C
        {
            // The ref we will be acting on
            public static ref readonly S1 X() => ref Unsafe.NullRef<S1>();

            // All defensive copies
            public C() => {|#1:X().M1()|};
            public C(int a) { {|#2:X().M1()|}; }
            public void M() => {|#3:X().M1()|};
            public void M(int a) { {|#4:X().M1()|}; }
            public int P1 => {|#5:X().M2()|};
            public int P2
            {
                get => {|#6:X().M2()|};
                set => {|#7:X().M1()|};
            }
            public int P3
            {
                get { return {|#8:X().M2()|}; }
                set { {|#9:X().M1()|}; }
            }
            public event Action E1
            {
                add => {|#10:X().M1()|};
                remove => {|#11:X().M1()|};
            }
            public event Action E2
            {
                add { {|#12:X().M1()|}; }
                remove { {|#13:X().M1()|}; }
            }
            public int F1 = {|#14:X().M2()|};
            public static int F2 = {|#15:X().M2()|};
            public static int operator +(C a) => {|#16:X().M2()|};
            public static int operator +(C a, C b) => {|#17:X().M2()|};
            public static implicit operator int(C a) => {|#18:X().M2()|};
            public static explicit operator long(C a) => {|#19:X().M2()|};
            public int this[int a] => {|#20:X().M2()|};
            public int this[int a, int b]
            {
                get => {|#21:X().M2()|};
                set => {|#22:X().M1()|};
            }
            public int this[int a, int b, int c]
            {
                get { return {|#23:X().M2()|}; }
                set { {|#24:X().M1()|}; }
            }
            static C() { {|#25:X().M1()|}; }
            ~C() { {|#26:X().M1()|}; }
            class D
            {
                static D() => {|#27:X().M1()|};
                ~D() => {|#28:X().M1()|};
            }
            delegate void DelType(in S1 val);
            public void LocalMethodTest()
            {
                DelType a = (in S1 x) => {|#29:x.M1()|};
                void M(in S1 x) { {|#30:x.M1()|}; }
            }
            public int P4 { get; set; } = {|#31:X().M2()|};
            public int F3 = {|#32:X().M2()|}, F4 = {|#33:X().M2()|}, F5;
        }

        [NonCopyable] public struct S1
        {
            public void M1() { }
            public int M2() => 0;
        }
        """,
        isLibrary: false);

    [TestMethod]
    public Task Standard_RefReadonlyParameter() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        public class C
        {
            // All defensive copies
            public void A1(in S1 a) => {|#0:a.M1()|};
            public void B1(ref readonly S1 a) => {|#1:a.M1()|};

            // No defensive copies
            public void A2(in S1 a) => a.M2();
            public void B2(ref readonly S1 a) => a.M2();
            public void C1(ref S1 a) => a.M1();
            public void C2(ref S1 a) => a.M2();
            public void D1(out S1 a) { a = default; a.M1(); }
            public void D2(out S1 a) { a = default; a.M2(); }
        }

        [NonCopyable] public struct S1
        {
            public void M1() { }
            public readonly void M2() { }
        }
        """);

    [TestMethod]
    public Task Standard_PrimaryConstructors() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        [NonCopyable] public readonly struct S1(S3 a)
        {
            // All defensive copies
            public void M11() => {|#0:a.M1()|};
            public void M21() { {|#1:a.M1()|}; }
            public string P11 => {|#2:a.M1()|};
            public string P21
            {
                get => {|#3:a.M1()|};
                set => {|#4:a.M1()|};
            }
            public string P31
            {
                get { return {|#5:a.M1()|}; }
                set { {|#6:a.M1()|}; }
            }
            public event Action A11
            {
                add => {|#7:a.M1()|};
                remove => {|#8:a.M1()|};
            }
            public event Action A21
            {
                add { {|#9:a.M1()|}; }
                remove { {|#10:a.M1()|}; }
            }
            public string this[int x] => {|#11:a.M1()|};
            public string this[long x]
            {
                get => {|#12:a.M1()|};
                set => {|#13:a.M1()|};
            }
            public string this[short x]
            {
                get { return {|#14:a.M1()|}; }
                set { {|#15:a.M1()|}; }
            }

            // No defensive copies
            public void M12() => a.M2();
            public void M22() { a.M2(); }
            public readonly string F1 = a.M1();
            public readonly string F2 = a.M2();
            public string P12 => a.M2();
            public string P22
            {
                get => a.M2();
                set => a.M2();
            }
            public string P32
            {
                get { return a.M2(); }
                set { a.M2(); }
            }
            public string P41 { get; } = a.M1();
            public string P51 { get; init; } = a.M1();
            public string P42 { get; } = a.M2();
            public string P52 { get; init; } = a.M2();
            public event Action A12
            {
                add => a.M2();
                remove => a.M2();
            }
            public event Action A22
            {
                add { a.M2(); }
                remove { a.M2(); }
            }
            public string this[uint x] => a.M2();
            public string this[ulong x]
            {
                get => a.M2();
                set => a.M2();
            }
            public string this[ushort x]
            {
                get { return a.M2(); }
                set { a.M2(); }
            }
        }

        [NonCopyable] public struct S2(S3 a)
        {
            // No defensive copies
            public void M11() => a.M1();
            public void M21() { a.M1(); }
            public readonly string F1 = a.M1();
            public string P11 => a.M1();
            public string P21
            {
                get => a.M1();
                set => a.M1();
            }
            public string P31
            {
                get { return a.M1(); }
                set { a.M1(); }
            }
            public string P41 { get; } = a.M1();
            public string P51 { get; init; } = a.M1();
            public event Action A11
            {
                add => a.M1();
                remove => a.M1();
            }
            public event Action A21
            {
                add { a.M1(); }
                remove { a.M1(); }
            }
            public string this[int x] => a.M1();
            public string this[long x]
            {
                get => a.M1();
                set => a.M1();
            }
            public string this[short x]
            {
                get { return a.M1(); }
                set { a.M1(); }
            }

            public void M12() => a.M2();
            public void M22() { a.M2(); }
            public readonly string F2 = a.M2();
            public string P12 => a.M2();
            public string P22
            {
                get => a.M2();
                set => a.M2();
            }
            public string P32
            {
                get { return a.M2(); }
                set { a.M2(); }
            }
            public string P42 { get; } = a.M2();
            public string P52 { get; init; } = a.M2();
            public event Action A12
            {
                add => a.M2();
                remove => a.M2();
            }
            public event Action A22
            {
                add { a.M2(); }
                remove { a.M2(); }
            }
            public string this[uint x] => a.M2();
            public string this[ulong x]
            {
                get => a.M2();
                set => a.M2();
            }
            public string this[ushort x]
            {
                get { return a.M2(); }
                set { a.M2(); }
            }

            // All defensive copies
            public readonly void M31() => {|#16:a.M1()|};
        }

        public class C1(S3 a)
        {
            // No defensive copies
            public void M11() => a.M1();
        }

        [NonCopyable] public struct S3
        {
            public string M1() => "";
            public readonly string M2() => "";
        }
        """);

    [TestMethod]
    public Task Standard_FieldKeyword() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        [NonCopyable] public struct S1
        {
            public void M1() { }
            public readonly void M2() { }
        }

        [NonCopyable] public struct S2
        {
            public S1 P1
            {
                get
                {
                    field.M1();
                    return field;
                }
                set
                {
                    field.M1();
                    value.M1();
                    field = value;
                }
            }

            public readonly S1 P2
            {
                get
                {
                    {|#0:field.M1()|};
                    field.M2();
                    return field;
                }
                set
                {
                    {|#1:field.M1()|};
                    field.M2();
                    value.M1();
                }
            }

            public S1 P3
            {
                readonly get
                {
                    {|#2:field.M1()|};
                    field.M2();
                    return field;
                }
                set
                {
                    field.M1();
                    value.M1();
                    field = value;
                }
            }

            public S1 P4
            {
                get
                {
                    field.M1();
                    return field;
                }
                readonly set
                {
                    {|#3:field.M1()|};
                    field.M2();
                    value.M1();
                }
            }
        }

        [NonCopyable] public struct S3
        {
            public readonly S1 field;

            public S1 P1
            {
                get
                {
                    field.M1();
                    {|#4:this.field.M1()|};
                    this.field.M2();
                    return field;
                }
                set
                {
                    field.M1();
                    {|#5:this.field.M1()|};
                    this.field.M2();
                    value.M1();
                }
            }

            public readonly S1 P2
            {
                get
                {
                    {|#6:field.M1()|};
                    field.M2();
                    {|#7:this.field.M1()|};
                    this.field.M2();
                    return field;
                }
                set
                {
                    {|#8:field.M1()|};
                    field.M2();
                    {|#9:this.field.M1()|};
                    this.field.M2();
                    value.M1();
                }
            }

            public void M1()
            {
                {|#10:field.M1()|};
                field.M2();
                {|#11:this.field.M1()|};
                this.field.M2();
            }

            public readonly void M2()
            {
                {|#12:field.M1()|};
                field.M2();
                {|#13:this.field.M1()|};
                this.field.M2();
            }
        }
        """);

    [TestMethod]
    public Task Standard_NullableSuppression() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        #nullable enable

        using System;

        public class C
        {
            public void M1<[NonCopyable] T1, [NonCopyable] T2>(in T1 value1, in ValueTuple<T2> value2)
                where T2 : I1
            {
                var x = ({|#0:value1!.ToString()|})!;
                var y = ({|#1:(value1!).ToString()|}!);
                _ = ({|#2:(value2!).Item1![(long)0]|}!);
                {|#3:(value2!).Item1!.E|} += null;
            }
        }

        public interface I1
        {
            int? this[long a] { get; }
            event Action? E;
        }
        """);

    [TestMethod]
    public Task Standard_ExtensionMethods() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        #nullable enable

        using System;

        public class C
        {
            public void M1(S1 value1, in S1 value2, ref readonly S1 value3)
            {
                value1.M1();
                value1.M2();
                value1.M3();
                value2.M1();
                value2.M2();
                value2.M3();
                value3.M1();
                value3.M2();
                value3.M3();
            }
        }

        [NonCopyable] public struct S1
        {
        }

        public static class Extensions
        {
            public static void M1(this S1 value) { }
            public static void M2(this in S1 value) { }
            public static void M3(this ref readonly S1 value) { }
        }
        """);

    [TestMethod]
    public Task Standard_Extensions() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;

        public class C
        {
            public void M1(Struct1 value1, in Struct1 value2, ref readonly Struct1 value3)
            {
                value1.M1();
                value1.M2();
                value1.M3();
                _ = value1.G1;
                _ = value1.G2;
                _ = value1.G3;
                value1.S1 = 0;
                value1.S2 = 0;
                value1.S3 = 0;

                value2.M1();
                value2.M2();
                value2.M3();

                value3.M1();
                value3.M2();
                value3.M3();
            }
        }

        [NonCopyable] public struct Struct1
        {
        }

        public static class Extensions
        {
            extension(Struct1 x)
            {
                public void M1() { }
                public int G1 => 0;
                public int S1 { set { } }
            }

            extension(in Struct1 x)
            {
                public void M2() { }
                public int G2 => 0;
                public int S2 { set { } }
            }

            extension(ref readonly Struct1 x)
            {
                public void M3() { }
                public int G3 => 0;
                public int S3 { set { } }
            }
        }
        """);

    [TestMethod]
    public Task Within_ThroughField() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        [NonCopyable] public struct C
        {
            public S1 Value;
            public void M1()
            {
                // No defensive copies
                this.Value.rM();
                Value.rM();

                _ = Value.rP;
                Value.rP = 0;

                Value.F = 0;

                _ = Value.P_RO_R;
                _ = ref Value.P_RO_R;
                Value.P_RO_R = 0;

                _ = Value.P_RO_R_RO;
                _ = ref Value.P_RO_R_RO;

                _ = Value[(long)0];
                Value[(long)0] = 0;
            }
            public void M2()
            {
                // No defensive copies
                this.Value.M();
                Value.M();

                _ = Value.P;
                Value.P = 0;

                _ = Value.P_R;
                _ = ref Value.P_R;
                Value.P_R = 0;

                _ = Value.P_R_RO;
                _ = ref Value.P_R_RO;

                _ = Value[(uint)0];
                Value[(uint)0] = 0;
            }
            public readonly void rM1()
            {
                // No defensive copies
                this.Value.rM();
                Value.rM();

                _ = Value.rP;
                Value.rP = 0;

                _ = Value.rF;
                _ = Value.F;

                _ = ref Value.rF;
                _ = ref Value.F;

                _ = Value.P_RO_R;
                _ = ref Value.P_RO_R;
                Value.P_RO_R = 0;

                _ = Value.P_RO_R_RO;
                _ = ref Value.P_RO_R_RO;

                _ = Value[(long)0];
                Value[(long)0] = 0;
            }
            public readonly void rM2()
            {
                // All defensive copies
                {|#0:this.Value.M()|};
                {|#1:Value.M()|};

                _ = {|#2:Value.P|};

                _ = {|#3:Value.P_R|};
                _ = ref {|#4:Value.P_R|};
                {|#5:Value.P_R|} = 0;

                _ = {|#6:Value.P_R_RO|};
                _ = ref {|#7:Value.P_R_RO|};

                _ = {|#8:Value[(uint)0]|};
            }
        }

        [NonCopyable] public struct S1
        {
            public readonly void rM() { }
            public void M() { }
            public readonly int rP { get => 0; set { } }
            public int P { get => 0; set { } }
            public readonly int rF;
            public int F;
            public ref int P_R => ref Unsafe.NullRef<int>();
            public ref readonly int P_R_RO => ref Unsafe.NullRef<int>();
            public readonly ref int P_RO_R => ref Unsafe.NullRef<int>();
            public readonly ref readonly int P_RO_R_RO => ref Unsafe.NullRef<int>();
            public event Action E { add { } remove { } }
            public readonly event Action rE { add { } remove { } }
            public int this[uint i] { get => 0; set { } }
            public readonly int this[long i] { get => 0; set { } }
        }
        """);

    [TestMethod]
    public Task Within_ThroughSelf() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        [NonCopyable] public struct C
        {
            public void M1()
            {
                // No defensive copies
                rM();
                this.rM();

                _ = rP;
                rP = 0;
                _ = this.rP;
                this.rP = 0;

                F = 0;
                this.F = 0;

                _ = P_RO_R;
                _ = ref P_RO_R;
                P_RO_R = 0;
                _ = this.P_RO_R;
                _ = ref this.P_RO_R;
                this.P_RO_R = 0;

                _ = P_RO_R_RO;
                _ = ref P_RO_R_RO;
                _ = this.P_RO_R_RO;
                _ = ref this.P_RO_R_RO;

                _ = this[(long)0];
                this[(long)0] = 0;
            }
            public void M2()
            {
                // No defensive copies
                M();
                this.M();

                _ = P;
                P = 0;
                _ = this.P;
                this.P = 0;

                _ = P_R;
                _ = ref P_R;
                P_R = 0;
                _ = this.P_R;
                _ = ref this.P_R;
                this.P_R = 0;

                _ = P_R_RO;
                _ = ref P_R_RO;
                _ = this.P_R_RO;
                _ = ref this.P_R_RO;

                _ = this[(uint)0];
                this[(uint)0] = 0;
            }
            public readonly void rM1()
            {
                // No defensive copies
                rM();
                this.rM();

                _ = rP;
                rP = 0;
                _ = this.rP;
                this.rP = 0;

                _ = rF;
                _ = F;
                _ = this.rF;
                _ = this.F;

                _ = ref rF;
                _ = ref F;
                _ = ref this.rF;
                _ = ref this.F;

                _ = P_RO_R;
                _ = ref P_RO_R;
                P_RO_R = 0;
                _ = this.P_RO_R;
                _ = ref this.P_RO_R;
                this.P_RO_R = 0;

                _ = P_RO_R_RO;
                _ = ref P_RO_R_RO;

                _ = this.P_RO_R_RO;
                _ = ref this.P_RO_R_RO;

                _ = this[(long)0];
                this[(long)0] = 0;
            }
            public readonly void rM2()
            {
                // All defensive copies
                {|#0:M()|};
                {|#1:this.M()|};

                _ = {|#2:P|};
                _ = {|#3:this.P|};

                _ = {|#4:P_R|};
                _ = ref {|#5:P_R|};
                {|#6:P_R|} = 0;
                _ = {|#7:this.P_R|};
                _ = ref {|#8:this.P_R|};
                {|#9:this.P_R|} = 0;

                _ = {|#10:P_R_RO|};
                _ = ref {|#11:P_R_RO|};
                _ = {|#12:this.P_R_RO|};
                _ = ref {|#13:this.P_R_RO|};

                _ = {|#14:this[(uint)0]|};
            }

            public readonly void rM() { }
            public void M() { }
            public readonly int rP { get => 0; set { } }
            public int P { get => 0; set { } }
            public readonly int rF;
            public int F;
            public ref int P_R => ref Unsafe.NullRef<int>();
            public ref readonly int P_R_RO => ref Unsafe.NullRef<int>();
            public readonly ref int P_RO_R => ref Unsafe.NullRef<int>();
            public readonly ref readonly int P_RO_R_RO => ref Unsafe.NullRef<int>();
            public event Action E { add { } remove { } }
            public readonly event Action rE { add { } remove { } }
            public int this[uint i] { get => 0; set { } }
            public readonly int this[long i] { get => 0; set { } }
        }
        """);

    [TestMethod]
    public Task Within_PossibleFalsePositives() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        using System;
        using System.Runtime.CompilerServices;

        [NonCopyable] public struct C
        {
            public readonly void M1(ref C value1, C value2, in C value3)
            {
                // No defensive copies
                rM();
                this.rM();

                value1.rM();
                value1.M();
                value2.rM();
                value2.M();
                value3.rM();
            }

            public readonly void rM() { }
            public void M() { }
        }
        """);

    [TestMethod]
    public Task Within_WithinAccessors() => Verify.DefensiveCopiesAsync(
        """
        using Singulink.CodeAnalysis;
        #pragma warning disable CS8656 // Call to non-readonly member from a 'readonly' member results in an implicit copy.
        using System;

        [NonCopyable] public struct C
        {
            // No defensive copies
            public int P1 => M2();
            public int P2
            {
                get => M2();
                set => M1();
            }
            public event Action E1
            {
                add => M1();
                remove => M1();
            }
            public int this[object o0] => M2();
            public int this[int i0]
            {
                get => M2();
                set => M1();
            }

            // All defensive copies
            public readonly int rP1 => {|#0:M2()|};
            public readonly int rP2
            {
                get => {|#1:M2()|};
                set => {|#2:M1()|};
            }
            public int rP2_
            {
                readonly get => {|#3:M2()|};
                set => M1();
            }
            public int rP2__
            {
                get => M2();
                readonly set => {|#4:M1()|};
            }
            public readonly event Action rE1
            {
                add => {|#5:M1()|};
                remove => {|#6:M1()|};
            }
            public readonly int this[object o0, object o1] => {|#7:M2()|};
            public readonly int this[int i0, int i1]
            {
                get => {|#8:M2()|};
                set => {|#9:M1()|};
            }
            public int this[int i0, int i1, int i2]
            {
                readonly get => {|#10:M2()|};
                set => M1();
            }
            public int this[int i0, int i1, int i2, int i3]
            {
                get => M2();
                readonly set => {|#11:M1()|};
            }

            // Helper methods
            public void M1() { }
            public int M2() => 0;
        }
        #pragma warning restore CS8656 // Call to non-readonly member from a 'readonly' member results in an implicit copy.
        """);
}
