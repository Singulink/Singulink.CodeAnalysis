namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// The IDs of the diagnostics that the analyzers report.
/// </summary>
public static class DiagnosticIds
{
    /// <summary>
    /// A non-copyable value is copied by assigning it from a variable, a field or a reference.
    /// </summary>
    public const string CopyByAssignment = "SCA1001";

    /// <summary>
    /// A non-copyable value is copied by returning a field or a reference by value.
    /// </summary>
    public const string CopyByReturn = "SCA1002";

    /// <summary>
    /// A non-copyable value is passed by value, captured, or used in another way that is not known to be safe.
    /// </summary>
    public const string UnsupportedUse = "SCA1003";

    /// <summary>
    /// A stored non-copyable value is boxed or converted to <see cref="object"/> or an interface.
    /// </summary>
    public const string Boxing = "SCA1004";

    /// <summary>
    /// <c>MemberwiseClone</c> is called on an object or a struct that holds a non-copyable value.
    /// </summary>
    public const string MemberwiseCloneCall = "SCA1005";

    /// <summary>
    /// A member is used through a read-only reference to a non-copyable value, so the compiler runs it on a defensive copy.
    /// </summary>
    public const string MemberOnReadOnlyReference = "SCA1006";

    /// <summary>
    /// <c>RuntimeHelpers.GetObjectValue</c> is used, which clones a boxed struct.
    /// </summary>
    public const string GetObjectValue = "SCA1007";

    /// <summary>
    /// <c>MemberwiseClone</c> is called on an object of a class that can be derived from in another assembly, where a derived class
    /// can hold a non-copyable value.
    /// </summary>
    public const string MemberwiseCloneOfOpenClass = "SCA1008";

    /// <summary>
    /// A copyable struct or a record has a field of a non-copyable type.
    /// </summary>
    public const string FieldOfCopyableType = "SCA1010";

    /// <summary>
    /// An auto-property has a non-copyable type.
    /// </summary>
    public const string AutoProperty = "SCA1011";

    /// <summary>
    /// An iterator that returns a sequence has a by-value parameter of a non-copyable type.
    /// </summary>
    public const string IteratorParameter = "SCA1012";

    /// <summary>
    /// A struct of a non-copyable type leaves an interface member to the default implementation of the interface.
    /// </summary>
    public const string DefaultInterfaceMember = "SCA1013";

    /// <summary>
    /// A non-copyable struct has an <see langword="async"/> or iterator instance member, which runs on a copy of the struct.
    /// </summary>
    public const string AsyncOrIteratorMember = "SCA1014";

    /// <summary>
    /// An <c>await</c> whose awaiter is of a non-copyable type.
    /// </summary>
    public const string NonCopyableAwaiter = "SCA1015";

    /// <summary>
    /// A member of a generic whose type parameter is not marked is used with a non-copyable type, and the member is not on the safe list.
    /// </summary>
    public const string UnlistedMember = "SCA1020";

    /// <summary>
    /// A member that requires a copyable type is used with a non-copyable type.
    /// </summary>
    public const string RequiresCopyable = "SCA1021";

    /// <summary>
    /// A container of non-copyable values is used as an untyped collection.
    /// </summary>
    public const string UntypedCollection = "SCA1022";

    /// <summary>
    /// A container of non-copyable values is used as a type that hides them from the analyzer.
    /// </summary>
    public const string OpaqueType = "SCA1023";

    /// <summary>
    /// A member of an assembly that was not built with the analyzer gets at a non-copyable value other than by value, and the list does
    /// not mark it 'borrows'.
    /// </summary>
    public const string UncheckedMember = "SCA1024";

    /// <summary>
    /// An assembly that the compilation references was not built with the analyzer, and references an assembly that declares a
    /// non-copyable type, and no list vouches for it.
    /// </summary>
    public const string UncheckedAssembly = "SCA1025";

    /// <summary>
    /// A non-copyable value is read by value from a stored container.
    /// </summary>
    public const string ReadFromStoredContainer = "SCA1030";

    /// <summary>
    /// A method returns a container that it does not own, and its return value is not marked <c>[Borrowed]</c>.
    /// </summary>
    public const string UnownedContainerReturned = "SCA1031";

    /// <summary>
    /// A method keeps a container parameter that is not marked <c>[Kept]</c>.
    /// </summary>
    public const string KeptParameter = "SCA1032";

    /// <summary>
    /// A container is moved out of something that is not a local which the method owns: it is passed to a <c>[MovedFrom]</c> parameter,
    /// as that of <c>NonCopyable.Move</c>, out of a field, an element, a parameter that is not <c>[MovedFrom]</c> itself, or a local that
    /// something else may hold.
    /// </summary>
    public const string ContainerNotOwned = "SCA1033";

    /// <summary>
    /// A <c>[NonCopyable]</c> or <c>[RequiresCopyable]</c> mark is not kept by a derived type, an override or an implementation, a
    /// <c>[Borrowed]</c> or <c>[Kept]</c> mark is added where the member that is replaced, or the delegate that a method is converted to,
    /// does not have it, or a <c>[MovedFrom]</c> mark is added or dropped. Also a default member of an interface that a non-copyable
    /// struct does not implement.
    /// </summary>
    public const string MarkNotKept = "SCA1040";

    /// <summary>
    /// <c>[MovedFrom]</c> is used wrongly.
    /// </summary>
    public const string MovedFromMisuse = "SCA1041";

    /// <summary>
    /// A line of a safe list file cannot be parsed.
    /// </summary>
    public const string SafeListLine = "SCA1042";

    /// <summary>
    /// A type derives from an erasable type, or implements one, and does not stay erasable.
    /// </summary>
    public const string ErasableBase = "SCA1043";

    /// <summary>
    /// An attribute of the analyzer is declared <c>[Conditional]</c>.
    /// </summary>
    public const string ConditionalMarker = "SCA1044";

    /// <summary>
    /// A class named <c>NonCopyableAttribute</c> in a namespace other than <c>Singulink.CodeAnalysis</c>, which is not recognised.
    /// </summary>
    public const string ForeignNonCopyableAttribute = "SCA1045";

    /// <summary>
    /// <c>[NotNullRef]</c> stands where it does not count: on a by-value parameter, on a parameter of anything but a method that is
    /// not virtual or a local function, or on a parameter of a method that the compiler can leave out of a build.
    /// </summary>
    public const string NotNullRefMisplaced = "SCA1046";

    /// <summary>
    /// <c>[NotNullRef]</c> on a parameter that the project declares: the analyzer does not check what the mark says.
    /// </summary>
    public const string NotNullRefNotChecked = "SCA1047";

    /// <summary>
    /// A member that is nested too deeply for the analyzer, which was not checked.
    /// </summary>
    public const string NestedTooDeeply = "SCA1048";
}
