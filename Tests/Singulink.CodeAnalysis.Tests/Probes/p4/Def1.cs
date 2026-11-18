using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct DG
{
    private int _s;
    public int Prop => _s++;              // not readonly
    public readonly int RoProp => _s;
    public void M() => _s++;
    public event Action? Ev { add => _s++; remove => _s--; }
    public ref int GetPinnableReference() => ref _s;
}

class Def1
{
    private readonly DG _ro;
    private static readonly DG s_ro;
    private DG _rw;

    static ref readonly DG GetRO(Def1 d) => ref d._rw;

    public Def1(Def1 other)
    {
        _ro.M();                           // S01 OK: own ctor, variable
        other._ro.M();                     // S02 SCA1006 expected: another instance's readonly field
        s_ro.M();                          // S03 SCA1006 expected: static readonly in an instance ctor
        Action a = () => _ro.M();          // S04 SCA1006 expected: lambda in the ctor
        void Local() => _ro.M();           // S05 SCA1006 expected: local function in the ctor
        Local();
        _ = _ro.Prop;                      // S06 OK in ctor
    }

    static Def1() { s_ro.M(); }            // S07 OK: static ctor

    void Use()
    {
        if (_ro is { Prop: 1 }) { }        // S08 SCA1006 expected: property pattern, non-readonly getter
        if (_ro is { RoProp: 1 }) { }      // S09 OK
        _ro.Ev += () => { };               // S10 SCA1006 expected: event accessor
        GetRO(this).M();                   // S11 SCA1006 expected: ref readonly return
        _ = GetRO(this).Prop;              // S12 SCA1006 expected
        Action d = _ro.M;                  // S13 copy into a delegate: SCA1003
        var x = _ro switch { { Prop: 1 } => 1, _ => 0 };  // S14 SCA1006 expected
    }

    unsafe void Pin()
    {
        fixed (int* p = _ro) { }           // S15 SCA1006 expected (GetPinnableReference on a copy)
        fixed (int* p = _rw) { }           // S16 OK
    }
}
