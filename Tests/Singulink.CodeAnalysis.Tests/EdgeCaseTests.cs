using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of paths that only unusual code reaches, so that the checks which guard them are not taken for dead.
/// </summary>
[PrefixTestClass]
public class EdgeCaseTests
{
    // Code with compile errors, as an editor shows it while someone types: an operation that the compiler marked invalid, or whose type is
    // an error type, cannot run, and gets no report as a use that the analyzer has no rule for. The reports about valid code in the same
    // member stay.
    [TestMethod]
    public Task InvalidCode_GetsNoReportAsAnUnknownUse() => new Verify.Test {
        TestCode = """
            using System.Collections.Generic;
            using Missing.Namespace;

            class C
            {
                Guard _guard;
                List<Guard> _list = new();

                void M()
                {
                    var copy = {|#0:_guard|};
                    var a = Missing.Call(_guard);
                    var b = new MissingGeneric<Guard>(_guard);
                    MissingType m = _guard;
                    object o = m;
                    var t = (_guard, missingLocal);
                    _guard.Nope();
                    var element = {|#1:_list[0]|};
                    if (_guard. ) { }
                    var z = _list.Missing().First();
                    Take(_guard, );
                    copy.Dispose();
                    element.Dispose();
                }

                static void Take(Guard guard, int count) { }
            }
            """ + System.Environment.NewLine + Sources.Guard,
        CompilerDiagnostics = Microsoft.CodeAnalysis.Testing.CompilerDiagnostics.None,
        ExpectedDiagnostics = {
            Verify.CopyByAssignment(0, "Guard"),
            Verify.ReadFromStoredContainer(1, "'List<T>.this[int]'", "Guard"),
        },
    }.RunAsync();

    // A null constant that is converted to a nullable of a non-copyable type through object is no value that is taken out of a box.
    [TestMethod]
    public Task Unboxing_ANullConstantThroughObjectIsNoCopy() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            void M(object? boxed)
            {
                Guard? none = (Guard?)(object?)null;
                Guard? alsoNone = (Guard?)(System.IDisposable?)null;

                // A box in a variable can hold a guard.
                Guard? copy = {|#0:(Guard?)boxed|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignmentOutOfBox(0, "Guard?", "object"));

    // An anonymous method with no parameter list gets the parameters of the delegate from the compiler, with names of its own and
    // with no attributes: it is compared like a lambda that leaves the marks out.
    [TestMethod]
    public Task Delegate_AnAnonymousMethodWithNoParameterListIsConvertedToADelegateWithParameters() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        delegate void Taker(Guard guard);

        delegate void Mover([MovedFrom] ref Guard guard);

        delegate void Keeper([Kept] System.Collections.Generic.List<Guard> guards);

        class C
        {
            void M()
            {
                Taker taker = delegate { };
                Mover mover = {|#0:delegate { }|};
                Keeper keeper = delegate { };
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromDroppedWithNoParameterList(0, "delegate 'Mover'", "guard"));
}
