using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lib
{
    // The library's own marks, kept out of its metadata as marker attributes often are.
    [Conditional("CODE_ANALYSIS_MARKS"), AttributeUsage(AttributeTargets.Struct | AttributeTargets.GenericParameter)]
    internal sealed class NonCopyableAttribute : Attribute;

    [Conditional("CODE_ANALYSIS_MARKS"), AttributeUsage(AttributeTargets.ReturnValue)]
    internal sealed class BorrowedAttribute : Attribute;

    public sealed class Lock
    {
        public int Acquires, Releases;
        public Guard Enter() { Acquires++; return new Guard(this); }
        public override string ToString() => $"acquires={Acquires} releases={Releases}";
    }

    [NonCopyable]
    public struct Guard : IDisposable
    {
        private Lock? _lock;
        public Guard(Lock l) => _lock = l;
        public void Dispose() { if (_lock is { } l) { _lock = null; l.Releases++; } }
    }

    public sealed class Holder
    {
        private readonly List<Guard> _guards = new();
        public void Add(Lock l) => _guards.Add(l.Enter());
        [return: Borrowed]
        public List<Guard> Guards() => _guards;
        public void ReleaseAll() { for (int i = 0; i < _guards.Count; i++) System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_guards)[i].Dispose(); }
    }

}
