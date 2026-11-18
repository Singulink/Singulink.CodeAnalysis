using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of paths that only unusual code reaches, so that the checks which guard them are not taken for dead.
/// </summary>
[PrefixTestClass]
public class EdgeCaseTests
{
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
