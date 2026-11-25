using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the framework structs that the built-in list names as non-copyable, and of the property that leaves them out.
/// </summary>
/// <remarks>
/// Docs: articles/rules/non-copyable-types.md.
/// </remarks>
[PrefixTestClass]
public class BuiltInTypeTests
{
    private const string Copies = """
        using System;
        using System.Buffers;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using System.Threading;
        using System.Threading.Tasks.Sources;

        class C
        {
            SpinLock _spinLock;
            GCHandle _handle;
            GCHandle<string> _typedHandle;
            PinnedGCHandle<byte[]> _pinnedHandle;
            WeakGCHandle<string> _weakHandle;
            MemoryHandle _memoryHandle;
            AsyncFlowControl _flowControl;
            ManualResetValueTaskSourceCore<int> _core;

            void M(Lock lockObject, ref DefaultInterpolatedStringHandler handler)
            {
                var spinLock = {|#0:_spinLock|};
                var handle = {|#1:_handle|};
                var typedHandle = {|#2:_typedHandle|};
                var pinnedHandle = {|#3:_pinnedHandle|};
                var weakHandle = {|#4:_weakHandle|};
                var memoryHandle = {|#5:_memoryHandle|};
                var flowControl = {|#6:_flowControl|};
                var core = {|#7:_core|};
                var handlerCopy = {|#8:handler|};

                var scope = lockObject.EnterScope();
                var scopeCopy = {|#9:scope|};
                scope.Dispose();
            }
        }

        // Its own struct is reported whatever the property says, and so is one that a list of the project names.
        class D
        {
            Own _own;
            Listed _listed;

            void M()
            {
                var own = {|#10:_own|};
                var listed = {|#11:_listed|};
            }
        }

        [Singulink.CodeAnalysis.NonCopyable]
        struct Own { }

        struct Listed { }
        """;

    private const string UserList = "T:Listed;noncopyable";

    // The harness sets the property to 'true', as the targets file of the package does in a project that references a package itself.
    [TestMethod]
    public Task BuiltInNonCopyableTypes_AreReportedWhereThePropertyIsTrue() => Verify.AnalyzerAsync(
        Copies,
        UserList,
        Verify.CopyByAssignment(0, "System.Threading.SpinLock"),
        Verify.CopyByAssignment(1, "System.Runtime.InteropServices.GCHandle"),
        Verify.CopyByAssignment(2, "System.Runtime.InteropServices.GCHandle<string>"),
        Verify.CopyByAssignment(3, "System.Runtime.InteropServices.PinnedGCHandle<byte[]>"),
        Verify.CopyByAssignment(4, "System.Runtime.InteropServices.WeakGCHandle<string>"),
        Verify.CopyByAssignment(5, "System.Buffers.MemoryHandle"),
        Verify.CopyByAssignment(6, "System.Threading.AsyncFlowControl"),
        Verify.CopyByAssignment(7, "System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore<int>"),
        Verify.CopyByAssignment(8, "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler"),
        Verify.CopyByAssignment(9, "System.Threading.Lock.Scope"),
        Verify.CopyByAssignment(10, "Own"),
        Verify.CopyByAssignment(11, "Listed"));

    // Under the updated rules the five types whose copy can break memory safety are errors: the four GC handles, whose Free or Dispose
    // releases a slot that may belong to another object by then, and MemoryHandle, whose Dispose frees and unpins. The other six are
    // the ordinary warning, DispatcherProcessingDisabled among them (below), and so are the project's own plain types.
    [TestMethod]
    public Task BuiltInNonCopyableTypes_FiveAreUnsafeToCopyUnderTheUpdatedRules() => Verify.UpdatedRulesAsync(
        Copies,
        UserList,
        Verify.CopyByAssignment(0, "System.Threading.SpinLock"),
        Verify.CopyByAssignment(1, "System.Runtime.InteropServices.GCHandle").AsError(),
        Verify.CopyByAssignment(2, "System.Runtime.InteropServices.GCHandle<string>").AsError(),
        Verify.CopyByAssignment(3, "System.Runtime.InteropServices.PinnedGCHandle<byte[]>").AsError(),
        Verify.CopyByAssignment(4, "System.Runtime.InteropServices.WeakGCHandle<string>").AsError(),
        Verify.CopyByAssignment(5, "System.Buffers.MemoryHandle").AsError(),
        Verify.CopyByAssignment(6, "System.Threading.AsyncFlowControl"),
        Verify.CopyByAssignment(7, "System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore<int>"),
        Verify.CopyByAssignment(8, "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler"),
        Verify.CopyByAssignment(9, "System.Threading.Lock.Scope"),
        Verify.CopyByAssignment(10, "Own"),
        Verify.CopyByAssignment(11, "Listed"));

    // A build in which the targets file of the package did not run has no value, and errs to off; so does any value but 'true'.
    [TestMethod]
    [DataRow("false")]
    [DataRow("False")]
    [DataRow(" FALSE ")]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("no")]
    [DataRow("1")]
    public Task BuiltInNonCopyableTypes_AreLeftOutUnlessThePropertyIsTrue(string? value)
    {
        var test = CreateTest(Copies, value);
        test.TestState.AdditionalFiles.Add((Verify.ListFileName, UserList));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(10, "Own"));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(11, "Listed"));
        return test.RunAsync();
    }

    [TestMethod]
    [DataRow("true")]
    [DataRow("True")]
    [DataRow(" TRUE ")]
    public Task BuiltInNonCopyableTypes_OnlyTrueTurnsThemOn(string value)
    {
        var test = CreateTest(
            """
            class C
            {
                System.Threading.SpinLock _spinLock;

                void M()
                {
                    var spinLock = {|#0:_spinLock|};
                }
            }
            """,
            value);

        test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(0, "System.Threading.SpinLock"));
        return test.RunAsync();
    }

    [TestMethod]
    public Task BuiltInNonCopyableTypes_TheRestOfTheBuiltInListStillApplies()
    {
        // Without the built-in types the other entries are as they were, and a project gets a type back by naming it in its own list.
        var test = CreateTest(
            $$"""
            using System.Collections.Generic;
            using System.Threading;

            class C
            {
                SpinLock _spinLock;
                System.Runtime.InteropServices.GCHandle _handle;

                void M(RwLock rwLock, List<Guard> list, Stack<Guard> stack)
                {
                    var spinLock = {|#0:_spinLock|};
                    var handle = _handle;

                    list.Add(rwLock.Enter());
                    Guard top = stack.Pop();
                    Guard first = {|#1:list[0]|};
                    Guard[] all = {|#2:list.ToArray()|};
                }
            }

            {{Sources.Guard}}
            """,
            "false");

        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "T:System.Threading.SpinLock;noncopyable"));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(0, "System.Threading.SpinLock"));
        test.ExpectedDiagnostics.Add(Verify.ReadFromStoredContainer(1, "'List<T>.this[int]'", "Guard"));
        test.ExpectedDiagnostics.Add(Verify.UnlistedMember(2, "List<T>.ToArray()", "Guard", "T", "M:System.Collections.Generic.List`1.ToArray"));
        return test.RunAsync();
    }

    [TestMethod]
    public Task BuiltInNonCopyableTypes_ReportNothingOnNormalUse() => Verify.AnalyzerAsync("""
        using System;
        using System.Buffers;
        using System.Globalization;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using System.Threading;
        using System.Threading.Tasks;
        using System.Threading.Tasks.Sources;

        class C
        {
            readonly Lock _lock = new();
            SpinLock _spinLock = new(enableThreadOwnerTracking: false);
            GCHandle _handle;

            // The compiler builds an ordinary interpolated string with a DefaultInterpolatedStringHandler.
            string Interpolated(int number, string text, object value) => $"Number {number}, text {text,10}, value {value:X}";

            string InterpolatedWithProvider(int number) => string.Create(CultureInfo.InvariantCulture, $"Number {number}");

            string Handler(int number)
            {
                var handler = new DefaultInterpolatedStringHandler(8, 1);
                handler.AppendLiteral("Number ");
                handler.AppendFormatted(number);
                return handler.ToStringAndClear();
            }

            // The compiler enters and leaves a Lock with a Lock.Scope.
            void Locked()
            {
                lock (_lock)
                {
                }

                using (_lock.EnterScope())
                {
                }

                using var scope = _lock.EnterScope();
            }

            void Spin()
            {
                bool taken = false;

                try
                {
                    _spinLock.Enter(ref taken);
                }
                finally
                {
                    if (taken)
                        _spinLock.Exit();
                }
            }

            unsafe void Pin(Memory<byte> memory, byte[] array)
            {
                using var handle = memory.Pin();
                void* pointer = handle.Pointer;

                using (MemoryHandle second = memory.Pin())
                {
                }

                _handle = GCHandle.Alloc(array, GCHandleType.Pinned);
                IntPtr address = _handle.AddrOfPinnedObject();
                _handle.Free();

                using var typed = new PinnedGCHandle<byte[]>(array);
                byte[] target = typed.Target;
            }

            async Task Flow()
            {
                using (ExecutionContext.SuppressFlow())
                {
                    _ = Task.Run(() => { });
                }

                AsyncFlowControl control = ExecutionContext.SuppressFlow();
                control.Undo();
                await Task.Yield();
            }
        }

        // The usual value-task source, with the core in a field and its members called in place.
        sealed class Source : IValueTaskSource<bool>
        {
            private ManualResetValueTaskSourceCore<bool> _core = new() { RunContinuationsAsynchronously = true };

            public ValueTask<bool> WaitAsync() => new(this, _core.Version);

            public void Complete() => _core.SetResult(true);

            public void Fail(Exception exception) => _core.SetException(exception);

            public bool GetResult(short token)
            {
                try
                {
                    return _core.GetResult(token);
                }
                finally
                {
                    _core.Reset();
                }
            }

            public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

            public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
                _core.OnCompleted(continuation, state, token, flags);
        }
        """);

    // DispatcherProcessingDisabled of WindowsBase: Dispatcher.DisableProcessing adds 1 to a count and returns it, and its Dispose checks
    // only the struct's own field before it subtracts 1, so the Dispose of a copy subtracts a second time. A copy of a stored value is
    // reported, its operators may be given stored values, and the value that DisableProcessing returns is new. With the built-in types
    // off, nothing is reported.
    private const string DispatcherProcessing = """
        using System.Windows.Threading;

        class C
        {
            DispatcherProcessingDisabled _disabled;
            DispatcherProcessingDisabled _other;

            void Copy() => _other = {|#0:_disabled|};

            bool Compare() => _disabled == _other || _disabled != _other;

            void Disable(Dispatcher dispatcher)
            {
                using (dispatcher.DisableProcessing())
                {
                }

                DispatcherProcessingDisabled disabled = dispatcher.DisableProcessing();
                _disabled = disabled;
            }
        }
        """;

    private static readonly ReferenceAssemblies DesktopReferenceAssemblies =
        Verify.Test.NetReferenceAssemblies.AddPackages([new PackageIdentity("Microsoft.WindowsDesktop.App.Ref", "10.0.0")]);

    [TestMethod]
    public async Task DispatcherProcessingDisabled_IsNonCopyable()
    {
        var test = new Verify.Test { TestCode = DispatcherProcessing, ReferenceAssemblies = DesktopReferenceAssemblies };
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(0, "System.Windows.Threading.DispatcherProcessingDisabled"));
        await test.RunAsync();
    }

    [TestMethod]
    public async Task DispatcherProcessingDisabled_IsPlainNonCopyableUnderTheUpdatedRules()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        var test = new Verify.Test {
            TestCode = DispatcherProcessing,
            ReferenceAssemblies = DesktopReferenceAssemblies,
            LanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
        };

        test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(0, "System.Windows.Threading.DispatcherProcessingDisabled"));
        await test.RunAsync();
    }

    [TestMethod]
    public Task DispatcherProcessingDisabled_IsCopyableWithTheBuiltInTypesOff() =>
        new Verify.Test { TestCode = DispatcherProcessing.Replace("{|#0:_disabled|}", "_disabled", StringComparison.Ordinal), ReferenceAssemblies = DesktopReferenceAssemblies }
            .WithBuildProperty("NonCopyableIncludeBuiltInTypes", "false")
            .RunAsync();

    private static Verify.Test CreateTest(string source, string? includeBuiltInTypes) =>
        new Verify.Test { TestCode = source }.WithBuildProperty("NonCopyableIncludeBuiltInTypes", includeBuiltInTypes);

    [TestMethod]
    public Task MembersThatTakeTheirTypeByValue_AreMarkedAsBorrowing() => Verify.AnalyzerAsync(
        """
        using System;
        using System.Buffers;
        using System.Runtime.InteropServices;
        using System.Threading;

        unsafe class C
        {
            GCHandle _handle;
            GCHandle<string> _typed;
            PinnedGCHandle<int[]> _pinned;
            PinnedGCHandle<string> _pinnedString;
            WeakGCHandle<string> _weak;
            AsyncFlowControl _flow;

            void M(GCHandle other, GCHandle<string> otherTyped, AsyncFlowControl otherFlow, void* pointer)
            {
                // These read the handle that they are given and keep nothing of it, so a stored one may be passed.
                nint a = GCHandle.ToIntPtr(_handle);
                bool b = _handle == other;
                bool c = _handle != other;
                nint d = (IntPtr)_handle;
                bool e = _handle.Equals(other);
                nint f = GCHandle<string>.ToIntPtr(_typed);
                bool g = _typed.Equals(otherTyped);
                int* h = _pinned.GetAddressOfArrayData();
                char* i = _pinnedString.GetAddressOfStringData();
                nint j = WeakGCHandle<string>.ToIntPtr(_weak);
                bool k = _flow.Equals(otherFlow);
                bool l = _flow == otherFlow;

                // The constructor of MemoryHandle keeps the handle, so it has no entry.
                var memory = new MemoryHandle(pointer, {|#0:_handle|});
            }
        }
        """,
        Verify.CopyOfStoredValue(0, "'MemoryHandle.MemoryHandle(void*, GCHandle, IPinnable?)' receives", "System.Runtime.InteropServices.GCHandle"));
}
