using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// The descriptors of the diagnostics that <see cref="DoNotCopyValueAnalyzer"/> reports. An ID can have more than one message.
/// </summary>
internal static class Rules
{
    private const string Category = "Reliability";

    // The article of each diagnostic on the documentation site is named by its ID in lower case. The address is fixed from the first release on.
    private const string HelpLinkBaseUri = "https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/diagnostics/";

    private const string CopyByAssignmentTitle = "Do not copy a non-copyable value by assignment";
    private const string CopyByReturnTitle = "Do not copy a non-copyable value by returning it";
    private const string MemberwiseCloneTitle = "Do not call MemberwiseClone on what holds a non-copyable value";
    private const string FieldOfCopyableTypeTitle = "Do not declare a field of a non-copyable type in a copyable struct or a record";
    private const string UnsupportedUseTitle = "Do not use a non-copyable value where it may be copied";
    private const string UntypedCollectionTitle = "Do not use a container of non-copyable values as an untyped collection";
    private const string OpaqueTypeTitle = "Do not use a container of non-copyable values as a type that hides them";
    private const string MarkNotKeptTitle =
        "Keep a [NonCopyable] or [RequiresCopyable] mark, do not add [Borrowed] or [Kept], and neither add nor drop [MovedFrom], against a base " +
        "member or a delegate";
    private const string ContainerNotOwnedTitle = "Do not hand over a container that the method does not own";
    private const string MovedFromTitle = "Use [MovedFrom] on a 'ref' parameter that the method resets";
    private const string MemberOnReadOnlyReferenceTitle = "Do not use a member that is not readonly through a read-only reference to a non-copyable value";
    private const string DefaultInterfaceMemberTitle = "Implement every interface member in a struct of a non-copyable type";
    private const string UnownedContainerReturnedTitle = "Do not return a container that the method does not own";
    private const string ReadFromStoredContainerTitle = "Do not read a non-copyable value by value from a stored container";
    private const string ConditionalMarkerTitle = "Do not declare an attribute of the analyzer as conditional";
    private const string ForeignNonCopyableAttributeTitle = "Declare NonCopyableAttribute in the namespace Singulink.CodeAnalysis";

    private const string CopyByAssignmentMessage =
        "Assigning a stored value of non-copyable type '{0}' copies it{1}. Assign a reference, assign a new value, or move the stored value " +
        "out first with NonCopyable.Move.";

    private const string CopyByAssignmentDescription =
        "Assigning a stored value copies it: a variable before its last use, a field, an element or what a reference points to. Assign a " +
        "reference instead (with 'ref'), a new value, or what NonCopyable.Move takes out of the stored value.";

    private const string CopyByAssignmentForDeconstructMessage =
        "'foreach' copies each element of non-copyable type '{0}' before '{1}' deconstructs it, so the method is given a copy. Make the " +
        "method 'readonly', or declare a 'ref' iteration variable and call the method on it by name.";

    private const string DeconstructionOfStoredValueMessage =
        "A deconstruction copies the stored value of non-copyable type '{0}' before '{1}' deconstructs it, a local included, so the method " +
        "runs on a copy and what it changes is lost. Make the method 'readonly', deconstruct a new value or what NonCopyable.Move takes " +
        "out of the stored value, or call the method by name.";

    private const string CopyByAssignmentIntoForEachVariableMessage =
        "Variable '{1}' is a copy of a stored value of non-copyable type '{0}': 'foreach' copies each element into its variable. Write " +
        "'foreach (ref var {1} in ...)' over a span of the elements, such as 'CollectionsMarshal.AsSpan(list)' or 'array.AsSpan()', or " +
        "take the elements out with a member that removes them.";

    private const string CopyByAssignmentIntoVariableDescription =
        "A variable that a pattern, a deconstruction or a 'foreach' declares holds a copy of what it reads from when that is a stored value: a " +
        "variable, a field, an element or a reference. Read from a new value, such as the result of a call, move the stored value out first with " +
        "NonCopyable.Move, or declare a 'ref' iteration variable.";

    private const string CopyByAssignmentOutOfBoxDescription =
        "A value of a non-copyable type in a box is in a heap object that others can hold, so taking it out makes a copy, whatever gives the " +
        "reference: a cast, 'as', a pattern that declares a variable or looks into the value, or an assignment from dynamic, also when the box " +
        "comes from a call. A bare type test copies nothing. Keep the value out of object, dynamic and interface variables, or use it in place " +
        "through an interface that it implements.";

    private const string CopyByReturnOfLivingVariableMessage =
        "Returning '{1}' copies a value of non-copyable type '{0}' that lives on after the return: a variable that a lambda or a local function " +
        "captured, a parameter of a primary constructor, a 'using' variable or a variable of an iterator. Return a moved value instead.";

    private const string CopyByReturnOfVariableUsedAfterMessage =
        "Returning '{1}' copies a value of non-copyable type '{0}' that is used after the return: a 'finally' block, or the end of a " +
        "'using' scope that holds a reference to the variable, runs after the value was read. Return after the 'try' statement, or " +
        "return a moved value.";

    private const string ValuePassedTwiceMessage =
        "A value of non-copyable type '{0}' is passed twice here, to {1}, and each gets a copy of its own. Pass it by reference, or do " +
        "the two steps one after the other.";

    private const string DelegatesCombinedMessage =
        "Combining delegates of type '{0}' copies a non-copyable value: every target of a delegate with several targets gets a copy of " +
        "its own of the argument of type '{1}', which the delegate takes by value. Call the targets one by one, or pass the value by " +
        "reference.";

    private const string CopyByReturnOfLivingVariableDescription =
        "A local or a by-value parameter can be returned by value because it ends with the method. One that a lambda or a local function " +
        "captures, a parameter of a primary constructor that a member reads, a 'using' variable and a variable that an iterator yields do not: " +
        "they are fields in fact, or are disposed, or are read again. Return NonCopyable.Move(ref variable), or a new value.";

    private const string CopyByReturnDescription =
        "Returning the value that a reference points to copies it. Return the reference instead (with 'ref'), or a value that is not read from a " +
        "reference, such as a local or a new instance.";

    private const string UnsupportedUseMessage =
        "The analyzer does not know that this use of a value of non-copyable type '{0}' makes no copy (a '{1}' operation). Use the value in " +
        "place or by reference, or move it out first with NonCopyable.Move.";

    private const string UnsupportedUseDescription =
        "A value of a non-copyable type can only be used where it is known not to be copied. This use may copy it, or it is one the analyzer does " +
        "not know to be safe.";

    private const string SwitchStatementMatchesCopyMessage =
        "The pattern reads from a copy of the value of non-copyable type '{0}': a 'switch' statement matches a copy of what it switches " +
        "on, also where that is a local or a parameter. An 'is' pattern and a 'switch' expression without 'when' match it in place: use " +
        "one of those, or move the value into the 'switch' with NonCopyable.Move.";

    private const string SwitchExpressionWithWhenMatchesCopyMessage =
        "The pattern reads from a copy of the value of non-copyable type '{0}': a 'when' in a 'switch' expression makes every arm match " +
        "a copy of what it switches on, also where that is a local or a parameter. An 'is' pattern and a 'switch' expression without " +
        "'when' match it in place: use one of those, or move the value into the 'switch' with NonCopyable.Move.";

    private const string PartUsedTwiceInPatternMessage =
        "{0} and {1} use one value of a non-copyable type twice in this pattern or 'switch': the compiler copies each part that a " +
        "pattern looks into to a temporary, and the value that it is a part of still holds the original. Take the value apart in one " +
        "way only, or in a statement of its own.";

    private const string CopyOfStoredValueMessage =
        "{0} a copy of the stored value of non-copyable type '{1}'{2}. Use the value in place, use a new value, or move the stored value out " +
        "first with NonCopyable.Move.";

    private const string MemberOnReadOnlyReferenceMessage =
        "'{1}' is used through a read-only reference to a value of non-copyable type '{0}', so the compiler runs it on a copy and what it " +
        "changes is lost. Make the member 'readonly', or reach the value through a reference that can be written.";

    private const string MemberOnReadOnlyTypeParameterMessage =
        "'{1}' is used through a read-only reference to a value of type parameter '{0}'. The compiler cannot tell which members of a type " +
        "parameter are readonly, so it runs every one of them on a copy. Reach the value through a reference that can be written, such as a " +
        "'ref' parameter.";

    private const string MemberOnReadOnlyReferenceDescription =
        "The compiler runs a member that is not readonly on a defensive copy when the value is reached through 'in', 'ref readonly', a " +
        "readonly field or a 'ref readonly' local, when it is a field of something that is reached that way, and when it is a field that is " +
        "used inside a readonly member. Whatever the member changes is lost with the copy. For a value of a type parameter the compiler does " +
        "so for every member, since generic code cannot see which members are readonly.";

    private const string BoxingDescription =
        "Converting a value of a non-copyable type to object or an interface puts a copy of it in a box, and the variable it was read from still " +
        "holds the original. Convert a new value, or pass the value by reference instead.";

    private const string MemberwiseCloneCallDescription =
        "MemberwiseClone makes a shallow copy of an object or a boxed struct, field by field, so it copies every non-copyable value that is " +
        "stored in a field. A field that holds a reference to a container does not count: the clone shares the container.";

    private const string FieldOfRecordDescription =
        "A record copies its fields in 'with', in its copy constructor and in the members that the compiler writes for it (Equals, GetHashCode " +
        "and ToString), so a record, class or struct, cannot have a field of a non-copyable type. Use a class or a struct that is not a record.";

    private const string FieldOfCopyableTypeDescription =
        "Copying a struct copies its fields, so a field of a non-copyable type cannot be a member of a copyable struct. Mark the containing struct " +
        "non-copyable too, make it a class, or remove the field.";

    private const string FieldByPrimaryConstructorMessage =
        "Parameter '{1}' of the primary constructor of copyable struct '{2}' cannot have non-copyable type '{0}': members of the struct use it, " +
        "which makes it a field";

    private const string IteratorParameterMessage =
        "Iterator '{2}' keeps parameter '{1}' of non-copyable type '{0}' in the object that it returns and copies it into every enumerator. " +
        "Create the value inside the iterator, or pass an object that holds it.";

    private const string IteratorParameterDescription =
        "The compiler keeps the arguments of an iterator in the object that the method returns, and copies them into every enumerator that is " +
        "made from it. Everything that the iterator does with a by-value parameter of a non-copyable type then runs on a copy, once for each " +
        "enumeration. Create the value inside the iterator, or pass a reference to an object that holds it. An iterator that returns an " +
        "enumerator and not a sequence keeps its arguments once, and is not reported.";

    private const string AsyncOrIteratorMemberMessage =
        "'{0}' is an {2} member of non-copyable struct '{1}', so it runs on a copy of 'this' that the compiler keeps in its state machine: " +
        "what it changes is lost, and it runs later, on a copy that is out of date by then, also where the member is 'readonly'. Do the work " +
        "in a member that is neither async nor an iterator, or in a static method that is given what it needs.";

    private const string AsyncOrIteratorMemberDescription =
        "The compiler moves the body of an async method or an iterator into a state machine, and copies 'this' into it when 'this' is a " +
        "struct. Everything that such a member of a non-copyable struct does then runs on a copy: an async DisposeAsync leaves the caller's " +
        "value as it was. A 'readonly' member changes nothing, but it runs later: the body of an iterator runs when the sequence is " +
        "enumerated, on a copy that still shows the struct as it was when the member was called, such as a guard that has been released " +
        "since. A member that is neither async nor an iterator can change the struct and return a task that a static method makes, and an " +
        "iterator becomes a static iterator over what it needs.";

    private const string DefaultInterfaceMemberMessage =
        "Struct '{0}' of a non-copyable type leaves '{1}' to the default implementation of the interface, which runs on a boxed copy of the " +
        "value. Implement the member in the struct.";

    private const string DefaultInterfaceMemberDescription =
        "A call of an interface member that a struct does not implement itself runs on a boxed copy of the value, also through a constrained " +
        "type parameter, where nothing shows the box. A struct of a non-copyable type implements every member of its interfaces itself, or does " +
        "not implement an interface that has a member it cannot implement.";

    private const string DefaultInterfaceMemberBoundMessage =
        "Struct '{0}' of a non-copyable type is bound to type parameter '{2}' of '{3}', which is constrained to '{4}', and leaves '{1}' to the " +
        "default implementation of the interface, which runs on a boxed copy of the value. Implement the member in the struct.";

    private const string AnonymousTypeMemberMessage =
        "Member '{1}' of an anonymous type cannot have non-copyable type '{0}': the members of an anonymous type are auto-properties, " +
        "which its ToString, Equals and GetHashCode copy, and 'with' makes a second object that holds the same value. Use a tuple, or a " +
        "class with a field.";

    private const string PositionalParameterReadByDeconstructMessage =
        "Positional parameter '{1}' of record '{2}' cannot have non-copyable type '{0}': the Deconstruct method that the compiler writes " +
        "for the record reads the member by value, also where the record declares it as a property that returns a reference. Declare " +
        "the Deconstruct method yourself, or give the type an ordinary constructor.";

    private const string AutoPropertyDescription =
        "The getter of an auto-property returns a copy of its backing field, so an auto-property cannot have a non-copyable type. Use a field instead.";

    private const string UnlistedMemberMessage =
        "'{0}' may copy non-copyable type '{1}': type parameter '{2}' is not marked [NonCopyable] and the member is not on the safe list. Mark the " +
        "type parameter, add '{3}' to a NonCopyableSymbols.txt file if the member makes no copy, or avoid the use.";

    private const string UnlistedMemberDescription =
        "Generic code whose type parameter is not marked [NonCopyable] is not checked, so it is assumed to copy the values it handles. Its members " +
        "can only be used with a non-copyable type if a safe list names them. If the generic is yours, mark its type parameter and fix what the " +
        "analyzer then reports inside it. If it is not, and the member makes no copy other than the value it hands out, add its documentation " +
        "comment ID to an additional file named NonCopyableSymbols.txt.";

    private const string RequiresCopyableMemberDescription =
        "A member marked [RequiresCopyable] copies values of the type parameters that are marked [NonCopyable], so it cannot be used when one of " +
        "them is a non-copyable type.";

    private const string UntypedCollectionConversionMessage =
        "Container {0} is converted to untyped collection '{1}', which hands out copies of its elements. Keep the element type visible, for " +
        "example with a generic interface, or pass the container only to a member on the safe list.";

    private const string UntypedCollectionVarianceMessage =
        "The conversion of '{2}' to '{3}' turns container '{0}' into untyped collection '{1}' for the code behind the reference, which hands " +
        "out copies of its elements. Variance converts a type argument with no conversion written. Keep the type arguments the same.";

    private const string UntypedCollectionMemberMessage =
        "'{2}' uses container '{0}' as untyped collection '{1}', which hands out copies of its elements, and is not on the safe list. Add '{3}' " +
        "to a NonCopyableSymbols.txt file if the member makes no copy, or avoid the use.";

    private const string UntypedCollectionDescription =
        "System.Array and the non-generic collection interfaces hand out boxed copies of the elements. A container of non-copyable values can " +
        "only be used as one of them by a member that a safe list names.";

    private const string OpaqueConversionMessage =
        "Container {0} is converted to '{1}', which hides its non-copyable elements from the analyzer. Keep the element type visible, or pass " +
        "the container only to a member on the safe list.";

    private const string OpaqueLambdaResultMessage =
        "The result of this lambda, a container of type {0}, is converted to '{1}', which hides its non-copyable elements from the " +
        "analyzer. If the result is not meant to be returned, give the lambda a statement body, as in '() => {{ Call(); }}'. Otherwise " +
        "keep the element type visible.";

    private const string OpaqueVarianceMessage =
        "The conversion of '{2}' to '{3}' turns container '{0}' into '{1}' for the code behind the reference, which hides its non-copyable " +
        "elements from the analyzer. Variance converts a type argument with no conversion written. Keep the type arguments the same.";

    private const string OpaqueMemberMessage =
        "'{2}' uses container '{0}' as '{1}', which hides its non-copyable elements from the analyzer, and is not on the safe list. Add '{3}' to " +
        "a NonCopyableSymbols.txt file if the member makes no copy, or avoid the use.";

    private const string UntypedCollectionReturnMessage =
        "'{2}' returns container '{0}' where '{3}', which it overrides, returns untyped collection '{1}': a caller of the base member gets a " +
        "collection that hands out copies of the elements. Return the type of the base member, or keep the element type visible in both.";

    private const string OpaqueReturnMessage =
        "'{2}' returns container '{0}' where '{3}', which it overrides, returns '{1}': a caller of the base member gets the container as a type " +
        "that hides its non-copyable elements from the analyzer. Return the type of the base member, or keep the element type visible in both.";

    private const string UntypedCollectionMethodGroupMessage =
        "The conversion of '{2}' to delegate '{3}' turns container '{0}' into untyped collection '{1}', which hands out copies of its elements. " +
        "Use a delegate type that keeps the element type visible.";

    private const string OpaqueMethodGroupMessage =
        "The conversion of '{2}' to delegate '{3}' turns container '{0}' into '{1}', which hides its non-copyable elements from the analyzer. " +
        "Use a delegate type that keeps the element type visible.";

    private const string OpaqueTypeParameterConversionMessage =
        "A value of type parameter '{0}' is converted to '{1}'. With a container bound to '{0}' that hands out the container as a type that " +
        "hides its non-copyable elements from the analyzer, and nothing in the generic can tell. Keep the value as '{0}'.";

    private const string UntypedCollectionConstraintMessage =
        "'{1}' is bound to type parameter '{2}' of '{0}', which is constrained to untyped collection '{3}': the generic can use it as that " +
        "type, which hands out copies of its elements. Use a type parameter that is constrained to a type which keeps the element type visible.";

    private const string OpaqueConstraintMessage =
        "'{1}' is bound to type parameter '{2}' of '{0}', which is constrained to '{3}': the generic can use it as that type, which hides the " +
        "non-copyable type that it holds from the analyzer. Use a type parameter that is constrained to a type which keeps that type visible.";

    private const string OpaqueBoxingMessage =
        "Boxing '{0}' as '{1}' hides the non-copyable type that it holds. The code of '{2}' is not checked, and what its interfaces give on " +
        "the box are boxed copies. Keep the value as '{0}'.";

    private const string OpaqueLambdaBoxingMessage =
        "The result of this lambda, of type '{0}', is boxed as '{1}', which hides the non-copyable type that it holds. The code of '{2}' is " +
        "not checked, and what its interfaces give on the box are boxed copies. If the result is not meant to be returned, give the lambda " +
        "a statement body, as in '() => {{ Call(); }}'. Otherwise keep the value as '{0}'.";

    private const string OpaqueTypeArgumentMessage =
        "Container '{1}' is bound to type parameter '{2}' of '{0}', which is not marked [NonCopyable], and the member is not on the safe list. " +
        "Mark the type parameter, add '{3}' to a NonCopyableSymbols.txt file if the member makes no copy, or avoid the use.";

    private const string OpaqueTypeDescription =
        "Code that receives a container as object, as a base class or interface that does not name the non-copyable type, or through a type " +
        "parameter that is not marked [NonCopyable] cannot be checked, and can reach the elements with a cast. The same goes for a value of a " +
        "marked type parameter that is converted to such a type, since a container can be bound to it. A container can only be used this " +
        "way by a member that a safe list names, or if every non-copyable type in it sits inside an erasable type: one that a safe list marks, " +
        "as Task<T> is, or a class or an interface of the project, sealed or not, whose marks guarantee the same.";

    private const string ReadFromStoredContainerMessage =
        "{0} hands out a copy of non-copyable type '{1}' because it reads from a stored value{2}. Read from a new value, take the value out with " +
        "a member that removes it{3}.";

    private const string CreatedFromStoredContainerMessage =
        "This creates a value of non-copyable type '{0}' out of a stored value{1}, which still holds what the new value reads. Create it " +
        "from a new value{2}.";

    private const string ReadFromStoredContainerDescription =
        "A member on the safe list hands out a value that is as new as what the member was called on and with. Called on a stored container, such " +
        "as a list in a variable or a task in a field, it hands out a copy, and the container still holds the original. Read from a new value, " +
        "take the value out with a member that removes it, use a reference to the element, or move the stored value out first with " +
        "NonCopyable.Move.";

    private const string UnownedContainerReturnedMessage =
        "'{0}' returns a container of type {1} that something else still holds{2}, and callers take the result of a call for a new value. " +
        "{3}.";

    private const string DelegateHandsOutStoredContainerMessage =
        "The delegate for '{0}' hands out a container of type '{1}' that something else still holds, and callers of a delegate take its " +
        "result for a new value. Use a delegate type whose return value is marked [return: Borrowed], or a lambda that returns a " +
        "container which it created.";

    private const string RequiresCopyableNotOnBaseCopyConstructorMessage =
        "'{0}' cannot be marked [RequiresCopyable] because the copy constructor of its base record '{1}' is not: 'with' on a variable of " +
        "the base record runs the copy constructor of '{2}' with nothing to report it at";

    private const string RequiresCopyableOnMemberNeverCalledMessage =
        "'{0}' cannot be marked [RequiresCopyable]: the runtime runs a static constructor and a finalizer, so there is no use at which a " +
        "copy could be reported. The mark counts for nothing, and the body is checked as it would be without it.";

    private const string HolderReturnedByOverrideMessage =
        "'{2}' returns '{0}' where '{3}', which it overrides, returns '{1}': a caller of the base member takes the result for a new " +
        "'{1}', while an object of '{0}' is never new, since the code of its class decides what it holds and who else holds it. Return " +
        "the type of the base member, or mark the base member [return: Borrowed] so that its callers treat the result as stored.";

    private const string HolderLostByVarianceMessage =
        "The conversion of '{0}' to '{1}' turns '{2}' into '{3}': what comes out through '{1}' is taken for a new '{3}', while an object of " +
        "'{2}' is never new, since the code of its class decides what it holds and who else holds it. Keep the type argument '{2}'.";

    private const string DelegateOverTypeParameterMessage =
        "The delegate for '{0}' returns '{1}', which mentions the marked type parameter '{2}'. With a container bound to '{2}', every call " +
        "hands out a container that something else may still hold, and callers of a delegate take its result for a new value. Use a " +
        "delegate type whose return value is marked [return: Borrowed].";

    private const string UnownedContainerReturnedDescription =
        "The result of a call is a new value, so a method must not return, or yield, a container that something else still holds: one that " +
        "it read from a field, a property, an indexer, an element or a parameter, or a local that it also stored somewhere, passed to " +
        "something that keeps it, put a stored container into, or that a lambda captures. Every caller would read its non-copyable values as " +
        "new. That goes for an array and an ArraySegment<T> as well; a Span<T> and a Memory<T> are not checked, since every by-value read " +
        "of an element through one is reported whatever it came from. A method whose return value is marked [Borrowed] keeps what it returns, and its " +
        "callers treat the result as stored, as they treat what a property returns.";

    private const string KeepsUnmarkedParameterMessage =
        "'{0}' keeps its parameter '{1}', a container of type {2}, which is not marked [Kept]. A caller takes a container that it passes to " +
        "be its own afterwards. Mark the parameter [Kept], or do not store it.";

    private const string ParameterUsedInAWayNotFollowedMessage =
        "'{0}' uses its parameter '{1}', a container of type {2}, which is not marked [Kept], in a way that is not known to leave it with " +
        "the caller alone, so it counts as kept. A caller takes a container that it passes to be its own afterwards. Mark the parameter " +
        "[Kept], or use it in a way that is known: use a member of it, pass it to a parameter that does not keep it, test it or enumerate it.";

    private const string PassesParameterToUncheckedCodeMessage =
        "'{0}' passes its parameter '{1}', a container of type {2}, which is not marked [Kept], to '{3}', which is taken to keep it: the " +
        "type of the parameter that receives it mentions a type parameter that is not marked [NonCopyable], so nothing checks what the " +
        "member does with the container. A caller takes a container that it passes to be its own afterwards. If the member keeps nothing " +
        "of it, add '{4};borrows' to a NonCopyableSymbols.txt file; if it keeps the container, mark the parameter [Kept].";

    private const string KeepsParameterAfterReturningMessage =
        "'{0}' {1} its parameter '{2}', a container of type {3}, which is not marked [Kept]. A caller takes a container that it passes to " +
        "be its own afterwards. Mark the parameter [Kept]{4}.";

    private const string KeepsUnmarkedParameterDescription =
        "What a method is given, it only uses during the call, unless it says that it keeps it. A caller that passes a container which it " +
        "created still has the only reference afterwards, and may return or move it as its own. So a method, a local function, a lambda or an " +
        "operator that assigns a container parameter to a field, an element or a property, captures it, or passes it on to something that " +
        "keeps it has to mark the parameter [Kept]. The parameters of a constructor and of a setter are taken to be kept.";

    private const string ContainerMovedFromSlotParameterMessage =
        "Container '{0}' is moved out of [MovedFrom] parameter '{1}', which is not the method's own: {2} gives the parameter the type of a " +
        "type parameter, so generic code passes it a slot of its own, and with a container bound that slot may hold a container that " +
        "something else still holds. Use the container in place.";

    private const string SlotGivenToOwnerMessage =
        "'{0}' takes its [MovedFrom] parameter '{1}' to be its own, and {2} gives that parameter the type of a type parameter: generic code " +
        "passes it a slot of its own, which with a container bound may hold a container that something else still holds. Use a method or a " +
        "lambda that uses the container in place.";

    private const string ContainerMovedFromUnownedMessage =
        "Container {0} is moved out of '{1}', which is not a local that the method owns{2}, so clearing it does not make this the only " +
        "reference to the container. Move a local that holds a container which the method created or got from a call.";

    private const string ContainerNotOwnedDescription =
        "A value of a non-copyable type has one holder by construction, but a reference to a container can be copied freely. Code that takes " +
        "a container over has to get the only reference to it. So a container that is passed to a [MovedFrom] parameter must be a local that " +
        "the method owns: one that it only filled, never stored elsewhere and never passed to something that keeps it. A [MovedFrom] " +
        "parameter of the method itself is its own as well, unless the member that its caller sees, which it overrides, implements or is " +
        "converted to as a delegate, gives the parameter the type of a type parameter: the caller is then generic code, which passes a slot " +
        "without knowing what it holds. Nothing can show that a container in a field is unshared, so taking it out of one needs a suppression.";

    private const string MovedFromAddedMessage =
        "Parameter '{0}' of '{1}' cannot be marked [MovedFrom] because {2} does not mark it: a caller through that passes anything, and not " +
        "only what it owns";

    private const string MovedFromDroppedMessage =
        "Parameter '{0}' of '{1}' must be marked [MovedFrom] because {2} marks it: a caller through that trusts what the method leaves in " +
        "its variable, and the body is checked for that only with the mark";

    private const string MovedFromDroppedWithNoParameterListMessage =
        "The anonymous method is converted to {0}, which marks its parameter '{1}' [MovedFrom], and has no parameter list in which the " +
        "parameter could be marked: a caller through the delegate trusts what the method leaves in its variable, and the body is checked " +
        "for that only with the mark. Write the parameter list, and mark the parameter [MovedFrom].";

    private const string KeptNotOnBaseMemberMessage =
        "Parameter '{0}' of '{1}' cannot be marked [Kept] because '{2}', which the member overrides or implements, only uses what it is given: " +
        "a caller through that member takes its container to be its own afterwards";

    private const string KeptNotOnDelegateMessage =
        "Parameter '{0}' of '{1}' is marked [Kept], and the member is converted to delegate '{2}', whose parameter is not: a caller of the " +
        "delegate takes its container to be its own afterwards. Use a delegate type whose parameter is marked [Kept] as well.";

    private const string KeepingNotOnDelegateMessage =
        "Parameter '{0}' of '{1}' is taken to keep what it is given, {3}, and the member is " +
        "converted to delegate '{2}', whose parameter is not marked [Kept]: a caller of the delegate takes its container to be its own " +
        "afterwards. Use a delegate type whose parameter is marked [Kept].";

    private const string KeepingNotOnInterfaceMemberMessage =
        "Parameter '{0}' of '{1}' is taken to keep what it is given, {4}, and '{3}' inherits " +
        "the member as its implementation of '{2}', whose parameter is not marked [Kept]: a caller through the interface takes its " +
        "container to be its own afterwards. Implement the member in '{3}', or mark the parameter of the interface member [Kept].";

    private const string KeepsByTypeParameter = "as a parameter whose type is a type parameter is";

    private const string KeepsByUncheckedCode =
        "as a parameter whose type mentions a type parameter that is not marked [NonCopyable] is where the list does not mark the member 'borrows'";

    /// <summary>
    /// Says in the words of a message by what a parameter is taken to keep what it is given without a mark.
    /// </summary>
    public static string DescribeKeeping(bool byUncheckedCode) => byUncheckedCode ? KeepsByUncheckedCode : KeepsByTypeParameter;

    private const string BorrowedNotOnBaseMemberMessage =
        "'{0}' cannot be marked [return: Borrowed] because '{1}', which it overrides or implements, is not: a caller through that member " +
        "takes the result for a new value";

    private const string BorrowedNotOnDelegateMessage =
        "'{0}' is marked [return: Borrowed] and is converted to delegate '{1}', whose return value is not: a caller of the delegate takes the " +
        "result for a new value. Use a delegate type whose return value is marked [Borrowed] as well.";

    private const string NonCopyableAwaiterMessage =
        "The awaiter of this 'await' is of non-copyable type '{0}'. When the 'await' suspends, the compiler copies the awaiter into its " +
        "state machine, calls OnCompleted on the original and GetResult on the copy. Make the awaiter a class or a copyable struct that " +
        "refers to the state.";

    private const string NonCopyableAwaiterDescription =
        "At an 'await' that does not complete at once the compiler stores the awaiter in a field of the state machine, calls OnCompleted " +
        "or UnsafeOnCompleted on the local that it copied it from, and calls GetResult on the field when the method resumes. So what " +
        "OnCompleted takes out of the awaiter is still in the copy that GetResult sees. The copy is made whatever the awaited type is, and " +
        "nothing in the source shows it.";

    private const string ConditionalMarkerMessage =
        "Attribute '{0}' is declared [Conditional]. The compiler leaves a conditional attribute out of the assembly unless its symbol is " +
        "defined, so the marks would hold inside this project and be gone for every project that uses it. Remove [Conditional] from the " +
        "attribute.";

    private const string ConditionalMarkerBaseMessage =
        "Attribute '{0}' derives from '{1}', which is declared [Conditional]. A class that derives from a conditional attribute is " +
        "conditional too: the compiler leaves its uses out of the assembly unless the symbol is defined, so the marks would hold inside " +
        "this project and be gone for every project that uses it. Derive the attribute from a class that is not conditional.";

    private const string ConditionalMarkerUseMessage =
        "Attribute '{0}' is declared [Conditional] in '{1}', and '{2}' is not defined here: the compiler leaves this use out of the " +
        "assembly, so the mark holds inside this project and is gone for every project that uses it. Use an attribute that is not " +
        "conditional: the one of the Singulink.CodeAnalysis package, or one that the project declares in the namespace " +
        "Singulink.CodeAnalysis.";

    private const string ForeignNonCopyableAttributeMessage =
        "'{0}' is not the attribute that the analyzer reads: only 'Singulink.CodeAnalysis.NonCopyableAttribute' is recognised, so a " +
        "struct that is marked with this class is copied with no finding. Declare the class in the namespace 'Singulink.CodeAnalysis', " +
        "or suppress this finding if the attribute is meant for something else.";

    private const string NonCopyableAttributeWithNoSuffixMessage =
        "'{0}' is not the attribute that the analyzer reads: only 'Singulink.CodeAnalysis.NonCopyableAttribute' is recognised, by that " +
        "name, so a struct that is marked with this class is copied with no finding. Name the class 'NonCopyableAttribute' and declare " +
        "it in the namespace 'Singulink.CodeAnalysis', or suppress this finding if the attribute is meant for something else.";

    private const string ForeignNonCopyableAttributeUseMessage =
        "This mark is not recognised: '{0}' of '{1}' is not the attribute that the analyzer reads, so {2}. Use " +
        "'Singulink.CodeAnalysis.NonCopyableAttribute', from the Singulink.CodeAnalysis package or declared by the project in the namespace " +
        "'Singulink.CodeAnalysis', or suppress this finding if the attribute is meant for something else.";

    private const string ForeignNonCopyableAttributeDescription =
        "The attributes of the analyzer are recognised by their full name: the namespace Singulink.CodeAnalysis and the name of the class, " +
        "in any assembly, public or internal. A class of the same name in another namespace has no effect on what the project declares, " +
        "since it can as well be the attribute of another tool. For NonCopyableAttribute that fails without a sign: the struct is " +
        "copyable in a build that looks clean. So an attribute class of that name anywhere else is reported where it is declared, and " +
        "where it is used if a referenced assembly declares it. On a struct of a referenced assembly, which the project cannot change, " +
        "a NonCopyableAttribute of any namespace is recognised.";

    private const string ConditionalMarkerDescription =
        "A project may declare the attributes of the analyzer itself. Inside the project the analyzer sees every use of such an " +
        "attribute in the source. Another project sees what the compiler has written into the assembly, and it writes a use of a " +
        "conditional attribute only where the symbol of the attribute is defined. A struct that is marked [NonCopyable] in the library " +
        "would be copyable for its users, and a [Borrowed] or [Kept] mark would be lost in the same way.";

    private const string ErasableBaseMessage =
        "'{0}' derives from or implements erasable type '{1}' and does not stay erasable: {2}. An object of it that is converted as '{1}' can " +
        "be converted on to a type that hands out its non-copyable values untyped.";

    private const string ErasableBaseDescription =
        "A container of an erasable type may be converted to a type that hides its non-copyable type, because no base type and no interface " +
        "of an erasable type hands out what it holds. A conversion sees only the type that it is written with, and the object behind it can " +
        "be of a type that derives from it. So such a type must not add a way out: a base class or an interface with bodies that are not " +
        "checked for the non-copyable type that it receives, or an interface with a variant type parameter, 'in' or 'out' and at any depth " +
        "of its type arguments, that is bound to a reference type which mentions the non-copyable type. A type parameter that is not " +
        "constrained to 'struct' counts as a reference type.";

    private const string TypeParameterMarkNotKeptMessage =
        "Type parameter '{0}' of '{1}' must be marked [NonCopyable] because type parameter '{2}' of '{3}', which the member overrides or " +
        "implements, is";

    private const string MarkNotKeptDescription =
        "Callers trust a type parameter that is marked [NonCopyable], and a member that is not marked [RequiresCopyable], not to copy, and " +
        "they take what a method returns for a new value unless its return value is marked [Borrowed], and what they pass to it to be still " +
        "their own unless the parameter is marked [Kept]. A derived type, an override or an implementation that drops a mark, or adds " +
        "[Borrowed] or [Kept], would put code that is not checked behind that promise. [MovedFrom] is two promises, to the method that its " +
        "caller owned what it passed and to the caller that the method leaves only what the caller may own, so it is kept in both " +
        "directions: an override, an implementation or a function that is converted to a delegate has it on exactly the parameters on " +
        "which the member that it stands for has it.";

    private const string MovedFromParameterNotAssignedMessage =
        "'{0}' can leave with what it read here from [MovedFrom] parameter '{1}' still in the parameter. A method that moves a value out " +
        "must leave a fresh one behind on every path, for example with '{1} = default' right after the read.";

    private const string MovedFromParameterReadAgainMessage =
        "[MovedFrom] parameter '{0}' is read again before it is assigned. Only one read can be the move, and every other read is a copy. " +
        "Read it once into a local, and assign the parameter before it is read again.";

    private const string MovedFromParameterNotAssignedAtOnceMessage =
        "Between the read of [MovedFrom] parameter '{0}' and its assignment the method runs {1}. If that throws, or reaches the same " +
        "variable in another way, the value is in two places. Do it before the read, into a local, and assign '{0}' right after the read.";

    private const string MovedFromParameterNotAssignedAtOnceThroughReferenceMessage =
        "Between the read of [MovedFrom] parameter '{0}' and its assignment the method reads or writes through '{1}', which can be a null " +
        "reference: if that fails, the value is in two places. Check the reference before the read of '{0}', with " +
        "NonCopyable.ThrowIfNullRef(in {1}) or with a method of the project whose parameter is marked [NotNullRef].";

    private const string NotNullRefMisplacedMessage =
        "[NotNullRef] on parameter '{0}' of '{1}' does not count: {2}. A call of '{1}' does not make a reference known to refer to something.";

    private const string NotNullRefNotCheckedMessage =
        "The analyzer does not check that '{1}' returns only if the reference that it is given for parameter '{0}' refers to something, " +
        "as [NotNullRef] says: what it reports about a [MovedFrom] parameter and about a value that is committed at the end of a 'try' " +
        "block rests on that. Verify and test the method, then suppress this finding here.";

    private const string NestedTooDeeplyMessage =
        "'{0}' is nested too deeply for the analyzer and was not checked: a copy in it is not reported. Break up the expression that is " +
        "nested this deeply, with locals or with methods of its own.";

    private const string NestedTooDeeplyDescription =
        "The analyzer walks an expression by recursion and stops before the stack runs out: a chain of thousands of operators or of a " +
        "thousand calls is deeper than it can follow. The member is then not checked at all, and this finding says so, so that a member " +
        "that is not checked never looks clean.";

    private const string NotNullRefDescription =
        "A read or a write through a null reference fails, so a read or a write through a 'ref', 'in' or 'out' parameter, a 'ref' local " +
        "or 'this' in a struct counts as able to throw unless the reference is known to refer to something. [NotNullRef] on a 'ref', " +
        "'in', 'ref readonly' or 'out' parameter says that the method returns only if the reference that it is given refers to " +
        "something, so a call that returns proves the argument. The mark counts where a call names the code that runs: on a method that " +
        "is not virtual, abstract or an override, static or not, an extension method included, and on a local function. It does not " +
        "count on a by-value parameter, on a member of an interface, a delegate, a lambda, an operator, a constructor or an accessor, " +
        "or on a method that the compiler can leave out of a build, which is one that is [Conditional] or a partial method with no " +
        "body. The body of the method is not checked: the mark is a statement of its author. So every mark that a project declares is " +
        "reported once, where it stands, and is suppressed there when the method has been verified and tested. NonCopyable.ThrowIfNullRef " +
        "and NonCopyable.ThrowIfNullRefArgument of the attributes package are two such methods.";

    private const string MovedFromParameterReassignedMessage =
        "[MovedFrom] parameter '{0}' is made to refer to something else. The assignment that follows then resets that, and the caller's " +
        "variable keeps the value that was moved out. Assign the parameter itself.";

    private const string GenericMoveReturnsSomethingElseMessage =
        "'{0}' is a generic move, and returns something other than 'default' or what it read from its [MovedFrom] parameters. With a " +
        "container bound to '{1}', its callers take what it returns to be the container that they passed, which nobody else holds. Return " +
        "what was moved out, or mark the return value [return: Borrowed] so that callers treat a container that it hands out as stored.";

    private const string GenericMoveLeavesSomethingElseMessage =
        "'{0}' is a generic move, and leaves something in [MovedFrom] parameter '{2}' other than 'default' or what it read from its " +
        "[MovedFrom] parameters. With a container bound to '{1}', its callers take what is left in their variable to be their own.";

    private const string MovedFromContainerNotOwnedMessage =
        "[MovedFrom] parameter '{0}' is assigned a container that something else still holds. The caller's variable counts as its own " +
        "afterwards, so leave null, a new value or a local that the method owns in it.";

    private const string MovedFromContainerExposedMessage =
        "[MovedFrom] parameter '{0}' {1}, which can leave a container in it that something else still holds. The caller's variable counts as " +
        "its own afterwards, so assign the parameter itself, or pass it on only to a [MovedFrom] parameter of a move.";

    private const string ImplementationNotCheckedMessage =
        "'{0}', which '{2}' inherits and uses to implement '{1}', is not checked code for non-copyable type '{3}': type parameter '{4}' is not " +
        "marked [NonCopyable]. Implement the interface member in '{2}', or mark the type parameter.";

    private const string MovedFromOnPrimaryConstructorMessage =
        "[MovedFrom] cannot be applied to parameter '{0}' of the primary constructor of '{1}': the code of a primary constructor is the " +
        "initializers of the type and the call of the base constructor, which have no order in which the parameter could be read and " +
        "then assigned. Declare an ordinary constructor.";

    private const string MovedFromPathsNotCheckedMessage =
        "The paths of '{1}' are not followed for its [MovedFrom] parameter '{0}', so nothing shows that the parameter is assigned after " +
        "it is read. Declare the member as an ordinary method or constructor.";

    private const string MovedFromDescription =
        "[MovedFrom] says that a method takes the value out of a 'ref' parameter and leaves a fresh one behind, which is what makes reading " +
        "the parameter by value a move and not a copy. It has no meaning on anything else, a method that does not assign the parameter leaves " +
        "the caller with a second copy, and so does a method that reads the parameter by value more than once. A caller's local that holds " +
        "a container stays the caller's own after the call, so what the method leaves in a parameter of a container type is null, a new " +
        "value or a local of its own. A generic move, whose [MovedFrom] parameters have a marked type parameter as their type, is checked to " +
        "be a pure move: what it returns and what it leaves behind is 'default' or what it read from those parameters.";

    private const string SafeListNonCopyableNamesNothingMessage =
        "This line of the safe list marks '{0}' as non-copyable, and the compilation has no type of that ID: the type stays copyable. Fix the " +
        "ID, or take the line out of the file of a project that does not reference the type.";

    private const string SafeListCopyableHasNoEffectMessage =
        "This line of the safe list marks '{0}' as copyable. It has no effect, because {1}.";

    private const string SafeListLineDescription =
        "A line of a NonCopyableSymbols.txt file is a documentation comment ID that starts with 'M:', 'P:', 'E:' or 'T:', optionally followed " +
        "by ';' and a marker: 'new' or 'borrows' for a member, 'drains' for a member that returns a sequence, 'owned' for an instance property " +
        "of a class whose type is Task<TResult>, 'view', " +
        "'noncopyable' or 'copyable' for a struct, 'erasable' for a generic class or interface. A type " +
        "needs a marker. A member can end with ';for=' and the names of the type parameters that the entry vouches for. A line that starts with " +
        "'#' is a comment, and so is the rest of a line after white space and '#'.";

    public static readonly DiagnosticDescriptor CopyByAssignment = Create(
        DiagnosticIds.CopyByAssignment,
        CopyByAssignmentTitle,
        CopyByAssignmentMessage,
        CopyByAssignmentDescription);

    public static readonly DiagnosticDescriptor CopyByAssignmentIntoVariable = Create(
        DiagnosticIds.CopyByAssignment,
        CopyByAssignmentTitle,
        "Variable '{1}' is a copy of a stored value of non-copyable type '{0}'",
        CopyByAssignmentIntoVariableDescription);

    public static readonly DiagnosticDescriptor CopyByAssignmentIntoForEachVariable = Create(
        DiagnosticIds.CopyByAssignment,
        CopyByAssignmentTitle,
        CopyByAssignmentIntoForEachVariableMessage,
        CopyByAssignmentIntoVariableDescription);

    public static readonly DiagnosticDescriptor CopyByAssignmentIntoSecondVariable = Create(
        DiagnosticIds.CopyByAssignment,
        CopyByAssignmentTitle,
        "Variable '{1}' is a copy of a value of non-copyable type '{0}' that the pattern gives to another variable as well. A value goes to one variable only.",
        CopyByAssignmentIntoVariableDescription);

    public static readonly DiagnosticDescriptor CopyByAssignmentForDeconstruct = Create(
        DiagnosticIds.CopyByAssignment,
        CopyByAssignmentTitle,
        CopyByAssignmentForDeconstructMessage,
        CopyByAssignmentIntoVariableDescription);

    public static readonly DiagnosticDescriptor DeconstructionOfStoredValue = Create(
        DiagnosticIds.CopyByAssignment,
        CopyByAssignmentTitle,
        DeconstructionOfStoredValueMessage,
        CopyByAssignmentIntoVariableDescription);

    public static readonly DiagnosticDescriptor CopyByAssignmentOutOfBox = Create(
        DiagnosticIds.CopyByAssignment,
        CopyByAssignmentTitle,
        "Converting '{1}' to non-copyable type '{0}' copies the value out of the box",
        CopyByAssignmentOutOfBoxDescription);

    public static readonly DiagnosticDescriptor CopyByReturnOfLivingVariable = Create(
        DiagnosticIds.CopyByReturn,
        CopyByReturnTitle,
        CopyByReturnOfLivingVariableMessage,
        CopyByReturnOfLivingVariableDescription);

    public static readonly DiagnosticDescriptor CopyByReturnOfVariableUsedAfter = Create(
        DiagnosticIds.CopyByReturn,
        CopyByReturnTitle,
        CopyByReturnOfVariableUsedAfterMessage,
        CopyByReturnOfLivingVariableDescription);

    public static readonly DiagnosticDescriptor CopyByReturn = Create(
        DiagnosticIds.CopyByReturn,
        CopyByReturnTitle,
        "Do not return a stored value of non-copyable type '{0}' by value",
        CopyByReturnDescription);

    public static readonly DiagnosticDescriptor UnsupportedUse = Create(
        DiagnosticIds.UnsupportedUse,
        UnsupportedUseTitle,
        UnsupportedUseMessage,
        UnsupportedUseDescription);

    public static readonly DiagnosticDescriptor ValuePassedTwice = Create(
        DiagnosticIds.UnsupportedUse,
        UnsupportedUseTitle,
        ValuePassedTwiceMessage,
        UnsupportedUseDescription);

    public static readonly DiagnosticDescriptor DelegatesCombined = Create(
        DiagnosticIds.UnsupportedUse,
        UnsupportedUseTitle,
        DelegatesCombinedMessage,
        UnsupportedUseDescription);

    public static readonly DiagnosticDescriptor CopyOfStoredValue = Create(
        DiagnosticIds.UnsupportedUse,
        UnsupportedUseTitle,
        CopyOfStoredValueMessage,
        UnsupportedUseDescription);

    public static readonly DiagnosticDescriptor SwitchStatementMatchesCopy = Create(
        DiagnosticIds.UnsupportedUse,
        UnsupportedUseTitle,
        SwitchStatementMatchesCopyMessage,
        UnsupportedUseDescription);

    public static readonly DiagnosticDescriptor SwitchExpressionWithWhenMatchesCopy = Create(
        DiagnosticIds.UnsupportedUse,
        UnsupportedUseTitle,
        SwitchExpressionWithWhenMatchesCopyMessage,
        UnsupportedUseDescription);

    public static readonly DiagnosticDescriptor PartUsedTwiceInPattern = Create(
        DiagnosticIds.UnsupportedUse,
        UnsupportedUseTitle,
        PartUsedTwiceInPatternMessage,
        UnsupportedUseDescription);

    public static readonly DiagnosticDescriptor MemberOnReadOnlyReference = Create(
        DiagnosticIds.MemberOnReadOnlyReference,
        MemberOnReadOnlyReferenceTitle,
        MemberOnReadOnlyReferenceMessage,
        MemberOnReadOnlyReferenceDescription);

    public static readonly DiagnosticDescriptor MemberOnReadOnlyTypeParameter = Create(
        DiagnosticIds.MemberOnReadOnlyReference,
        MemberOnReadOnlyReferenceTitle,
        MemberOnReadOnlyTypeParameterMessage,
        MemberOnReadOnlyReferenceDescription);

    public static readonly DiagnosticDescriptor Boxing = Create(
        DiagnosticIds.Boxing,
        "Do not box a stored non-copyable value",
        "Converting a stored value of non-copyable type '{0}' to '{1}' boxes a copy of it",
        BoxingDescription);

    public static readonly DiagnosticDescriptor MemberwiseCloneCall = Create(
        DiagnosticIds.MemberwiseCloneCall,
        MemberwiseCloneTitle,
        "MemberwiseClone of '{0}' copies its field '{2}' of non-copyable type '{1}'",
        MemberwiseCloneCallDescription);

    public static readonly DiagnosticDescriptor MemberwiseCloneCallOnValue = Create(
        DiagnosticIds.MemberwiseCloneCall,
        MemberwiseCloneTitle,
        "MemberwiseClone copies a value of non-copyable type '{0}'",
        MemberwiseCloneCallDescription);

    public static readonly DiagnosticDescriptor FieldOfCopyableType = Create(
        DiagnosticIds.FieldOfCopyableType,
        FieldOfCopyableTypeTitle,
        "Copyable field '{1}' cannot have non-copyable type '{0}'",
        FieldOfCopyableTypeDescription);

    public static readonly DiagnosticDescriptor FieldOfRecord = Create(
        DiagnosticIds.FieldOfCopyableType,
        FieldOfCopyableTypeTitle,
        "Field '{1}' of a record cannot have non-copyable type '{0}'",
        FieldOfRecordDescription);

    public static readonly DiagnosticDescriptor FieldByPrimaryConstructor = Create(
        DiagnosticIds.FieldOfCopyableType,
        FieldOfCopyableTypeTitle,
        FieldByPrimaryConstructorMessage,
        FieldOfCopyableTypeDescription);

    public static readonly DiagnosticDescriptor AutoProperty = Create(
        DiagnosticIds.AutoProperty,
        "Do not declare an auto-property of a non-copyable type",
        "Auto-property '{1}' cannot have non-copyable type '{0}'",
        AutoPropertyDescription);

    public static readonly DiagnosticDescriptor AnonymousTypeMember = Create(
        DiagnosticIds.AutoProperty,
        "Do not declare an auto-property of a non-copyable type",
        AnonymousTypeMemberMessage,
        AutoPropertyDescription);

    public static readonly DiagnosticDescriptor PositionalParameterReadByDeconstruct = Create(
        DiagnosticIds.AutoProperty,
        "Do not declare an auto-property of a non-copyable type",
        PositionalParameterReadByDeconstructMessage,
        AutoPropertyDescription);

    public static readonly DiagnosticDescriptor IteratorParameter = Create(
        DiagnosticIds.IteratorParameter,
        "Do not give an iterator that returns a sequence a by-value parameter of a non-copyable type",
        IteratorParameterMessage,
        IteratorParameterDescription);

    public static readonly DiagnosticDescriptor DefaultInterfaceMember = Create(
        DiagnosticIds.DefaultInterfaceMember,
        DefaultInterfaceMemberTitle,
        DefaultInterfaceMemberMessage,
        DefaultInterfaceMemberDescription);

    public static readonly DiagnosticDescriptor DefaultInterfaceMemberBound = Create(
        DiagnosticIds.DefaultInterfaceMember,
        DefaultInterfaceMemberTitle,
        DefaultInterfaceMemberBoundMessage,
        DefaultInterfaceMemberDescription);

    public static readonly DiagnosticDescriptor AsyncOrIteratorMember = Create(
        DiagnosticIds.AsyncOrIteratorMember,
        "Do not make an instance member of a non-copyable struct async or an iterator",
        AsyncOrIteratorMemberMessage,
        AsyncOrIteratorMemberDescription);

    public static readonly DiagnosticDescriptor UnlistedMember = Create(
        DiagnosticIds.UnlistedMember,
        "Do not use a member of an unmarked generic with a non-copyable type",
        UnlistedMemberMessage,
        UnlistedMemberDescription);

    public static readonly DiagnosticDescriptor RequiresCopyableMember = Create(
        DiagnosticIds.RequiresCopyable,
        "Do not use a member that requires a copyable type with a non-copyable type",
        "'{0}' requires a copyable type and cannot be used with non-copyable type '{1}' as '{2}'. Use a member that does not copy.",
        RequiresCopyableMemberDescription);

    public static readonly DiagnosticDescriptor UntypedCollectionConversion = Create(
        DiagnosticIds.UntypedCollection,
        UntypedCollectionTitle,
        UntypedCollectionConversionMessage,
        UntypedCollectionDescription);

    public static readonly DiagnosticDescriptor UntypedCollectionVariance = Create(
        DiagnosticIds.UntypedCollection,
        UntypedCollectionTitle,
        UntypedCollectionVarianceMessage,
        UntypedCollectionDescription);

    public static readonly DiagnosticDescriptor UntypedCollectionMember = Create(
        DiagnosticIds.UntypedCollection,
        UntypedCollectionTitle,
        UntypedCollectionMemberMessage,
        UntypedCollectionDescription);

    public static readonly DiagnosticDescriptor UntypedCollectionReturn = Create(
        DiagnosticIds.UntypedCollection,
        UntypedCollectionTitle,
        UntypedCollectionReturnMessage,
        UntypedCollectionDescription);

    public static readonly DiagnosticDescriptor UntypedCollectionMethodGroup = Create(
        DiagnosticIds.UntypedCollection,
        UntypedCollectionTitle,
        UntypedCollectionMethodGroupMessage,
        UntypedCollectionDescription);

    public static readonly DiagnosticDescriptor UntypedCollectionConstraint = Create(
        DiagnosticIds.UntypedCollection,
        UntypedCollectionTitle,
        UntypedCollectionConstraintMessage,
        UntypedCollectionDescription);

    public static readonly DiagnosticDescriptor OpaqueConversion = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueConversionMessage,
        OpaqueTypeDescription);

    public static readonly DiagnosticDescriptor OpaqueVariance = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueVarianceMessage,
        OpaqueTypeDescription);

    public static readonly DiagnosticDescriptor OpaqueMember = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueMemberMessage,
        OpaqueTypeDescription);

    public static readonly DiagnosticDescriptor OpaqueReturn = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueReturnMessage,
        OpaqueTypeDescription);

    public static readonly DiagnosticDescriptor OpaqueMethodGroup = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueMethodGroupMessage,
        OpaqueTypeDescription);

    public static readonly DiagnosticDescriptor OpaqueTypeParameterConversion = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueTypeParameterConversionMessage,
        OpaqueTypeDescription);

    public static readonly DiagnosticDescriptor OpaqueConstraint = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueConstraintMessage,
        OpaqueTypeDescription);

    public static readonly DiagnosticDescriptor OpaqueBoxing = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueBoxingMessage,
        OpaqueTypeDescription);

    public static readonly DiagnosticDescriptor OpaqueLambdaBoxing = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueLambdaBoxingMessage,
        OpaqueTypeDescription);

    public static readonly DiagnosticDescriptor OpaqueTypeArgument = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueTypeArgumentMessage,
        OpaqueTypeDescription);

    public static readonly DiagnosticDescriptor ReadFromStoredContainer = Create(
        DiagnosticIds.ReadFromStoredContainer,
        ReadFromStoredContainerTitle,
        ReadFromStoredContainerMessage,
        ReadFromStoredContainerDescription);

    public static readonly DiagnosticDescriptor CreatedFromStoredContainer = Create(
        DiagnosticIds.ReadFromStoredContainer,
        ReadFromStoredContainerTitle,
        CreatedFromStoredContainerMessage,
        ReadFromStoredContainerDescription);

    public static readonly DiagnosticDescriptor OpaqueLambdaResult = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueLambdaResultMessage,
        OpaqueTypeDescription);

    public static readonly DiagnosticDescriptor UnownedContainerReturned = Create(
        DiagnosticIds.UnownedContainerReturned,
        UnownedContainerReturnedTitle,
        UnownedContainerReturnedMessage,
        UnownedContainerReturnedDescription);

    public static readonly DiagnosticDescriptor DelegateHandsOutStoredContainer = Create(
        DiagnosticIds.UnownedContainerReturned,
        UnownedContainerReturnedTitle,
        DelegateHandsOutStoredContainerMessage,
        UnownedContainerReturnedDescription);

    public static readonly DiagnosticDescriptor DelegateOverTypeParameter = Create(
        DiagnosticIds.UnownedContainerReturned,
        UnownedContainerReturnedTitle,
        DelegateOverTypeParameterMessage,
        UnownedContainerReturnedDescription);

    public static readonly DiagnosticDescriptor HolderReturnedByOverride = Create(
        DiagnosticIds.UnownedContainerReturned,
        UnownedContainerReturnedTitle,
        HolderReturnedByOverrideMessage,
        UnownedContainerReturnedDescription);

    public static readonly DiagnosticDescriptor HolderLostByVariance = Create(
        DiagnosticIds.UnownedContainerReturned,
        UnownedContainerReturnedTitle,
        HolderLostByVarianceMessage,
        UnownedContainerReturnedDescription);

    public static readonly DiagnosticDescriptor TypeParameterMarkNotPassedOn = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        "Type parameter '{0}' is passed to [NonCopyable] type parameter '{1}' of '{2}' and must be marked [NonCopyable] as well",
        MarkNotKeptDescription);

    public static readonly DiagnosticDescriptor TypeParameterMarkNotKept = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        TypeParameterMarkNotKeptMessage,
        MarkNotKeptDescription);

    public static readonly DiagnosticDescriptor RequiresCopyableNotOnBaseMember = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        "'{0}' cannot be marked [RequiresCopyable] because '{1}', which it overrides or implements, is not",
        MarkNotKeptDescription);

    public static readonly DiagnosticDescriptor RequiresCopyableNotOnBaseCopyConstructor = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        RequiresCopyableNotOnBaseCopyConstructorMessage,
        MarkNotKeptDescription);

    public static readonly DiagnosticDescriptor RequiresCopyableOnMemberNeverCalled = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        RequiresCopyableOnMemberNeverCalledMessage,
        MarkNotKeptDescription);

    public static readonly DiagnosticDescriptor BorrowedNotOnBaseMember = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        BorrowedNotOnBaseMemberMessage,
        MarkNotKeptDescription);

    public static readonly DiagnosticDescriptor BorrowedNotOnDelegate = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        BorrowedNotOnDelegateMessage,
        MarkNotKeptDescription);

    public static readonly DiagnosticDescriptor KeepsUnmarkedParameter = Create(
        DiagnosticIds.KeptParameter,
        "Mark a container parameter that the method keeps",
        KeepsUnmarkedParameterMessage,
        KeepsUnmarkedParameterDescription);

    public static readonly DiagnosticDescriptor ParameterUsedInAWayNotFollowed = Create(
        DiagnosticIds.KeptParameter,
        "Mark a container parameter that the method keeps",
        ParameterUsedInAWayNotFollowedMessage,
        KeepsUnmarkedParameterDescription);

    public static readonly DiagnosticDescriptor PassesParameterToUncheckedCode = Create(
        DiagnosticIds.KeptParameter,
        "Mark a container parameter that the method keeps",
        PassesParameterToUncheckedCodeMessage,
        KeepsUnmarkedParameterDescription);

    public static readonly DiagnosticDescriptor KeepsParameterAfterReturning = Create(
        DiagnosticIds.KeptParameter,
        "Mark a container parameter that the method keeps",
        KeepsParameterAfterReturningMessage,
        KeepsUnmarkedParameterDescription);

    public static readonly DiagnosticDescriptor ContainerMovedFromUnowned = Create(
        DiagnosticIds.ContainerNotOwned,
        ContainerNotOwnedTitle,
        ContainerMovedFromUnownedMessage,
        ContainerNotOwnedDescription);

    public static readonly DiagnosticDescriptor ContainerMovedFromSlotParameter = Create(
        DiagnosticIds.ContainerNotOwned,
        ContainerNotOwnedTitle,
        ContainerMovedFromSlotParameterMessage,
        ContainerNotOwnedDescription);

    /// <summary>
    /// The same finding for a method that is used as a method group, which is known only when the whole compilation has been seen.
    /// </summary>
    public static readonly DiagnosticDescriptor ContainerMovedFromSlotParameterOfMethodGroup = Create(
        DiagnosticIds.ContainerNotOwned,
        ContainerNotOwnedTitle,
        ContainerMovedFromSlotParameterMessage,
        ContainerNotOwnedDescription,
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor SlotGivenToOwner = Create(
        DiagnosticIds.ContainerNotOwned,
        ContainerNotOwnedTitle,
        SlotGivenToOwnerMessage,
        ContainerNotOwnedDescription);

    public static readonly DiagnosticDescriptor MovedFromAdded = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        MovedFromAddedMessage,
        MarkNotKeptDescription);

    public static readonly DiagnosticDescriptor MovedFromDropped = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        MovedFromDroppedMessage,
        MarkNotKeptDescription);

    public static readonly DiagnosticDescriptor MovedFromDroppedWithNoParameterList = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        MovedFromDroppedWithNoParameterListMessage,
        MarkNotKeptDescription);

    public static readonly DiagnosticDescriptor KeptNotOnBaseMember = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        KeptNotOnBaseMemberMessage,
        MarkNotKeptDescription);

    public static readonly DiagnosticDescriptor KeptNotOnDelegate = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        KeptNotOnDelegateMessage,
        MarkNotKeptDescription);

    public static readonly DiagnosticDescriptor KeepingNotOnInterfaceMember = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        KeepingNotOnInterfaceMemberMessage,
        MarkNotKeptDescription);

    public static readonly DiagnosticDescriptor KeepingNotOnDelegate = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        KeepingNotOnDelegateMessage,
        MarkNotKeptDescription);

    public static readonly DiagnosticDescriptor ImplementationNotChecked = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        ImplementationNotCheckedMessage,
        MarkNotKeptDescription);

    public static readonly DiagnosticDescriptor MovedFromNotOnRefParameter = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        "[MovedFrom] can only be applied to a 'ref' parameter",
        MovedFromDescription);

    public static readonly DiagnosticDescriptor MovedFromOnPrimaryConstructor = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromOnPrimaryConstructorMessage,
        MovedFromDescription);

    public static readonly DiagnosticDescriptor MovedFromPathsNotChecked = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromPathsNotCheckedMessage,
        MovedFromDescription,
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor MovedFromParameterNotAssigned = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromParameterNotAssignedMessage,
        MovedFromDescription);

    public static readonly DiagnosticDescriptor MovedFromParameterReadAgain = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromParameterReadAgainMessage,
        MovedFromDescription);

    public static readonly DiagnosticDescriptor MovedFromParameterNotAssignedAtOnce = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromParameterNotAssignedAtOnceMessage,
        MovedFromDescription);

    public static readonly DiagnosticDescriptor MovedFromParameterReassigned = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromParameterReassignedMessage,
        MovedFromDescription);

    public static readonly DiagnosticDescriptor GenericMoveReturnsSomethingElse = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        GenericMoveReturnsSomethingElseMessage,
        MovedFromDescription);

    public static readonly DiagnosticDescriptor GenericMoveLeavesSomethingElse = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        GenericMoveLeavesSomethingElseMessage,
        MovedFromDescription);

    public static readonly DiagnosticDescriptor MovedFromContainerNotOwned = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromContainerNotOwnedMessage,
        MovedFromDescription);

    public static readonly DiagnosticDescriptor MovedFromContainerExposed = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromContainerExposedMessage,
        MovedFromDescription);

    public static readonly DiagnosticDescriptor SafeListLine = Create(
        DiagnosticIds.SafeListLine,
        "Fix the line of the safe list file",
        "This line of the safe list cannot be parsed: {0}",
        SafeListLineDescription);

    public static readonly DiagnosticDescriptor SafeListNonCopyableNamesNothing = Create(
        DiagnosticIds.SafeListLine,
        "Fix the line of the safe list file",
        SafeListNonCopyableNamesNothingMessage,
        SafeListLineDescription);

    public static readonly DiagnosticDescriptor SafeListCopyableHasNoEffect = Create(
        DiagnosticIds.SafeListLine,
        "Fix the line of the safe list file",
        SafeListCopyableHasNoEffectMessage,
        SafeListLineDescription);

    public static readonly DiagnosticDescriptor NonCopyableAwaiter = Create(
        DiagnosticIds.NonCopyableAwaiter,
        "Do not await with an awaiter of a non-copyable type",
        NonCopyableAwaiterMessage,
        NonCopyableAwaiterDescription);

    public static readonly DiagnosticDescriptor ConditionalMarker = Create(
        DiagnosticIds.ConditionalMarker,
        ConditionalMarkerTitle,
        ConditionalMarkerMessage,
        ConditionalMarkerDescription);

    public static readonly DiagnosticDescriptor ConditionalMarkerBase = Create(
        DiagnosticIds.ConditionalMarker,
        ConditionalMarkerTitle,
        ConditionalMarkerBaseMessage,
        ConditionalMarkerDescription);

    public static readonly DiagnosticDescriptor ConditionalMarkerUse = Create(
        DiagnosticIds.ConditionalMarker,
        ConditionalMarkerTitle,
        ConditionalMarkerUseMessage,
        ConditionalMarkerDescription);

    public static readonly DiagnosticDescriptor ForeignNonCopyableAttribute = Create(
        DiagnosticIds.ForeignNonCopyableAttribute,
        ForeignNonCopyableAttributeTitle,
        ForeignNonCopyableAttributeMessage,
        ForeignNonCopyableAttributeDescription);

    public static readonly DiagnosticDescriptor NonCopyableAttributeWithNoSuffix = Create(
        DiagnosticIds.ForeignNonCopyableAttribute,
        ForeignNonCopyableAttributeTitle,
        NonCopyableAttributeWithNoSuffixMessage,
        ForeignNonCopyableAttributeDescription);

    public static readonly DiagnosticDescriptor ForeignNonCopyableAttributeUse = Create(
        DiagnosticIds.ForeignNonCopyableAttribute,
        ForeignNonCopyableAttributeTitle,
        ForeignNonCopyableAttributeUseMessage,
        ForeignNonCopyableAttributeDescription);

    public static readonly DiagnosticDescriptor NotNullRefMisplaced = Create(
        DiagnosticIds.NotNullRefMisplaced,
        "Put [NotNullRef] on a reference parameter of a method that is not virtual",
        NotNullRefMisplacedMessage,
        NotNullRefDescription);

    public static readonly DiagnosticDescriptor NotNullRefNotChecked = Create(
        DiagnosticIds.NotNullRefNotChecked,
        "Verify a method with a [NotNullRef] parameter, then suppress this finding on it",
        NotNullRefNotCheckedMessage,
        NotNullRefDescription);

    public static readonly DiagnosticDescriptor NestedTooDeeply = Create(
        DiagnosticIds.NestedTooDeeply,
        "Break up code that is nested too deeply to be checked",
        NestedTooDeeplyMessage,
        NestedTooDeeplyDescription);

    public static readonly DiagnosticDescriptor MovedFromParameterNotAssignedAtOnceThroughReference = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromParameterNotAssignedAtOnceThroughReferenceMessage,
        MovedFromDescription);

    public static readonly DiagnosticDescriptor ErasableBaseNotKept = Create(
        DiagnosticIds.ErasableBase,
        "Keep a type that derives from an erasable type erasable",
        ErasableBaseMessage,
        ErasableBaseDescription);

    /// <summary>
    /// Every descriptor of this class, which is what the analyzer supports. A descriptor that is added above is added here: a test
    /// fails if one is missing.
    /// </summary>
    public static readonly ImmutableArray<DiagnosticDescriptor> All = [
        CopyByAssignment,
        CopyByAssignmentIntoVariable,
        CopyByAssignmentIntoForEachVariable,
        CopyByAssignmentIntoSecondVariable,
        CopyByAssignmentForDeconstruct,
        DeconstructionOfStoredValue,
        CopyByAssignmentOutOfBox,
        CopyByReturnOfLivingVariable,
        CopyByReturnOfVariableUsedAfter,
        CopyByReturn,
        UnsupportedUse,
        ValuePassedTwice,
        DelegatesCombined,
        CopyOfStoredValue,
        SwitchStatementMatchesCopy,
        SwitchExpressionWithWhenMatchesCopy,
        PartUsedTwiceInPattern,
        MemberOnReadOnlyReference,
        MemberOnReadOnlyTypeParameter,
        Boxing,
        MemberwiseCloneCall,
        MemberwiseCloneCallOnValue,
        FieldOfCopyableType,
        FieldOfRecord,
        FieldByPrimaryConstructor,
        AutoProperty,
        AnonymousTypeMember,
        PositionalParameterReadByDeconstruct,
        IteratorParameter,
        DefaultInterfaceMember,
        DefaultInterfaceMemberBound,
        AsyncOrIteratorMember,
        UnlistedMember,
        RequiresCopyableMember,
        UntypedCollectionConversion,
        UntypedCollectionVariance,
        UntypedCollectionMember,
        UntypedCollectionReturn,
        UntypedCollectionMethodGroup,
        UntypedCollectionConstraint,
        OpaqueConversion,
        OpaqueVariance,
        OpaqueMember,
        OpaqueReturn,
        OpaqueMethodGroup,
        OpaqueTypeParameterConversion,
        OpaqueConstraint,
        OpaqueBoxing,
        OpaqueLambdaBoxing,
        OpaqueTypeArgument,
        ReadFromStoredContainer,
        CreatedFromStoredContainer,
        OpaqueLambdaResult,
        UnownedContainerReturned,
        DelegateHandsOutStoredContainer,
        DelegateOverTypeParameter,
        HolderReturnedByOverride,
        HolderLostByVariance,
        TypeParameterMarkNotPassedOn,
        TypeParameterMarkNotKept,
        RequiresCopyableNotOnBaseMember,
        RequiresCopyableNotOnBaseCopyConstructor,
        RequiresCopyableOnMemberNeverCalled,
        BorrowedNotOnBaseMember,
        BorrowedNotOnDelegate,
        KeepsUnmarkedParameter,
        ParameterUsedInAWayNotFollowed,
        PassesParameterToUncheckedCode,
        KeepsParameterAfterReturning,
        ContainerMovedFromUnowned,
        ContainerMovedFromSlotParameter,
        ContainerMovedFromSlotParameterOfMethodGroup,
        SlotGivenToOwner,
        MovedFromAdded,
        MovedFromDropped,
        MovedFromDroppedWithNoParameterList,
        KeptNotOnBaseMember,
        KeptNotOnDelegate,
        KeepingNotOnDelegate,
        KeepingNotOnInterfaceMember,
        ImplementationNotChecked,
        MovedFromNotOnRefParameter,
        MovedFromOnPrimaryConstructor,
        MovedFromPathsNotChecked,
        MovedFromParameterNotAssigned,
        MovedFromParameterReadAgain,
        MovedFromParameterNotAssignedAtOnce,
        MovedFromParameterReassigned,
        GenericMoveReturnsSomethingElse,
        GenericMoveLeavesSomethingElse,
        MovedFromContainerNotOwned,
        MovedFromContainerExposed,
        SafeListLine,
        SafeListNonCopyableNamesNothing,
        SafeListCopyableHasNoEffect,
        NonCopyableAwaiter,
        ConditionalMarker,
        ConditionalMarkerBase,
        ConditionalMarkerUse,
        ForeignNonCopyableAttribute,
        NonCopyableAttributeWithNoSuffix,
        ForeignNonCopyableAttributeUse,
        ErasableBaseNotKept,
        NotNullRefMisplaced,
        NotNullRefNotChecked,
        MovedFromParameterNotAssignedAtOnceThroughReference,
        NestedTooDeeply,
    ];

    private static DiagnosticDescriptor Create(string id, string title, string messageFormat, string description, params string[] customTags) => new(
        id, title, messageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description, HelpLinkBaseUri + id.ToLowerInvariant() + ".html", customTags);
}
