using System;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct G3
{
    private int _n;
    public void Touch() => _n++;
    public object CloneBoxed() => MemberwiseClone();        // E01 clone of this
}

internal class Probe2
{
    private Guard _field;

    private void Into(bool flag, Guard stored, in Guard byIn, Guard[] guards)
    {
        Guard local = Owner.Enter();
        object b01 = flag ? Owner.Enter() : stored;         // B01 one arm stored
        object b02 = NonCopyable.Move(ref local);           // B02 allowed: moved
        object b03 = local;                                 // B03
        object b04 = byIn;                                  // B04
        object b05 = guards[0];                             // B05
        object b06 = _field;                                // B06
        Func<object> f1 = () => Owner.Enter();              // B07 allowed
        Func<object> f2 = () => stored;                     // B08
        object[] a1 = { Owner.Enter() };                    // B09 allowed
        object[] a2 = new object[] { stored };              // B10
        bool same = ReferenceEquals(stored, stored);        // B11
        IDisposable b12 = Owner.Enter();                    // B12 allowed
        (b12 as IDisposable)?.Dispose();                    // B13 in place
        object b14 = flag ? Owner.Enter() : Owner.Enter();  // B14 allowed
        object b15 = (object)Owner.Enter() ?? stored;       // B15
        ValueType b16 = Owner.Enter();                      // B16 allowed
        Enum? b17 = null;
        IComparable? b18 = null;
        object b19 = default(Guard);                        // B19 allowed
        object b20 = new Guard();                           // B20 allowed
        object b21 = (Guard?)Owner.Enter();                 // B21 allowed
        object b22 = (Guard?)stored;                        // B22
    }
}
