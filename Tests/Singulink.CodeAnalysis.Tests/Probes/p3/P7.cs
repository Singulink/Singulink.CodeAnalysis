using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

interface IRel7 { void Release() { } int P => 0; event Action E { add { } remove { } } int this[int i] => 0; }
interface IRel7b { sealed void Fixed() { } }
interface IRel7c { void Release(); }
interface IRel7d : IRel7c { void IRel7c.Release() { } }

[NonCopyable] struct D1 : IRel7 { }                                                 // H01 SCA1013
[NonCopyable] struct D2 : IRel7b { }                                                // H02 SCA1013
[NonCopyable] struct D3 : IRel7d { }                                                // H03 SCA1013 (derived interface gives the default)
[NonCopyable] struct D4 : IRel7 { public void Release() { } public int P => 1; public event Action E { add { } remove { } } public int this[int i] => 0; }  // H04 fine
[NonCopyable] struct D5<[NonCopyable] T> : IRel7c { T _v; public void Release() { } public async Task A() { await Task.Yield(); } }   // H05 SCA1014
struct D6<T> : IRel7 { T _v; }                                                      // H06 (D6<Guard> through a marked T: rare)

[NonCopyable] struct S7
{
    private Guard _g;
    public IEnumerable<int> Items { get { yield return 1; } }                       // H10 SCA1014
    public async ValueTask DisposeAsync() { await Task.Yield(); }                   // H11 SCA1014
    public readonly void M1() => Touch();                                           // H12 copy of this
    public void Touch() => _g.Touch();
    public static async Task S() { await Task.Yield(); }                            // H13 fine
    public IEnumerator<int> GetEnumerator() { yield return 1; }                     // H14 SCA1014
    public readonly void M2() { ref readonly Guard r = ref _g; r.Touch(); }         // H15 copy through ref readonly
    public Task Local() { return L(); async Task L() { await Task.Yield(); } }      // H16 local function (does not use this)
}

static class It7
{
    public static IEnumerable<int> I1(Guard g) { yield break; }                     // H20 SCA1012
    public static async IAsyncEnumerable<int> I2(Guard g) { await Task.Yield(); yield break; }   // H21 SCA1012
    public static IEnumerator<int> I3(Guard g) { yield break; }                     // H22 fine
    public static IEnumerable<int> I4<[NonCopyable] T>(T g) { yield break; }        // H23 SCA1012
    public static IEnumerable<int> I5(Guard? g) { yield break; }                    // H24
    public static IEnumerable<int> I6((Guard, int) g) { yield break; }              // H25
    public static IEnumerable<int> I7(ValueTask<Guard> g) { yield break; }          // H26
    public static IEnumerable I8(Guard g) { yield break; }                          // H27
    public static IEnumerable<int> I9() { return L(Owner.Enter()); static IEnumerable<int> L(Guard g) { yield break; } }   // H28
    public static IEnumerable<int> I10(ArraySegment<Guard> s) { yield break; }      // H29 fine (view)
    public static IEnumerable<int> I11(KeyValuePair<int, Guard> s) { yield break; } // H30
}

class B7
{
    private GCHandle _h;
    private GCHandle _h2;
    private AsyncFlowControl _afc;
    private PinnedGCHandle<byte[]> _p;

    unsafe void M()
    {
        IntPtr a = GCHandle.ToIntPtr(_h);                                           // H40 fine (borrows)
        bool b = _h == _h2;                                                         // H41 fine
        IntPtr c = (IntPtr)_h;                                                      // H42 fine
        bool d = _h.Equals(_h2);                                                    // H43 fine
        byte* e = _p.GetAddressOfArrayData();                                       // H44 fine
        bool f = _afc == _afc;                                                      // H45 fine
        bool g = _h.Equals((object)_h2);                                            // H46 SCA1004
        GCHandle h2 = GCHandle.FromIntPtr(GCHandle.ToIntPtr(_h));                   // H47 a second handle (design)
        var mh = new System.Buffers.MemoryHandle(null, _h, null);                   // H48 SCA1003
        bool j = _h != default;                                                     // H50
        bool k = GCHandle.ToIntPtr(_h) == IntPtr.Zero;                              // H51
        GCHandle? n = _h2;                                                          // H52 SCA1001-ish
    }
}

class P7
{
    void M(D6<Guard> d) { }
}
