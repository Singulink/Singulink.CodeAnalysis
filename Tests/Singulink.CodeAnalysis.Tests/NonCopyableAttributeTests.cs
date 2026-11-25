using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of <see cref="NonCopyableAttribute"/> itself: reading <see cref="NonCopyableAttribute.UnsafeToCopy"/> where it was not set throws,
/// since on a type parameter not setting it differs from setting it to false, and <see cref="NonCopyableAttribute.GetUnsafeToCopy"/> tells
/// the three apart.
/// </summary>
[PrefixTestClass]
public class NonCopyableAttributeTests
{
    [TestMethod]
    public void UnsafeToCopy_ThrowsWhereItWasNotSet()
    {
        var attribute = new NonCopyableAttribute();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => attribute.UnsafeToCopy);
        StringAssert.Contains(exception.Message, "GetUnsafeToCopy()");
        Assert.IsNull(attribute.GetUnsafeToCopy());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void UnsafeToCopy_ReadsWhatWasSet(bool value)
    {
        var attribute = new NonCopyableAttribute { UnsafeToCopy = value };

        Assert.AreEqual(value, attribute.UnsafeToCopy);
        Assert.AreEqual(value, attribute.GetUnsafeToCopy());
    }
}
