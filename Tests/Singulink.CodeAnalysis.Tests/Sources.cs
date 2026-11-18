namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Source code that the tests share.
/// </summary>
internal static class Sources
{
    /// <summary>
    /// The non-copyable struct of the examples, and a lock that hands it out in every way the examples use.
    /// </summary>
    public const string Guard = """
        [Singulink.CodeAnalysis.NonCopyable]
        public struct Guard : System.IDisposable
        {
            public int Value;
            public void Dispose() { }
        }

        public sealed class RwLock
        {
            public Guard Enter() => default;
            public Guard? TryEnter() => default;
            public (Guard guard, bool ok) TryEnterTuple() => default;
            public System.Threading.Tasks.ValueTask<Guard> EnterAsync() => default;
            public System.Threading.Tasks.Task<Guard> EnterTaskAsync() => null!;
            public System.Collections.Generic.List<Guard> CreateGuards() => new();
        }
        """;
}
