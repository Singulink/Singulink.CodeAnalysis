namespace FirstHour;

public sealed class Knobs
{
    private SpinLock _spin = new(false);
    private readonly SpinLock _roSpin = new(false);

    public void CopySpin()
    {
        SpinLock copy = _spin;          // K01 SCA1001 with built-ins, nothing with property=false
        bool taken = false;
        copy.Enter(ref taken);
    }

    public void ReadonlySpin()
    {
        bool taken = false;
        _roSpin.Enter(ref taken);       // K02 SCA1006 with built-ins
    }

    public void Suppressed(System.Collections.Generic.List<LockGuard> list)
    {
#pragma warning disable SCA1030
        var g = list[0];                // K03 suppressed
#pragma warning restore SCA1030
        g.Dispose();
    }
}
