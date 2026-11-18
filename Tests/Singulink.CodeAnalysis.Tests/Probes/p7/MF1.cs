using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
using static Sink;

public static class MF1
{
    static Guard s_g;
    static object s_o = new();

    // Exchange after a read: a second read of the same value
    public static (Task<Guard>, Task<Guard>) C01([MovedFrom] ref Task<Guard> t)   // C01
    {
        var m = t;
        var n = Interlocked.Exchange(ref t, null!);
        return (m, n);
    }

    public static Task<Guard> C02([MovedFrom] ref Task<Guard> t)   // C02 clean: exchange is the read and the assignment
        => Interlocked.Exchange(ref t, null!);

    // string concatenation calls ToString on an object: user code
    public static Guard C03([MovedFrom] ref Guard a, object o)   // C03
    {
        Guard m = a;
        string s = "x" + o;
        a = default;
        return m;
    }

    // swap with another reference: the other one is read, but not assigned
    public static void C04([MovedFrom] ref Guard a, ref Guard b)   // C04
    {
        Guard t = a;
        a = b;
        Held = t;
    }

    // a read of the value through a property of a [MovedFrom] container
    public static Guard C05([MovedFrom] ref Task<Guard> t)   // C05
    {
        var r = t.Result;
        t = null!;
        return r;
    }

    // tuple of the parameter and a call
    public static (Guard, int) C06([MovedFrom] ref Guard a)   // C06
    {
        var r = (a, Compute());
        a = default;
        return r;
    }
    static int Compute() => 1;

    // goto backward: a read in a loop made by goto
    public static Guard C07([MovedFrom] ref Guard a, int n)   // C07
    {
        Guard m = default;
    top:
        m = a;
        if (--n > 0) goto top;
        a = default;
        return m;
    }

    // read in try, assign in finally
    public static Guard C08([MovedFrom] ref Guard a)   // C08 clean?
    {
        Guard m;
        try { m = a; }
        finally { a = default; }
        return m;
    }

    // read in try, assign after try; catch swallows
    public static Guard C09([MovedFrom] ref Guard a)   // C09 reported? (nothing can throw)
    {
        Guard m = default;
        try { m = a; } catch { }
        a = default;
        return m;
    }

    // stored, then an implicit conversion that boxes a local int
    public static void C10([MovedFrom] ref Guard a, ref Guard b, int i)   // C10 clean
    {
        b = a;
        object o = i;
        a = default;
    }

    // stored into an array local element: bounds check
    public static void C11([MovedFrom] ref Guard a, Guard[] arr)   // C11
    {
        arr[5] = a;
        a = default;
    }

    // out argument from the parameter of a local function with the read
    public static Guard C12([MovedFrom] ref Guard a)   // C12
    {
        Guard m = a;
        Reset(out a);
        return m;
        static void Reset(out Guard g) => g = default;
    }

    // is-pattern that declares
    public static Guard C13([MovedFrom] ref Guard a)   // C13
    {
        if (a is var m) { a = default; return m; }
        return default;
    }

    // switch on the parameter with a when clause that calls
    public static Guard C14([MovedFrom] ref Guard a)   // C14
    {
        Guard r = a switch { var m when Compute() > 0 => m, var m2 => m2 };
        a = default;
        return r;
    }

    // ref local alias reassigned in a branch
    public static Guard C15([MovedFrom] ref Guard a, ref Guard b, bool c)   // C15
    {
        ref Guard r = ref a;
        if (c) r = ref b;
        Guard m = a;
        r = default;
        return m;
    }

    // ref local alias of the parameter: read through it twice
    public static (Guard, Guard) C16([MovedFrom] ref Guard a)   // C16
    {
        ref Guard r = ref a;
        Guard m = a;
        Guard n = r;
        r = default;
        return (m, n);
    }

    // conditional reads across branches
    public static Guard C17([MovedFrom] ref Guard a, bool c)   // C17
    {
        Guard m = default;
        if (c) m = a;
        if (!c) m = a;
        a = default;
        return m;
    }

    // deconstruct into the parameter
    public static Guard C18([MovedFrom] ref Guard a)   // C18 clean
    {
        Guard m = a;
        (a, int x) = (default(Guard), 1);
        return m;
    }

    // ??= on a container parameter after a read
    public static Task<Guard> C19([MovedFrom] ref Task<Guard>? t)   // C19
    {
        var m = t;
        t ??= null!;
        return m!;
    }

    // a read of the parameter's field (container inside a value)
    public static Guard C20([MovedFrom] ref Guard a)   // C20 clean
    {
        Guard m = a;
        a = default;
        return m;
    }

    // a lock between the read and the assign
    public static Guard C21([MovedFrom] ref Guard a)   // C21
    {
        Guard m = a;
        lock (s_o) { a = default; }
        return m;
    }

    // Move of a local between read and assign
    public static Guard C22([MovedFrom] ref Guard a)   // C22 clean
    {
        Guard m = a;
        Guard n = NonCopyable.Move(ref m);
        a = default;
        return n;
    }

    // an aliasing [MovedFrom] call: Move(ref a) after a read
    public static (Guard, Guard) C23([MovedFrom] ref Guard a)   // C23
    {
        Guard m = a;
        Guard n = NonCopyable.Move(ref a);
        return (m, n);
    }
}

public static class RTMF1
{
    public static Lock C01()
    {
        var l = new Lock();
        Task<Guard> t = l.EnterTaskAsync();
        var (x, y) = MF1.C01(ref t);
        x.Result.Dispose(); y.Result.Dispose();
        return l;
    }
}
