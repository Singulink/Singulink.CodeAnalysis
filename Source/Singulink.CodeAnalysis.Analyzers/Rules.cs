using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// The rules of the diagnostics that <see cref="DoNotCopyValueAnalyzer"/> reports, each with its descriptors. An ID can have more than one
/// message, and each message has a descriptor for every severity that it can be reported with: see <see cref="RuleGroup"/>.
/// </summary>
internal static class Rules
{
    private const string Category = "Reliability";

    // The article of each diagnostic on the documentation site is named by its ID in lower case. The address is fixed from the first release on.
    private const string HelpLinkBaseUri = "https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/diagnostics/";

    private const string CopyByAssignmentTitle = "Do not copy a non-copyable value by assignment";
    private const string CopyByReturnTitle = "Do not copy a non-copyable value by returning it";
    private const string MemberwiseCloneTitle = "Do not call MemberwiseClone on what holds a non-copyable value";
    private const string CloningBaseTitle = "Do not hold or bind a non-copyable value in a class whose base class clones itself";
    private const string FieldOfCopyableTypeTitle = "Do not declare a field of a non-copyable type in a copyable struct or a record";
    private const string UnsupportedUseTitle = "Do not use a non-copyable value where it may be copied";
    private const string UntypedCollectionTitle = "Do not use a container of non-copyable values as an untyped collection";
    private const string OpaqueTypeTitle = "Do not use a container of non-copyable values as a type that hides them";
    private const string MarkNotKeptTitle =
        "Keep a [NonCopyable] mark, do not add [RequiresCopyable], [Borrowed], [Kept] or UnsafeToCopy = false, and neither add nor drop " +
        "[MovedFrom], against a base member or a delegate";
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

    private const string UsedAfterHandOffTitle = "Do not use a variable after its non-copyable value was handed over";

    private const string UsedAfterHandOffMessage =
        "'{0}' is used after {2}, so that hand-off was a copy of non-copyable type '{1}'. Use '{0}' only before the hand-off, pass it by " +
        "reference, or hand over NonCopyable.Move(ref {0}).";

    private const string UsedAfterUsingMessage =
        "'{0}' is used after a 'using' statement took a copy of its value of non-copyable type '{1}', which the statement disposes at its " +
        "end while '{0}' still holds the value. Declare the variable in the statement, as in 'using (var {0} = ...)', or use a 'using' " +
        "declaration with a new value, as in 'using var {0} = ...'.";

    private const string UsedAfterHandOffInHandlerMessage =
        "'{0}' is used after {2}, so that hand-off was a copy of non-copyable type '{1}': a 'catch', a 'finally' or an exception filter " +
        "that uses '{0}' can still run after the hand-off. Use '{0}' only before the hand-off, or, where a 'catch' uses it, store the value " +
        "at the end of the 'try' block, after everything that can throw.";

    private const string UsedAfterHandOffBeforeNullReferenceMessage =
        "'{0}' is used after {2}, so that hand-off was a copy of non-copyable type '{1}': the 'try' block then reads or writes through " +
        "'{3}', which can be a null reference, so this can still run. Check the reference first, with NonCopyable.ThrowIfNullRef(in {3}) " +
        "or with a method whose parameter is marked [NotNullRef], which counts only in an assembly that was built with the analyzer.";

    private const string UsedAfterHandOffDescription =
        "Reading a local or a by-value parameter by value moves its value where nothing uses the variable again, its last use. Where the " +
        "variable is used after the read, on any path that the code or an exception can take, the read was a copy, and the first use " +
        "after it on each path is reported, with the hand-off as an additional location. A 'catch', a 'finally' and an exception filter " +
        "that use the variable count as uses after a read in the 'try' block, except where the value is stored at the end of the 'try' " +
        "block and nothing that can throw follows.";

    private const string UnsupportedUseDescription =
        "A value of a non-copyable type can only be used where it is known not to be copied. This use may copy it, or it is one the analyzer does " +
        "not know to be safe.";

    private const string SwitchStatementMatchesCopyMessage =
        "The pattern reads from a copy of the value of non-copyable type '{0}': a 'switch' statement matches a copy of what it switches " +
        "on, even where that is a local or a parameter. An 'is' pattern and a 'switch' expression without 'when' match it in place: use " +
        "one of those, or move the value into the 'switch' with NonCopyable.Move.";

    private const string SwitchExpressionWithWhenMatchesCopyMessage =
        "The pattern reads from a copy of the value of non-copyable type '{0}': a 'when' in a 'switch' expression makes every arm match " +
        "a copy of what it switches on, even where that is a local or a parameter. An 'is' pattern and a 'switch' expression without " +
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
        "stored in a field. A field that holds a reference to a container does not count: the clone shares the container. A class whose " +
        "base class clones itself in code that the analyzer cannot read gets SCA1009 instead.";

    private const string CloningBaseDescription =
        "A class that the symbol list marks 'clones' clones its objects field by field with MemberwiseClone in code that the analyzer " +
        "cannot read, and the clone copies every field of a class that derives from it. So a class of the compilation that derives from " +
        "one is reported on each field that holds a non-copyable value, and a non-copyable type bound to a type parameter of such a " +
        "class is reported where it is bound, as a base class or in an object creation. Where the binding is in the type of a value, as in a " +
        "'Tagged<Guard>' that a call returns, or in the base list of a class of another assembly, the value is reported wherever it is " +
        "used as a type that hands it to the cloning code: the receiver of a member of the cloning class or of a class above it, a " +
        "conversion to one of those or to an interface, and a call of a member that clones. A base class of an assembly that was not built " +
        "with the analyzer, and that is no part of the framework, may clone itself too, and nothing tells the analyzer: a class of the " +
        "compilation that derives from one is reported on each field that holds a non-copyable value until a symbol list line says " +
        "what the base class does. 'clones' says that it clones itself, 'clones=none' that neither it nor a class above it does, and a " +
        "'trusted' line for its assembly that no code of the assembly copies a non-copyable value.";

    private const string FieldBehindUnknownBaseMessage =
        "'{0}' derives from '{1}' of assembly '{2}', which was not built with the analyzer, and no symbol list line says whether its code " +
        "clones an object field by field with MemberwiseClone. A clone would copy the {3} of type '{4}' of '{0}', which holds a non-copyable " +
        "value. Add '{5};clones' to a NonCopyableSymbols.txt file if '{1}' clones itself, '{5};clones=none' if neither it nor a class above " +
        "it does, or 'A:{2};trusted' if no code of the assembly copies a non-copyable value. Or hold the value in an object of a class that " +
        "'{0}' refers to, which a clone shares.";

    // A message that advises a suppression has a second text for the current memory safety rules, under which a suppression can be
    // anywhere. The first text is that of a report about a type that is unsafe to copy under the updated rules, where only code in an
    // unsafe context and a declaration marked 'unsafe' or 'safe' can suppress it: a report at a declaration says that marking the
    // declaration 'safe' answers for it. Every other report has the second text under both rule sets.
    private const string MemberwiseCloneOfTypeParameterThroughListedBaseMessage =
        "'{0}' derives from '{1}', whose own code clones an object field by field with MemberwiseClone, so a clone of an object of '{0}' " +
        "copies its {2} of type '{3}' with the non-copyable value that a project binds to its type parameter. Hold the value in an object " +
        "of a class that '{0}' refers to, which the clone shares. Where the code of '{0}' never clones its objects, mark the declaration " +
        "'safe' and suppress the warning there: a project that binds a non-copyable type gets a report of its own where it hands such an " +
        "object to the code of '{1}'.";

    private const string MemberwiseCloneOfTypeParameterThroughListedBaseCurrentRulesMessage =
        "'{0}' derives from '{1}', whose own code clones an object field by field with MemberwiseClone, so a clone of an object of '{0}' " +
        "copies its {2} of type '{3}' with the non-copyable value that a project binds to its type parameter. Suppress this warning only " +
        "where the code of '{0}' never clones its objects: a project that binds a non-copyable type gets a report of its own where it hands " +
        "such an object to the code of '{1}'. Or hold the value in an object of a class that '{0}' refers to, which the clone shares.";

    private const string CloningBindingMessage =
        "Non-copyable type '{0}' is bound to a type parameter of '{1}', whose objects the own code of '{2}' clones field by field with " +
        "MemberwiseClone, so a clone copies the '{0}' that it holds. Bind a class that refers to the value instead, which a clone shares.";

    private const string CloningBindingInBaseListMessage =
        "Non-copyable type '{0}' is bound to a type parameter of '{1}', whose objects the own code of '{2}' clones field by field with " +
        "MemberwiseClone, so a clone copies the '{0}' that it holds. Bind a class that refers to the value instead, which a clone shares. " +
        "Where no object of '{1}' is ever cloned, mark the declaration of the class 'safe' and suppress the warning there.";

    private const string CloningBindingCurrentRulesMessage =
        "Non-copyable type '{0}' is bound to a type parameter of '{1}', whose objects the own code of '{2}' clones field by field with " +
        "MemberwiseClone, so a clone copies the '{0}' that it holds. Bind a class that refers to the value instead, which a clone shares, or " +
        "suppress this warning where no object of '{1}' is ever cloned.";

    private const string CloningUseConversionMessage =
        "'{0}' is converted to '{1}', which hands the object, or what holds it, to code that can clone it: the own code of '{2}' clones its " +
        "objects field by field with MemberwiseClone, and a clone copies the non-copyable '{3}' that is bound to a type parameter of '{4}'. " +
        "Keep the value as '{0}' or bind a class that refers to the value instead, which a clone shares. Code that never clones the object " +
        "can convert it in an unsafe context and suppress the warning there.";

    private const string CloningUseReturnMessage =
        "'{0}' is converted to '{1}', which hands the object, or what holds it, to code that can clone it: the own code of '{2}' clones its " +
        "objects field by field with MemberwiseClone, and a clone copies the non-copyable '{3}' that is bound to a type parameter of '{4}'. " +
        "Keep the value as '{0}' or bind a class that refers to the value instead, which a clone shares. Where callers never clone the " +
        "object, mark the override 'safe' and suppress the warning there.";

    private const string CloningUseConversionCurrentRulesMessage =
        "'{0}' is converted to '{1}', which hands the object, or what holds it, to code that can clone it: the own code of '{2}' clones its " +
        "objects field by field with MemberwiseClone, and a clone copies the non-copyable '{3}' that is bound to a type parameter of '{4}'. " +
        "Keep the value as '{0}', bind a class that refers to the value instead, which a clone shares, or suppress this warning where the " +
        "object is never cloned.";

    private const string CloningUseMemberMessage =
        "'{0}' uses '{1}' as '{2}', which hands the object to code that can clone it: the own code of '{3}' clones its objects field by " +
        "field with MemberwiseClone, and a clone copies the non-copyable '{4}' that is bound to a type parameter of '{5}'. Bind a class that " +
        "refers to the value instead, which a clone shares, or add '{6}' to a NonCopyableSymbols.txt file if the member does not clone the " +
        "object. Code that never clones the object can use '{0}' in an unsafe context and suppress the warning there.";

    private const string CloningUseMemberCurrentRulesMessage =
        "'{0}' uses '{1}' as '{2}', which hands the object to code that can clone it: the own code of '{3}' clones its objects field by " +
        "field with MemberwiseClone, and a clone copies the non-copyable '{4}' that is bound to a type parameter of '{5}'. Bind a class that " +
        "refers to the value instead, which a clone shares, add '{6}' to a NonCopyableSymbols.txt file if the member does not clone the " +
        "object, or suppress this warning where the object is never cloned.";

    private const string CloningUseOfCloningMemberMessage =
        "'{0}' clones its object field by field with MemberwiseClone, or hands it to code that can, so a clone of '{1}' can copy the " +
        "non-copyable '{2}' that is bound to a type parameter of '{3}', and no line of a symbol list file can vouch for the member. Bind a " +
        "class that refers to the value instead, which a clone shares, or avoid the use.";

    private const string CloningUseHandsBackMessage =
        "'{0}' can hand the object of '{1}' to other code as '{2}', which no longer shows its binding: the own code of '{3}' clones its " +
        "objects field by field with MemberwiseClone, and a clone copies the non-copyable '{4}' that is bound to a type parameter of '{5}'. " +
        "Bind a class that refers to the value instead, which a clone shares, or add '{6}' to a NonCopyableSymbols.txt file if the member " +
        "never hands the object itself to other code.";

    private const string CloningUseOfUnknownMemberMessage =
        "'{0}' is a member of '{2}', and no line says 'clones=complete' for '{3}', so the member can clone the object of '{1}': the own " +
        "code of '{3}' clones its objects field by field with MemberwiseClone, and a clone copies the non-copyable '{4}' that is bound to a " +
        "type parameter of '{5}'. Bind a class that refers to the value instead, which a clone shares, add '{6}' to a NonCopyableSymbols.txt " +
        "file if the member neither clones the object nor hands it on, or add '{7};clones=complete' to one once the 'clones' lines of '{3}' " +
        "name every member that clones the object or hands it on.";

    private const string CloningUseOfMemberOfUncoveredClassMessage =
        "'{0}' is a member of '{2}', a class of another assembly than '{3}' that was not built with the analyzers and that no line that " +
        "says 'clones=complete' covers, so the member can clone the object of '{1}': the own code of '{3}' clones its objects field by " +
        "field with MemberwiseClone, and a clone copies the non-copyable '{4}' that is bound to a type parameter of '{5}'. Bind a class that " +
        "refers to the value instead, which a clone shares, add '{6}' to a NonCopyableSymbols.txt file if the member neither clones the " +
        "object nor hands it on, or add '{7};clones=complete' to one once the 'clones' lines of '{2}' name every member that clones the " +
        "object or hands it on.";

    private const string CloningUseRunsMemberThatClonesMessage =
        "'{0}' is a member of '{3}', whose code can run '{2}' on the object of '{1}', itself or through another member that it runs: '{2}' " +
        "is an override, or implements a member of an interface, in a class below '{3}', and its 'clones' line says that it clones the " +
        "object or hands it to code that can. A clone copies the non-copyable '{4}' that is bound to a type parameter of '{5}', and no " +
        "line of a symbol list file can vouch for the use. Bind a class that refers to the value instead, which a clone shares, or avoid " +
        "the use.";

    private const string CloningUseRunsOverrideOfCloningClassMessage =
        "'{0}' is a member of '{3}', whose code can run '{2}' on the object of '{1}', itself or through another member that it runs: '{2}' " +
        "overrides a member that '{3}' has or inherits, and its 'clones' line says that it clones the object or hands it to code that can. " +
        "A clone copies the non-copyable '{4}' that is bound to a type parameter of '{5}'. Bind a class that refers to the value instead, " +
        "which a clone shares, add '{6};clones' to a NonCopyableSymbols.txt file if the member runs it, or add '{6}' if it neither clones " +
        "the object nor hands it on.";

    private const string CloningUseRunsUnknownMemberMessage =
        "'{0}' is a member of '{4}', whose code can run '{2}' on the object of '{1}', itself or through another member that it runs: '{2}' " +
        "is an override, or implements a member of an interface, in '{3}', a class below '{4}' of an assembly that was not built with the " +
        "analyzers and that no line that says 'clones=complete' covers, so it can clone the object. The own code of '{5}' clones its " +
        "objects field by field with MemberwiseClone, and a clone copies the " +
        "non-copyable '{6}' that is bound to a type parameter of '{7}'. Bind a class that refers to the value instead, which a clone " +
        "shares, add '{8}' to a NonCopyableSymbols.txt file if that member neither clones the object nor hands it on, or add " +
        "'{9};clones=complete' to one once the 'clones' lines of '{3}' name every member that clones the object or hands it on.";

    private const string MemberClonesWithoutLineMessage =
        "'{0}' clones its object through '{1}', and an object of '{2}' can hold a non-copyable value that another project derives or " +
        "binds, where nothing shows the clone. Add '{3};clones' to the symbol list file that ships with the library, so that a call of " +
        "'{0}' on such an object is reported where it is made{4}.";

    private const string CloningUseTypeArgumentMessage =
        "'{0}' binds '{1}' to type parameter '{2}', which is not marked [NonCopyable], so the generic can use the object as a type that " +
        "hands it to code that can clone it: the own code of '{3}' clones its objects field by field with MemberwiseClone, and a clone " +
        "copies the non-copyable '{4}' that is bound to a type parameter of '{5}'. Bind a class that refers to the value instead, which a " +
        "clone shares. Code that never clones the object can use '{0}' in an unsafe context and suppress the warning there.";

    private const string CloningUseTypeArgumentCurrentRulesMessage =
        "'{0}' binds '{1}' to type parameter '{2}', which is not marked [NonCopyable], so the generic can use the object as a type that " +
        "hands it to code that can clone it: the own code of '{3}' clones its objects field by field with MemberwiseClone, and a clone " +
        "copies the non-copyable '{4}' that is bound to a type parameter of '{5}'. Bind a class that refers to the value instead, which a " +
        "clone shares, or suppress this warning where the object is never cloned.";

    private const string CloningUseConstraintMessage =
        "'{0}' binds '{1}' to type parameter '{2}', whose constraint '{3}' lets the generic use the object as a type that hands it to code " +
        "that can clone it: the own code of '{4}' clones its objects field by field with MemberwiseClone, and a clone copies the " +
        "non-copyable '{5}' that is bound to a type parameter of '{6}'. Bind a class that refers to the value instead, which a clone shares. " +
        "Code that never clones the object can use '{0}' in an unsafe context and suppress the warning there.";

    private const string CloningUseConstraintCurrentRulesMessage =
        "'{0}' binds '{1}' to type parameter '{2}', whose constraint '{3}' lets the generic use the object as a type that hands it to code " +
        "that can clone it: the own code of '{4}' clones its objects field by field with MemberwiseClone, and a clone copies the " +
        "non-copyable '{5}' that is bound to a type parameter of '{6}'. Bind a class that refers to the value instead, which a clone shares, " +
        "or suppress this warning where the object is never cloned.";

    private const string UnlistedMemberThatClonesMessage =
        "'{0}' may copy non-copyable type '{1}': type parameter '{2}' is not marked [NonCopyable], and the member clones its object field by " +
        "field with MemberwiseClone, so no line of a symbol list file can vouch for it. Avoid the use.";

    private const string OpaqueMemberThatClonesMessage =
        "'{2}' uses container '{0}' as '{1}', which hides the non-copyable type that it mentions from the analyzer, and '{2}' clones its " +
        "object field by field with MemberwiseClone, so no line of a symbol list file can vouch for it. Avoid the use, or hold the values in an object of a " +
        "class that '{0}' refers to.";

    private const string SafeListNamesAccessorMessage =
        "This line of the symbol list file names '{0}', an accessor of '{1}'. It has no effect, because the analyzer looks up every use " +
        "of an accessor under its property or event: write the line for '{1}' instead.";

    private const string SafeListContradictsClonesMessage =
        "This line of the symbol list file says that '{0}' makes no copy, and a 'clones' line says that it clones its object field by field " +
        "with MemberwiseClone. The line is left out: remove it.";

    private const string MemberwiseCloneThroughListedBaseMessage =
        "'{0}' derives from '{1}', whose own code clones an object field by field with MemberwiseClone, so a clone of an object of '{0}' " +
        "copies its {2} of non-copyable type '{3}'. Hold the value in an object of a class that '{0}' refers to, which the clone shares. " +
        "Where no object of '{0}' is ever cloned, mark the declaration 'safe' and suppress the warning there.";

    private const string MemberwiseCloneThroughListedBaseCurrentRulesMessage =
        "'{0}' derives from '{1}', whose own code clones an object field by field with MemberwiseClone, so a clone of an object of '{0}' " +
        "copies its {2} of non-copyable type '{3}'. Hold the value in an object of a class that '{0}' refers to, which the clone shares, " +
        "or suppress this warning where no object of '{0}' is ever cloned.";

    private const string GetObjectValueDescription =
        "RuntimeHelpers.GetObjectValue returns a clone of a boxed struct, field by field, so it copies every non-copyable value that the " +
        "struct holds; for any other object it returns the object itself. Every use of it is reported, a call and a method group alike, " +
        "whatever the type of its argument: nobody calls it on something that cannot be a boxed struct.";

    private const string MemberwiseCloneOfOpenClassMessage =
        "MemberwiseClone of '{0}' clones an object of whatever class derives from it, and '{0}' can be derived from in another assembly, " +
        "where a derived class can hold a non-copyable value that the clone copies. Seal the class or copy its fields in a constructor " +
        "instead. Where no derived class may hold a non-copyable value, call MemberwiseClone in an unsafe context and suppress the warning " +
        "there. To keep the class open and cloneable in a library, add '{1};clones' to the symbol list file that ships with the library, so " +
        "that a class that derives from it in another project is reported for a non-copyable value, and then suppress the warning in " +
        "the same way.";

    private const string MemberwiseCloneOfOpenClassCurrentRulesMessage =
        "MemberwiseClone of '{0}' clones an object of whatever class derives from it, and '{0}' can be derived from in another assembly, " +
        "where a derived class can hold a non-copyable value that the clone copies. Seal the class, copy its fields in a constructor " +
        "instead, or suppress this warning as a rule that no derived class may hold a non-copyable value. To keep the class open and " +
        "cloneable in a library, add '{1};clones' to the symbol list file that ships with the library, so that a class that derives from " +
        "it in another project is reported for a non-copyable value, and then suppress this warning.";

    private const string MemberwiseCloneOfOpenClassDescription =
        "MemberwiseClone clones the object that it is called on, whatever its class, so a call on a class with no field of a non-copyable " +
        "type copies one of a derived class all the same. A class that can be derived from in another assembly can have such a derived " +
        "class that the analyzer does not see: it, or a class of the compilation that derives from it, is not sealed, is reachable from " +
        "outside its assembly, and has a constructor that a derived class there can call. An assembly with InternalsVisibleTo counts its internal types and constructors as reachable. A derived " +
        "class in the same compilation that has such a field is reported as a definite copy (SCA1005).";

    private const string FieldOfRecordDescription =
        "A record copies its fields in 'with', in its copy constructor and in the members that the compiler writes for it (Equals, GetHashCode " +
        "and ToString), so a record, class or struct, cannot have a field of a non-copyable type. Use a class or a struct that is not a record.";

    private const string FieldOfCopyableTypeDescription =
        "Copying a struct copies its fields, so a field of a non-copyable type cannot be a member of a copyable struct. Mark the containing struct " +
        "non-copyable too, make it a class, or remove the field.";

    private const string FieldByPrimaryConstructorMessage =
        "Parameter '{1}' of the primary constructor of copyable struct '{2}' cannot have non-copyable type '{0}': members of the struct use it, " +
        "which makes it a field";

    private const string FieldOfCopyableTypeUnsafeToCopyMessage =
        "Copyable field '{1}' cannot have non-copyable type '{0}', which is unsafe to copy. Mark '{2}' [NonCopyable(UnsafeToCopy = true)], " +
        "make it a class, or remove the field.";

    private const string FieldByPrimaryConstructorUnsafeToCopyMessage =
        "Parameter '{1}' of the primary constructor of copyable struct '{2}' cannot have non-copyable type '{0}', which is unsafe to copy: " +
        "members of the struct use it, which makes it a field. Mark '{2}' [NonCopyable(UnsafeToCopy = true)], or use the parameter only in " +
        "initializers.";

    private const string UnsafeToCopyFieldTitle = "Mark a struct that holds a field of a type that is unsafe to copy UnsafeToCopy = true";

    private const string UnsafeToCopyFieldMessage =
        "Field '{1}' has type '{0}', which is unsafe to copy, and a copy of struct '{2}' copies the field. Mark '{2}' " +
        "[NonCopyable(UnsafeToCopy = true)].";

    private const string UnsafeToCopyFieldByPrimaryConstructorMessage =
        "Parameter '{1}' of the primary constructor of struct '{2}' has type '{0}', which is unsafe to copy: members of the struct use it, " +
        "which makes it a field, and a copy of '{2}' copies the field. Mark '{2}' [NonCopyable(UnsafeToCopy = true)], or use the " +
        "parameter only in initializers.";

    private const string UnsafeToCopyFieldDescription =
        "A copy of a struct copies its fields, so a struct that holds a field of a type that is unsafe to copy is unsafe to copy as well. " +
        "A struct says that itself, with [NonCopyable(UnsafeToCopy = true)], and is what its attribute says until it does. A field whose " +
        "type is a type parameter of the struct, or a type that is unsafe to copy only through one, asks nothing of the struct: each " +
        "constructed type of the struct is judged by its type arguments.";

    private const string UnsafeToCopyNotTakenTitle =
        "Do not use a type that is unsafe to copy, or a container of one, for a type parameter marked [NonCopyable(UnsafeToCopy = false)]";

    private const string UnsafeToCopyNotTakenMessage =
        "'{0}' does not take '{1}' for type parameter '{2}', which is marked [NonCopyable(UnsafeToCopy = false)]: '{1}' is unsafe to copy. " +
        "Use a type that is not unsafe to copy, or a generic whose type parameter takes any non-copyable type.";

    private const string UnsafeToCopyReachedNotTakenMessage =
        "'{0}' does not take '{1}' for type parameter '{2}', which is marked [NonCopyable(UnsafeToCopy = false)]: through '{1}' other code " +
        "can reach a value that is unsafe to copy. Use a type through which no such value can be reached, or a generic whose type " +
        "parameter takes any non-copyable type.";

    private const string UnsafeToCopyTypeParameterNotTakenMessage =
        "'{0}' does not take type parameter '{1}' for type parameter '{2}', which is marked [NonCopyable(UnsafeToCopy = false)]: '{1}' can " +
        "stand for a type that is unsafe to copy. Mark '{1}' [NonCopyable(UnsafeToCopy = false)] as well, or use a generic whose type " +
        "parameter takes any non-copyable type.";

    private const string UnsafeToCopyNotTakenBehindBoxMessage =
        "Boxing '{0}' as '{1}' lets other code run the members of '{2}' on the box with '{3}' for type parameter '{4}', which is marked " +
        "[NonCopyable(UnsafeToCopy = false)] and does not take '{3}', since it is unsafe to copy. Keep the value as '{0}'.";

    private const string UnsafeToCopyReachedNotTakenBehindBoxMessage =
        "Boxing '{0}' as '{1}' lets other code run the members of '{2}' on the box with '{3}' for type parameter '{4}', which is marked " +
        "[NonCopyable(UnsafeToCopy = false)] and does not take '{3}', since a value that is unsafe to copy can be reached through it. Keep " +
        "the value as '{0}'.";

    private const string UnsafeToCopyNotTakenBehindTypeParameterMessage =
        "'{1}' is bound to type parameter '{2}' of '{0}', so the generic can run the members of '{3}' with '{4}' for type parameter '{5}', " +
        "which is marked [NonCopyable(UnsafeToCopy = false)] and does not take '{4}', since it is unsafe to copy. Keep the value out of '{2}'.";

    private const string UnsafeToCopyReachedNotTakenBehindTypeParameterMessage =
        "'{1}' is bound to type parameter '{2}' of '{0}', so the generic can run the members of '{3}' with '{4}' for type parameter '{5}', " +
        "which is marked [NonCopyable(UnsafeToCopy = false)] and does not take '{4}', since a value that is unsafe to copy can be reached " +
        "through it. Keep the value out of '{2}'.";

    private const string UnsafeToCopyNotTakenInFieldMessage =
        "The {0} of struct '{1}' holds '{2}': the Equals and GetHashCode that '{1}' inherits from ValueType run the members of '{3}' with " +
        "'{4}' for type parameter '{5}', which is marked [NonCopyable(UnsafeToCopy = false)] and does not take '{4}', since it is unsafe " +
        "to copy. Use a type that is not unsafe to copy, or override Equals and GetHashCode in '{1}'.";

    private const string UnsafeToCopyReachedNotTakenInFieldMessage =
        "The {0} of struct '{1}' holds '{2}': the Equals and GetHashCode that '{1}' inherits from ValueType run the members of '{3}' with " +
        "'{4}' for type parameter '{5}', which is marked [NonCopyable(UnsafeToCopy = false)] and does not take '{4}', since a value that is " +
        "unsafe to copy can be reached through it. Use a type through which no such value can be reached, or override Equals and " +
        "GetHashCode in '{1}'.";

    private const string UnsafeToCopyNotTakenDescription =
        "A generic whose type parameter is marked [NonCopyable(UnsafeToCopy = false)] treats it as a plain non-copyable type, so a copy of " +
        "its values inside the generic, and a use that hands them to other code, gets the ordinary warning, which its author may " +
        "suppress. Such a generic takes no type through which a value that is unsafe to copy can be reached: no type that is unsafe to " +
        "copy, and no container of one, tuple or struct that carries one, or delegate over one. A type parameter that is bound to it " +
        "counts as unsafe to copy unless it is marked so as well. The binding is " +
        "reported where the generic is used: a member of it, a base type or an interface, a constructor that the 'new()' constraint " +
        "lets other code call, a type over it that is given to other generic code, which can run its members, a struct over it that is " +
        "boxed, and a field of a struct that holds it inline, whose inherited Equals and GetHashCode run its members, where its members " +
        "run with no use that shows them.";

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
        "what it changes is lost, and it runs later, on a copy that is out of date by then, even where the member is 'readonly'. Do the work " +
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
        "for the record reads the member by value, even where the record declares it as a property that returns a reference. Declare " +
        "the Deconstruct method yourself, or give the type an ordinary constructor.";

    private const string AutoPropertyDescription =
        "The getter of an auto-property returns a copy of its backing field, so an auto-property cannot have a non-copyable type. Use a field instead.";

    private const string UnlistedMemberMessage =
        "'{0}' may copy non-copyable type '{1}': type parameter '{2}' is not marked [NonCopyable] and the member is not on the symbol list. Mark the " +
        "type parameter, add '{3}' to a NonCopyableSymbols.txt file if the member makes no copy, or avoid the use.";

    private const string MemberListedForOtherTypeParameterMessage =
        "'{0}' may copy non-copyable type '{1}': type parameter '{2}' is not marked [NonCopyable] in a build with the analyzer, and the " +
        "symbol list names the member for '{3}' only, which says nothing of what it does with a value of '{2}'. Bind a copyable type to " +
        "'{2}', or avoid the use.";

    private const string UnlistedMemberOfUncheckedCodeMessage =
        "'{0}' may copy non-copyable type '{1}': type parameter '{2}' is marked [NonCopyable], but the mark does not count in an assembly " +
        "that was not built with the analyzer, and the member is not on the symbol list. Add '{3}' to a NonCopyableSymbols.txt file if the " +
        "member makes no copy, or avoid the use.";

    // Reading or writing a static field of a generic type runs the static constructor and the static field initializers of the type with
    // its type arguments, which is a use of the type's code as a call of one of its members is.
    private const string UnlistedStaticFieldMessage =
        "Reading or writing static field '{0}' runs the static constructor and the static field initializers of its type, which may copy " +
        "non-copyable type '{1}': type parameter '{2}' is not marked [NonCopyable] and the field is not on the symbol list. Mark the type " +
        "parameter, add '{3}' to a NonCopyableSymbols.txt file if that code makes no copy, or avoid the use.";

    private const string UnlistedStaticFieldOfUncheckedCodeMessage =
        "Reading or writing static field '{0}' runs the static constructor and the static field initializers of its type, which may copy " +
        "non-copyable type '{1}': type parameter '{2}' is marked [NonCopyable], but the mark does not count in an assembly that was not " +
        "built with the analyzer, and the field is not on the symbol list. Add '{3}' to a NonCopyableSymbols.txt file if that code makes no " +
        "copy, or avoid the use.";

    private const string UncheckedMemberMessage =
        "'{0}' is a member of an assembly that was not built with the analyzer, and gets at a value of a non-copyable type other than by " +
        "value, through its parameter '{1}' of type '{2}': nothing checks what it does with that value. If the member uses what it is " +
        "given during the call and keeps nothing of it, add '{3};borrows' to a NonCopyableSymbols.txt file; otherwise avoid the use.";

    private const string UncheckedMemberDescription =
        "The code of an assembly that was not built with the analyzer is not checked. A value that is passed to it by value is moved, and " +
        "is its business from then on. A member that gets at a value of a non-copyable type in any other way, by 'ref', 'in' or 'ref " +
        "readonly', inside a container, inside a view or through a pointer, can copy it, so it can be used only where a list names it " +
        "'borrows': it uses what it is given during the call and keeps nothing of it. A member of the non-copyable type itself is trusted " +
        "with its receiver and with values of its own type.";

    private const string UncheckedAssemblyMessage =
        "Assembly '{0}' was not built with the analyzer, and it references '{1}' and with it non-copyable type '{2}': its code can copy a " +
        "value of that type where it implements an interface, makes a delegate or reads an object of a type of checked code, which no " +
        "call shows. Build '{0}' with the analyzer, or, if its code makes no copy of the non-copyable values that it gets at, vouch for it " +
        "with the line 'A:{0};trusted' in a NonCopyableSymbols.txt file.";

    private const string UncheckedAssemblyOfExposedValueMessage =
        "Assembly '{0}' was not built with the analyzer, and it references '{1}', where '{2}' lets other code get at a non-copyable " +
        "value: its code can copy such a value where it implements an interface, makes a delegate or reads an object of a type of " +
        "checked code, which no call shows. Build '{0}' with the analyzer, or, if its code makes no copy of the non-copyable values that it " +
        "gets at, vouch for it with the line 'A:{0};trusted' in a NonCopyableSymbols.txt file.";

    private const string UncheckedFriendAssemblyMessage =
        "Assembly '{0}' was not built with the analyzer, and '{1}' names it in an InternalsVisibleTo attribute, so it can use the internal " +
        "members of '{1}', which this build does not see: its code can copy a non-copyable value that it gets at there, which no call " +
        "shows. Build '{0}' with the analyzer, or, if its code makes no copy of the non-copyable values that it gets at, vouch for it " +
        "with the line 'A:{0};trusted' in a NonCopyableSymbols.txt file.";

    private const string UncheckedReferenceAssemblyMessage =
        "Assembly '{0}' was not built with the analyzer, and this build is given only its reference assembly, which does not show what its " +
        "code uses: its code can use '{1}', which {2}, and copy a non-copyable value that it gets at there, which no call shows. Build '{0}' with the " +
        "analyzer, or, if its code makes no copy of the non-copyable values that it gets at, vouch for it with the line 'A:{0};trusted' in " +
        "a NonCopyableSymbols.txt file.";

    private const string UncheckedAssemblyDescription =
        "A call is judged by the member that it binds to, so code of an assembly that was not built with the analyzer and that runs behind " +
        "a type of checked code or of the framework is not seen: an implementation of an interface or of an abstract class, a delegate or a " +
        "function pointer that it made, an object of a class that holds a non-copyable value. Such code can use a non-copyable type only " +
        "where its assembly references the assembly that declares it, so that reference is reported, once for each assembly. A type " +
        "counts that a [NonCopyable] mark or a 'noncopyable' line of a symbol list file makes non-copyable, and that no 'copyable' line takes " +
        "out. The built-in non-copyable types of the framework are declared where every assembly references them, so for them, and for " +
        "what is bound to a marked type parameter, the reference that counts is one to an assembly of checked code whose members let " +
        "other code get at such a value: a field, a member that returns a reference or a container, or a member that other code can " +
        "implement and that is given one. The internal members of an assembly are not seen where it is referenced, so an assembly that " +
        "checked code names in an InternalsVisibleTo attribute is reported whatever that code holds. A reference assembly, which a " +
        "build is given for a project of the same solution, names only the assemblies that its reachable members use, so what its code " +
        "uses is not known: such an assembly is reported where another assembly that it can reference declares a non-copyable type " +
        "or lets a value out. A line 'A:<name>;trusted' of a symbol list file vouches for one assembly.";

    private const string SafeListTrustedHasNoEffectMessage =
        "This line of the symbol list file vouches for assembly '{0}'. It has no effect, because {1}.";

    private const string UnlistedMemberDescription =
        "Generic code whose type parameter is not marked [NonCopyable] is not checked, so it is assumed to copy the values it handles. Its members " +
        "can only be used with a non-copyable type if the symbol list names them. If the generic is yours, mark its type parameter and fix what the " +
        "analyzer then warns about inside it. If it is not, and the member makes no copy other than the value it hands out, add its documentation " +
        "comment ID to an additional file named NonCopyableSymbols.txt.";

    private const string RequiresCopyableMemberDescription =
        "A member marked [RequiresCopyable] copies values of the type parameters that are marked [NonCopyable], so it cannot be used when one of " +
        "them is a non-copyable type.";

    private const string UntypedCollectionConversionMessage =
        "Container {0} is converted to untyped collection '{1}', which hands out copies of its elements. Keep the element type visible, for " +
        "example with a generic interface, or pass the container only to a member that the symbol list marks 'borrows'.";

    private const string UntypedCollectionVarianceMessage =
        "The conversion of '{2}' to '{3}' turns container '{0}' into untyped collection '{1}' for the code behind the reference, which hands " +
        "out copies of its elements. Variance converts a type argument with no conversion written. Keep the type arguments the same.";

    private const string UntypedCollectionMemberMessage =
        "'{2}' uses container '{0}' as untyped collection '{1}', which hands out copies of its elements, and is not on the symbol list. Add '{3}' " +
        "to a NonCopyableSymbols.txt file if the member makes no copy, or avoid the use.";

    private const string UntypedCollectionDescription =
        "System.Array and the non-generic collection interfaces hand out boxed copies of the elements. A container of non-copyable values can " +
        "only be used as one of them by a member that the symbol list names.";

    private const string OpaqueConversionMessage =
        "Container {0} is converted to '{1}', which hides the non-copyable type that it mentions from the analyzer. Keep that type visible, " +
        "or pass the container only to a member that the symbol list marks 'borrows'.";

    private const string OpaqueLambdaResultMessage =
        "The result of this lambda, a container of type {0}, is converted to '{1}', which hides the non-copyable type that it mentions from " +
        "the analyzer. If the result is not meant to be returned, give the lambda a statement body, as in '() => {{ Call(); }}'. Otherwise " +
        "keep that type visible.";

    private const string OpaqueVarianceMessage =
        "The conversion of '{2}' to '{3}' turns container '{0}' into '{1}' for the code behind the reference, which hides the non-copyable " +
        "type that it mentions from the analyzer. Variance converts a type argument with no conversion written. Keep the type arguments the same.";

    private const string OpaqueMemberMessage =
        "'{2}' uses container '{0}' as '{1}', which hides the non-copyable type that it mentions from the analyzer, and is not on the symbol " +
        "list. Add '{3}' to a NonCopyableSymbols.txt file if the member makes no copy, or avoid the use.";

    private const string UntypedCollectionReturnMessage =
        "'{2}' returns container '{0}' where '{3}', which it overrides, returns untyped collection '{1}': a caller of the base member gets a " +
        "collection that hands out copies of the elements. Return the type of the base member, or keep the element type visible in both.";

    private const string OpaqueReturnMessage =
        "'{2}' returns container '{0}' where '{3}', which it overrides, returns '{1}': a caller of the base member gets the container as a type " +
        "that hides the non-copyable type that it mentions from the analyzer. Return the type of the base member, or keep that type visible " +
        "in both.";

    private const string UntypedCollectionMethodGroupMessage =
        "The conversion of '{2}' to delegate '{3}' turns container '{0}' into untyped collection '{1}', which hands out copies of its elements. " +
        "Use a delegate type that keeps the element type visible.";

    private const string OpaqueMethodGroupMessage =
        "The conversion of '{2}' to delegate '{3}' turns container '{0}' into '{1}', which hides the non-copyable type that it mentions from " +
        "the analyzer. Use a delegate type that keeps that type visible.";

    private const string OpaqueTypeParameterConversionMessage =
        "A value of type parameter '{0}' is converted to '{1}'. With a container bound to '{0}' that hands out the container as a type that " +
        "hides the non-copyable type that it mentions from the analyzer, and nothing in the generic can tell. Keep the value as '{0}'.";

    private const string UntypedCollectionConstraintMessage =
        "'{1}' is bound to type parameter '{2}' of '{0}', which is constrained to untyped collection '{3}': the generic can use it as that " +
        "type, which hands out copies of its elements. Use a type parameter that is constrained to a type which keeps the element type visible.";

    private const string OpaqueConstraintMessage =
        "'{1}' is bound to type parameter '{2}' of '{0}', which is constrained to '{3}': the generic can use it as that type, which hides the " +
        "non-copyable type that it mentions from the analyzer. Use a type parameter that is constrained to a type which keeps that type visible.";

    private const string OpaqueBoxingMessage =
        "Boxing '{0}' as '{1}' hides the non-copyable type that it holds. The code of '{2}' is not checked, and what its interfaces give on " +
        "the box are boxed copies. Keep the value as '{0}'.";

    private const string OpaqueBoxingOfContainersMessage =
        "Boxing '{0}' as '{1}' hides the containers that its type arguments carry from the analyzer: other code can reach them through the " +
        "box, with a later cast or reflection. Keep the value as '{0}'.";

    private const string OpaqueLambdaBoxingMessage =
        "The result of this lambda, of type '{0}', is boxed as '{1}', which hides the non-copyable type that it holds. The code of '{2}' is " +
        "not checked, and what its interfaces give on the box are boxed copies. If the result is not meant to be returned, give the lambda " +
        "a statement body, as in '() => {{ Call(); }}'. Otherwise keep the value as '{0}'.";

    private const string OpaqueTypeArgumentMessage =
        "Container '{1}' is bound to type parameter '{2}' of '{0}', which is not marked [NonCopyable], and the member is not on the symbol list. " +
        "Mark the type parameter, add '{3}' to a NonCopyableSymbols.txt file if the member makes no copy, or avoid the use.";

    private const string OpaqueTypeArgumentOfStaticFieldMessage =
        "Container '{1}' is bound to type parameter '{2}' of '{0}', which is not marked [NonCopyable], and the field is not on the symbol " +
        "list: reading or writing the static field runs the static constructor and the static field initializers of its type. Mark the type " +
        "parameter, add '{3}' to a NonCopyableSymbols.txt file if that code makes no copy, or avoid the use.";

    private const string OpaqueTypeArgumentListedForOtherMessage =
        "Container '{1}' is bound to type parameter '{2}' of '{0}', which is not marked [NonCopyable], and the symbol list names the member " +
        "for '{3}' only, which says nothing of what it does with a value of '{2}'. Keep the container out of '{2}', or avoid the use.";

    private const string SafeListUnknownOutcomeMessage =
        "This line of the symbol list file gives 'borrowswhen' the outcome '{0}', which the analyzer does not know. It is left out, so the " +
        "line claims less of the member: the outcomes are 'true' and 'false'.";

    private const string SafeListWidensBuiltInMessage =
        "This line of the symbol list file names '{0}' for {1}, where the built-in framework symbol list names it for '{2}' only. It has no " +
        "effect, because a line of a file can claim less than the built-in list, not more.";

    private const string SafeListClaimsMoreOutcomesThanBuiltInMessage =
        "This line of the symbol list file lists {1} for '{0}' with 'borrowswhen', which the built-in framework symbol list does not list " +
        "for that member. That claims more than the built-in list does, and has no effect, because a line of a file can claim less of a " +
        "member than the built-in list, not more.";

    private const string SafeListBorrowsBeyondBuiltInMessage =
        "This line of the symbol list file marks '{0}' with 'borrows', where the built-in framework symbol list names it {1}. That claims " +
        "more than the built-in list does, and has no effect, because a line of a file can claim less of a member than the built-in list, " +
        "not more.";

    private const string OpaqueTypeDescription =
        "Code that receives a container as object, as a base class or interface that does not name the non-copyable type, or through a type " +
        "parameter that is not marked [NonCopyable] cannot be checked, and can get the container back with a cast. The same goes for a value of a " +
        "marked type parameter that is converted to such a type, since a container can be bound to it. A container can only be used this " +
        "way by a member that the symbol list names, or if every non-copyable type in it sits inside an erasable type: one that the symbol list marks, " +
        "as Task<T> is, or a class or an interface of the project, sealed or not, whose marks guarantee the same.";

    private const string ReadFromStoredContainerMessage =
        "{0} hands out a copy of non-copyable type '{1}' because it reads from a stored value{2}. Read from a new value, take the value out with " +
        "a member that removes it{3}.";

    private const string MadeFromStoredContainerMessage =
        "{0} makes a value of non-copyable type '{1}' out of a stored value{2}, which still holds what the new value reads. Call it on a " +
        "new value{3}.";

    private const string CreatedFromStoredContainerMessage =
        "This creates a value of non-copyable type '{0}' out of a stored value{1}, which still holds what the new value reads. Create it " +
        "from a new value{2}.";

    private const string ReadFromStoredContainerDescription =
        "A member on the symbol list hands out a value that is as new as what the member was called on and with. Called on a stored container, such " +
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
        "the base record runs the copy constructor of '{2}' with no place to warn about it";

    private const string RequiresCopyableOnMemberNeverCalledMessage =
        "'{0}' cannot be marked [RequiresCopyable]: the runtime runs a static constructor and a finalizer, so there is no use at which the " +
        "analyzer could warn about a copy. The mark counts for nothing, and the body is checked as it would be without it.";

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
        "without knowing what it holds. Nothing can show that a container in a field is unshared, so taking it out of one needs a " +
        "suppression, which under the updated memory safety rules only code in an unsafe context can have.";

    private const string MovedFromAddedMessage =
        "Parameter '{0}' of '{1}' cannot be marked [MovedFrom] because {2} does not mark it: a caller through that passes anything, and not " +
        "only what it owns";

    private const string MovedFromAddedOverUncheckedMarkMessage =
        "Parameter '{0}' of '{1}' cannot be marked [MovedFrom] because {2} is of an assembly that was not built with the analyzer, where " +
        "its own mark does not count: a caller through that passes anything, and not only what it owns";

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

    private const string UnsafeToCopySetInOwnCodeTitle = "Do not set UnsafeToCopy in the code of a non-copyable mark";

    private const string UnsafeToCopySetInOwnCodeMessage =
        "'{0}' sets UnsafeToCopy in its own code, which the analyzer does not read: it reads the value only where the attribute is " +
        "applied, from the named argument UnsafeToCopy or from an argument for a 'bool' parameter named 'unsafeToCopy'. A struct marked " +
        "with it counts as what it says there. Write the value where the attribute is applied, or take it as a 'bool' parameter named " +
        "'unsafeToCopy' and set the property or field from it.";

    private const string UnsafeToCopySetInOwnCodeDescription =
        "The analyzer reads UnsafeToCopy of a non-copyable mark where the mark is applied, which metadata shows for a referenced assembly " +
        "as well: the named argument, or an argument for a 'bool' parameter of the constructor named 'unsafeToCopy'. It cannot read the " +
        "code of an attribute class, such as a constructor that sets the property or field, the initializer of either, or a constant passed to the " +
        "constructor of the base class. A mark that sets the value there is read as if it did not, which leaves a struct that is meant " +
        "to be unsafe to copy plain non-copyable with nothing reported.";

    private const string ForeignNonCopyableAttributeMessage =
        "'{0}' is not the attribute that the analyzer reads: only 'Singulink.CodeAnalysis.NonCopyableAttribute' is recognised, so a " +
        "struct that is marked with this class is copied with nothing reported. Declare the class in the namespace 'Singulink.CodeAnalysis', " +
        "or suppress this warning if the attribute is meant for something else.";

    private const string NonCopyableAttributeWithNoSuffixMessage =
        "'{0}' is not the attribute that the analyzer reads: only 'Singulink.CodeAnalysis.NonCopyableAttribute' is recognised, by that " +
        "name, so a struct that is marked with this class is copied with nothing reported. Name the class 'NonCopyableAttribute' and declare " +
        "it in the namespace 'Singulink.CodeAnalysis', or suppress this warning if the attribute is meant for something else.";

    private const string ForeignNonCopyableAttributeUseMessage =
        "This mark is not recognised: '{0}' of '{1}' is not the attribute that the analyzer reads, so {2}. Use " +
        "'Singulink.CodeAnalysis.NonCopyableAttribute', from the Singulink.CodeAnalysis package or declared by the project in the namespace " +
        "'Singulink.CodeAnalysis', or suppress this warning if the attribute is meant for something else.";

    private const string ForeignNonCopyableAttributeDescription =
        "The attributes of the analyzer are recognised by their full name: the namespace Singulink.CodeAnalysis and the name of the class, " +
        "in any assembly, public or internal. A class of the same name in another namespace has no effect on what the project declares, " +
        "since it can as well be the attribute of another tool. For NonCopyableAttribute that fails without a sign: the struct is " +
        "copyable in a build that looks clean. So an attribute class of that name anywhere else gets a warning where it is declared, and " +
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

    private const string TypeParameterTakesNoUnsafeToCopyMessage =
        "Type parameter '{0}' of '{1}' cannot be marked [NonCopyable(UnsafeToCopy = false)] because type parameter '{2}' of '{3}', which the " +
        "member overrides or implements, is not: a call through '{3}' can bind a type that is unsafe to copy";

    private const string MarkNotKeptDescription =
        "Callers trust a type parameter that is marked [NonCopyable], and a member that is not marked [RequiresCopyable], not to copy, and " +
        "they take what a method returns for a new value unless its return value is marked [Borrowed], and what they pass to it to be still " +
        "their own unless the parameter is marked [Kept]. A derived type, an override or an implementation that drops a [NonCopyable] " +
        "mark, or adds [RequiresCopyable], [Borrowed] or [Kept], would put code that is not checked behind that promise, and one that adds " +
        "UnsafeToCopy = false to the mark of a type parameter would take a type that is unsafe to copy into code that is checked for it " +
        "with warnings only. [MovedFrom] is two promises, to the method that its " +
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
        "NonCopyable.ThrowIfNullRef(in {1}) or with a method whose parameter is marked [NotNullRef], which counts only in an assembly that was " +
        "built with the analyzer.";

    private const string NotNullRefMisplacedMessage =
        "[NotNullRef] on parameter '{0}' of '{1}' does not count: {2}. A call of '{1}' does not make a reference known to refer to something.";

    private const string NotNullRefNotCheckedMessage =
        "The analyzer does not check that '{1}' returns only if the reference that it is given for parameter '{0}' refers to something, " +
        "as [NotNullRef] says: its reports about a [MovedFrom] parameter and about a value that is committed at the end of a 'try' " +
        "block rest on that. Verify and test the method, then suppress this warning here.";

    private const string NestedTooDeeplyMessage =
        "'{0}' is nested too deeply for the analyzer and was not checked: a copy in it goes unseen. Break up the expression that is " +
        "nested this deeply, with locals or with methods of its own.";

    private const string NestedTooDeeplyDescription =
        "The analyzer walks an expression by recursion and stops before the stack runs out: a chain of thousands of operators or of a " +
        "thousand calls is deeper than it can follow. It also looks at no type with more than 512 parts, and at no more than 512 types " +
        "in one walk over types, which a type that the compiler infers through a long chain of calls can exceed. The member or the " +
        "declaration is then not checked at all, and this report says so, so that code that is not checked never looks clean.";

    private const string TypeTooLargeMessage =
        "'{0}' uses a type with more parts than the analyzer looks at, and was not checked: a copy in it goes unseen. Use a type with " +
        "fewer parts, nested less deeply or with fewer type arguments, for example with a type of its own for part of it.";

    /// <summary>
    /// What the message of a report says where it is strict under the updated rules only because a walk over types reached its bound.
    /// </summary>
    private const string AtBoundClause =
        "A type here is larger than the analyzer looks into, with its type arguments and what it derives from, so it is treated as if " +
        "it could hold a value that is unsafe to copy.";

    private const string NotNullRefDescription =
        "A read or a write through a null reference fails, so a read or a write through a 'ref', 'in' or 'out' parameter, a 'ref' local " +
        "or 'this' in a struct counts as able to throw unless the reference is known to refer to something. [NotNullRef] on a 'ref', " +
        "'in', 'ref readonly' or 'out' parameter says that the method returns only if the reference that it is given refers to " +
        "something, so a call that returns proves the argument. The mark counts where a call names the code that runs: on a method that " +
        "is not virtual, abstract or an override, static or not, an extension method included, and on a local function. It does not " +
        "count on a by-value parameter, on a member of an interface, a delegate, a lambda, an operator, a constructor or an accessor, " +
        "or on a method that the compiler can leave out of a build, which is one that is [Conditional] or a partial method with no " +
        "body. The body of the method is not checked: the mark is a statement of its author. So every mark that a project declares gets " +
        "one warning, where it stands, to be suppressed there when the method has been verified and tested. NonCopyable.ThrowIfNullRef " +
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
        "This line of the symbol list file marks '{0}' as non-copyable, and the compilation has no type of that ID: the type stays copyable. Fix the " +
        "ID, or take the line out of the file of a project that does not reference the type.";

    private const string SafeListCopyableHasNoEffectMessage =
        "This line of the symbol list file marks '{0}' as copyable. It has no effect, because {1}.";

    private const string SafeListNonCopyableHasNoEffectMessage =
        "This line of the symbol list file marks '{0}' as {1}. It has no effect, because {2}.";

    private const string SafeListClonesHasNoEffectMessage =
        "This line of the symbol list file marks '{0}' as {1}. It has no effect, because {2}.";

    private const string SafeListFieldHasNoEffectMessage =
        "This line of the symbol list file says that the static initializers of the type of '{0}' make no copy. It has no effect, because {1}.";

    private const string SafeListLineDescription =
        "A line of a NonCopyableSymbols.txt file is a documentation comment ID that starts with 'M:', 'P:', 'E:', 'F:' or 'T:', optionally " +
        "followed by ';' and a marker: 'new' or 'borrows' for a member, 'drains' for a member that returns a sequence, 'owned' for an instance " +
        "property of a class whose type is Task<TResult>, 'view', " +
        "'noncopyable' or 'copyable' for a struct, 'erasable' for a generic class or interface, 'clones' for a class whose own code clones " +
        "its objects with MemberwiseClone. A type " +
        "needs a marker. A static field of a generic type takes none: its line says that the static initializers of the type make no copy. " +
        "A member or a field can end with ';for=' and the names of the type parameters that the entry vouches for. A line 'A:<name>;trusted' " +
        "vouches for the assembly of that simple name, which then gets no warning as unchecked code that uses a non-copyable type. A line that starts with " +
        "'#' is a comment, and so is the rest of a line after white space and '#'.";

    public static readonly Rule CopyByAssignment = Create(
        DiagnosticIds.CopyByAssignment,
        CopyByAssignmentTitle,
        CopyByAssignmentMessage,
        CopyByAssignmentDescription,
        About.Copy(0));

    public static readonly Rule CopyByAssignmentIntoVariable = Create(
        DiagnosticIds.CopyByAssignment,
        CopyByAssignmentTitle,
        "Variable '{1}' is a copy of a stored value of non-copyable type '{0}'",
        CopyByAssignmentIntoVariableDescription,
        About.Copy(0));

    public static readonly Rule CopyByAssignmentIntoForEachVariable = Create(
        DiagnosticIds.CopyByAssignment,
        CopyByAssignmentTitle,
        CopyByAssignmentIntoForEachVariableMessage,
        CopyByAssignmentIntoVariableDescription,
        About.Copy(0));

    public static readonly Rule CopyByAssignmentIntoSecondVariable = Create(
        DiagnosticIds.CopyByAssignment,
        CopyByAssignmentTitle,
        "Variable '{1}' is a copy of a value of non-copyable type '{0}' that the pattern gives to another variable as well. A value goes to one variable only.",
        CopyByAssignmentIntoVariableDescription,
        About.Copy(0));

    public static readonly Rule CopyByAssignmentForDeconstruct = Create(
        DiagnosticIds.CopyByAssignment,
        CopyByAssignmentTitle,
        CopyByAssignmentForDeconstructMessage,
        CopyByAssignmentIntoVariableDescription,
        About.Copy(0));

    public static readonly Rule DeconstructionOfStoredValue = Create(
        DiagnosticIds.CopyByAssignment,
        CopyByAssignmentTitle,
        DeconstructionOfStoredValueMessage,
        CopyByAssignmentIntoVariableDescription,
        About.Copy(0));

    public static readonly Rule CopyByAssignmentOutOfBox = Create(
        DiagnosticIds.CopyByAssignment,
        CopyByAssignmentTitle,
        "Converting '{1}' to non-copyable type '{0}' copies the value out of the box",
        CopyByAssignmentOutOfBoxDescription,
        About.Copy(0));

    public static readonly Rule CopyByReturnOfLivingVariable = Create(
        DiagnosticIds.CopyByReturn,
        CopyByReturnTitle,
        CopyByReturnOfLivingVariableMessage,
        CopyByReturnOfLivingVariableDescription,
        About.Copy(0));

    public static readonly Rule CopyByReturnOfVariableUsedAfter = Create(
        DiagnosticIds.CopyByReturn,
        CopyByReturnTitle,
        CopyByReturnOfVariableUsedAfterMessage,
        CopyByReturnOfLivingVariableDescription,
        About.Copy(0));

    public static readonly Rule CopyByReturn = Create(
        DiagnosticIds.CopyByReturn,
        CopyByReturnTitle,
        "Do not return a stored value of non-copyable type '{0}' by value",
        CopyByReturnDescription,
        About.Copy(0));

    public static readonly Rule UsedAfterHandOff = Create(
        DiagnosticIds.UsedAfterHandOff,
        UsedAfterHandOffTitle,
        UsedAfterHandOffMessage,
        UsedAfterHandOffDescription,
        About.Copy(1));

    public static readonly Rule UsedAfterUsing = Create(
        DiagnosticIds.UsedAfterHandOff,
        UsedAfterHandOffTitle,
        UsedAfterUsingMessage,
        UsedAfterHandOffDescription,
        About.Copy(1));

    public static readonly Rule UsedAfterHandOffInHandler = Create(
        DiagnosticIds.UsedAfterHandOff,
        UsedAfterHandOffTitle,
        UsedAfterHandOffInHandlerMessage,
        UsedAfterHandOffDescription,
        About.Copy(1));

    public static readonly Rule UsedAfterHandOffBeforeNullReference = Create(
        DiagnosticIds.UsedAfterHandOff,
        UsedAfterHandOffTitle,
        UsedAfterHandOffBeforeNullReferenceMessage,
        UsedAfterHandOffDescription,
        About.Copy(1));

    public static readonly Rule UnsupportedUse = Create(
        DiagnosticIds.UnsupportedUse,
        UnsupportedUseTitle,
        UnsupportedUseMessage,
        UnsupportedUseDescription,
        About.Copy(0));

    public static readonly Rule ValuePassedTwice = Create(
        DiagnosticIds.UnsupportedUse,
        UnsupportedUseTitle,
        ValuePassedTwiceMessage,
        UnsupportedUseDescription,
        About.Copy(0));

    public static readonly Rule DelegatesCombined = Create(
        DiagnosticIds.UnsupportedUse,
        UnsupportedUseTitle,
        DelegatesCombinedMessage,
        UnsupportedUseDescription,
        About.Copy(1));

    public static readonly Rule CopyOfStoredValue = Create(
        DiagnosticIds.UnsupportedUse,
        UnsupportedUseTitle,
        CopyOfStoredValueMessage,
        UnsupportedUseDescription,
        About.Copy(1));

    public static readonly Rule SwitchStatementMatchesCopy = Create(
        DiagnosticIds.UnsupportedUse,
        UnsupportedUseTitle,
        SwitchStatementMatchesCopyMessage,
        UnsupportedUseDescription,
        About.Copy(0));

    public static readonly Rule SwitchExpressionWithWhenMatchesCopy = Create(
        DiagnosticIds.UnsupportedUse,
        UnsupportedUseTitle,
        SwitchExpressionWithWhenMatchesCopyMessage,
        UnsupportedUseDescription,
        About.Copy(0));

    public static readonly Rule PartUsedTwiceInPattern = Create(
        DiagnosticIds.UnsupportedUse,
        UnsupportedUseTitle,
        PartUsedTwiceInPatternMessage,
        UnsupportedUseDescription,
        About.GivenCopy);

    public static readonly Rule MemberOnReadOnlyReference = Create(
        DiagnosticIds.MemberOnReadOnlyReference,
        MemberOnReadOnlyReferenceTitle,
        MemberOnReadOnlyReferenceMessage,
        MemberOnReadOnlyReferenceDescription,
        About.Copy(0));

    public static readonly Rule MemberOnReadOnlyTypeParameter = Create(
        DiagnosticIds.MemberOnReadOnlyReference,
        MemberOnReadOnlyReferenceTitle,
        MemberOnReadOnlyTypeParameterMessage,
        MemberOnReadOnlyReferenceDescription,
        About.Copy(0));

    public static readonly Rule Boxing = Create(
        DiagnosticIds.Boxing,
        "Do not box a stored non-copyable value",
        "Converting a stored value of non-copyable type '{0}' to '{1}' boxes a copy of it",
        BoxingDescription,
        About.Copy(0));

    public static readonly Rule MemberwiseCloneCall = Create(
        DiagnosticIds.MemberwiseCloneCall,
        MemberwiseCloneTitle,
        "MemberwiseClone of '{0}' copies its {2} of non-copyable type '{1}'",
        MemberwiseCloneCallDescription,
        About.Copy(1));

    public static readonly Rule MemberwiseCloneCallOnValue = Create(
        DiagnosticIds.MemberwiseCloneCall,
        MemberwiseCloneTitle,
        "MemberwiseClone copies a value of non-copyable type '{0}'",
        MemberwiseCloneCallDescription,
        About.Copy(0));

    public static readonly Rule MemberwiseCloneThroughListedBase = CreateWithCurrentRulesMessage(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        MemberwiseCloneThroughListedBaseMessage,
        MemberwiseCloneThroughListedBaseCurrentRulesMessage,
        CloningBaseDescription,
        About.Copy(3));

    public static readonly Rule MemberwiseCloneOfTypeParameterThroughListedBase = CreateWithCurrentRulesMessage(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        MemberwiseCloneOfTypeParameterThroughListedBaseMessage,
        MemberwiseCloneOfTypeParameterThroughListedBaseCurrentRulesMessage,
        CloningBaseDescription,
        About.Copy(3));

    public static readonly Rule FieldBehindUnknownBase = Create(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        FieldBehindUnknownBaseMessage,
        CloningBaseDescription,
        About.Copy(4));

    public static readonly Rule CloningBinding = CreateWithCurrentRulesMessage(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        CloningBindingMessage,
        CloningBindingCurrentRulesMessage,
        CloningBaseDescription,
        About.Copy(0));

    // A binding in the base list of a class is at a declaration, which the modifier 'safe' can answer for, and not in code.
    public static readonly Rule CloningBindingInBaseList = CreateWithCurrentRulesMessage(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        CloningBindingInBaseListMessage,
        CloningBindingCurrentRulesMessage,
        CloningBaseDescription,
        About.Copy(0));

    public static readonly Rule CloningUseConversion = CreateWithCurrentRulesMessage(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        CloningUseConversionMessage,
        CloningUseConversionCurrentRulesMessage,
        CloningBaseDescription,
        About.Copy(3));

    // The conversion that the covariant return of an override stands for is at a declaration, and not in code.
    public static readonly Rule CloningUseReturn = CreateWithCurrentRulesMessage(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        CloningUseReturnMessage,
        CloningUseConversionCurrentRulesMessage,
        CloningBaseDescription,
        About.Copy(3));

    public static readonly Rule CloningUseMember = CreateWithCurrentRulesMessage(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        CloningUseMemberMessage,
        CloningUseMemberCurrentRulesMessage,
        CloningBaseDescription,
        About.Copy(4));

    public static readonly Rule CloningUseOfCloningMember = Create(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        CloningUseOfCloningMemberMessage,
        CloningBaseDescription,
        About.Copy(2));

    public static readonly Rule CloningUseHandsBack = Create(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        CloningUseHandsBackMessage,
        CloningBaseDescription,
        About.Copy(4));

    public static readonly Rule CloningUseOfMemberOfUncoveredClass = Create(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        CloningUseOfMemberOfUncoveredClassMessage,
        CloningBaseDescription,
        About.Copy(4));

    public static readonly Rule CloningUseRunsMemberThatClones = Create(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        CloningUseRunsMemberThatClonesMessage,
        CloningBaseDescription,
        About.Copy(4));

    public static readonly Rule CloningUseRunsOverrideOfCloningClass = Create(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        CloningUseRunsOverrideOfCloningClassMessage,
        CloningBaseDescription,
        About.Copy(4));

    public static readonly Rule CloningUseRunsUnknownMember = Create(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        CloningUseRunsUnknownMemberMessage,
        CloningBaseDescription,
        About.Copy(6));

    public static readonly Rule CloningUseOfUnknownMember = Create(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        CloningUseOfUnknownMemberMessage,
        CloningBaseDescription,
        About.Copy(4));

    public static readonly Rule CloningUseTypeArgument = CreateWithCurrentRulesMessage(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        CloningUseTypeArgumentMessage,
        CloningUseTypeArgumentCurrentRulesMessage,
        CloningBaseDescription,
        About.Copy(4));

    public static readonly Rule CloningUseConstraint = CreateWithCurrentRulesMessage(
        DiagnosticIds.CloningBase,
        CloningBaseTitle,
        CloningUseConstraintMessage,
        CloningUseConstraintCurrentRulesMessage,
        CloningBaseDescription,
        About.Copy(5));

    public static readonly Rule MemberwiseCloneOfDerivedClass = Create(
        DiagnosticIds.MemberwiseCloneCall,
        MemberwiseCloneTitle,
        "MemberwiseClone of '{0}' clones an object of '{1}' as well, which derives from it, and copies its {3} of non-copyable type '{2}'",
        MemberwiseCloneCallDescription,
        About.Copy(2));

    public static readonly Rule MemberwiseCloneOfOpenClass = CreateWithCurrentRulesMessage(
        DiagnosticIds.MemberwiseCloneOfOpenClass,
        "Do not call MemberwiseClone on a class that another assembly can derive from",
        MemberwiseCloneOfOpenClassMessage,
        MemberwiseCloneOfOpenClassCurrentRulesMessage,
        MemberwiseCloneOfOpenClassDescription,
        About.NoType);

    public static readonly Rule MemberClonesWithoutLine = Create(
        DiagnosticIds.MemberwiseCloneOfOpenClass,
        "Do not call MemberwiseClone on a class that another assembly can derive from",
        MemberClonesWithoutLineMessage,
        MemberwiseCloneOfOpenClassDescription,
        About.NoType);

    public static readonly Rule GetObjectValueCall = Create(
        DiagnosticIds.GetObjectValue,
        "Do not use RuntimeHelpers.GetObjectValue",
        "RuntimeHelpers.GetObjectValue clones a boxed struct field by field, a non-copyable value included. Use the object as it is.",
        GetObjectValueDescription,
        About.NoType);

    public static readonly Rule FieldOfCopyableType = Create(
        DiagnosticIds.FieldOfCopyableType,
        FieldOfCopyableTypeTitle,
        "Copyable field '{1}' cannot have non-copyable type '{0}'",
        FieldOfCopyableTypeDescription,
        About.Copy(0));

    public static readonly Rule FieldOfRecord = Create(
        DiagnosticIds.FieldOfCopyableType,
        FieldOfCopyableTypeTitle,
        "Field '{1}' of a record cannot have non-copyable type '{0}'",
        FieldOfRecordDescription,
        About.Copy(0));

    public static readonly Rule FieldByPrimaryConstructor = Create(
        DiagnosticIds.FieldOfCopyableType,
        FieldOfCopyableTypeTitle,
        FieldByPrimaryConstructorMessage,
        FieldOfCopyableTypeDescription,
        About.Copy(0));

    public static readonly Rule FieldOfCopyableTypeUnsafeToCopy = Create(
        DiagnosticIds.FieldOfCopyableType,
        FieldOfCopyableTypeTitle,
        FieldOfCopyableTypeUnsafeToCopyMessage,
        FieldOfCopyableTypeDescription,
        About.Copy(0));

    public static readonly Rule FieldByPrimaryConstructorUnsafeToCopy = Create(
        DiagnosticIds.FieldOfCopyableType,
        FieldOfCopyableTypeTitle,
        FieldByPrimaryConstructorUnsafeToCopyMessage,
        FieldOfCopyableTypeDescription,
        About.Copy(0));

    public static readonly Rule UnsafeToCopyField = Create(
        DiagnosticIds.UnsafeToCopyField,
        UnsafeToCopyFieldTitle,
        UnsafeToCopyFieldMessage,
        UnsafeToCopyFieldDescription,
        About.Copy(0));

    public static readonly Rule UnsafeToCopyFieldByPrimaryConstructor = Create(
        DiagnosticIds.UnsafeToCopyField,
        UnsafeToCopyFieldTitle,
        UnsafeToCopyFieldByPrimaryConstructorMessage,
        UnsafeToCopyFieldDescription,
        About.Copy(0));

    public static readonly Rule AutoProperty = Create(
        DiagnosticIds.AutoProperty,
        "Do not declare an auto-property of a non-copyable type",
        "Auto-property '{1}' cannot have non-copyable type '{0}'",
        AutoPropertyDescription,
        About.Copy(0));

    public static readonly Rule AnonymousTypeMember = Create(
        DiagnosticIds.AutoProperty,
        "Do not declare an auto-property of a non-copyable type",
        AnonymousTypeMemberMessage,
        AutoPropertyDescription,
        About.Copy(0));

    public static readonly Rule PositionalParameterReadByDeconstruct = Create(
        DiagnosticIds.AutoProperty,
        "Do not declare an auto-property of a non-copyable type",
        PositionalParameterReadByDeconstructMessage,
        AutoPropertyDescription,
        About.Copy(0));

    public static readonly Rule IteratorParameter = Create(
        DiagnosticIds.IteratorParameter,
        "Do not give an iterator that returns a sequence a by-value parameter of a non-copyable type",
        IteratorParameterMessage,
        IteratorParameterDescription,
        About.Copy(0));

    public static readonly Rule DefaultInterfaceMember = Create(
        DiagnosticIds.DefaultInterfaceMember,
        DefaultInterfaceMemberTitle,
        DefaultInterfaceMemberMessage,
        DefaultInterfaceMemberDescription,
        About.Copy(0));

    public static readonly Rule DefaultInterfaceMemberBound = Create(
        DiagnosticIds.DefaultInterfaceMember,
        DefaultInterfaceMemberTitle,
        DefaultInterfaceMemberBoundMessage,
        DefaultInterfaceMemberDescription,
        About.Copy(0));

    public static readonly Rule AsyncOrIteratorMember = Create(
        DiagnosticIds.AsyncOrIteratorMember,
        "Do not make an instance member of a non-copyable struct async or an iterator",
        AsyncOrIteratorMemberMessage,
        AsyncOrIteratorMemberDescription,
        About.Copy(1));

    public static readonly Rule MemberListedForOtherTypeParameter = Create(
        DiagnosticIds.UnlistedMember,
        "Do not use a member of an unmarked generic with a non-copyable type",
        MemberListedForOtherTypeParameterMessage,
        UnlistedMemberDescription,
        About.Reach(1));

    public static readonly Rule UnlistedMember = Create(
        DiagnosticIds.UnlistedMember,
        "Do not use a member of an unmarked generic with a non-copyable type",
        UnlistedMemberMessage,
        UnlistedMemberDescription,
        About.Reach(1));

    public static readonly Rule UnlistedMemberOfUncheckedCode = Create(
        DiagnosticIds.UnlistedMember,
        "Do not use a member of an unmarked generic with a non-copyable type",
        UnlistedMemberOfUncheckedCodeMessage,
        UnlistedMemberDescription,
        About.Reach(1));

    public static readonly Rule UnlistedStaticField = Create(
        DiagnosticIds.UnlistedMember,
        "Do not use a member of an unmarked generic with a non-copyable type",
        UnlistedStaticFieldMessage,
        UnlistedMemberDescription,
        About.Reach(1));

    public static readonly Rule UnlistedStaticFieldOfUncheckedCode = Create(
        DiagnosticIds.UnlistedMember,
        "Do not use a member of an unmarked generic with a non-copyable type",
        UnlistedStaticFieldOfUncheckedCodeMessage,
        UnlistedMemberDescription,
        About.Reach(1));

    public static readonly Rule UnlistedMemberThatClones = Create(
        DiagnosticIds.UnlistedMember,
        "Do not use a member of an unmarked generic with a non-copyable type",
        UnlistedMemberThatClonesMessage,
        UnlistedMemberDescription,
        About.Reach(1));

    public static readonly Rule UncheckedMember = Create(
        DiagnosticIds.UncheckedMember,
        "Do not give a non-copyable value other than by value to code that was not built with the analyzer",
        UncheckedMemberMessage,
        UncheckedMemberDescription,
        About.Reach(2));

    public static readonly Rule UncheckedAssembly = Create(
        DiagnosticIds.UncheckedAssembly,
        "Build an assembly that uses a non-copyable type with the analyzer",
        UncheckedAssemblyMessage,
        UncheckedAssemblyDescription,
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly Rule UncheckedAssemblyOfExposedValue = Create(
        DiagnosticIds.UncheckedAssembly,
        "Build an assembly that uses a non-copyable type with the analyzer",
        UncheckedAssemblyOfExposedValueMessage,
        UncheckedAssemblyDescription,
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly Rule UncheckedFriendAssembly = Create(
        DiagnosticIds.UncheckedAssembly,
        "Build an assembly that uses a non-copyable type with the analyzer",
        UncheckedFriendAssemblyMessage,
        UncheckedAssemblyDescription,
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly Rule UncheckedReferenceAssembly = Create(
        DiagnosticIds.UncheckedAssembly,
        "Build an assembly that uses a non-copyable type with the analyzer",
        UncheckedReferenceAssemblyMessage,
        UncheckedAssemblyDescription,
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly Rule UnsafeToCopyNotTaken = Create(
        DiagnosticIds.UnsafeToCopyNotTaken,
        UnsafeToCopyNotTakenTitle,
        UnsafeToCopyNotTakenMessage,
        UnsafeToCopyNotTakenDescription,
        About.Reach(1));

    public static readonly Rule UnsafeToCopyReachedNotTaken = Create(
        DiagnosticIds.UnsafeToCopyNotTaken,
        UnsafeToCopyNotTakenTitle,
        UnsafeToCopyReachedNotTakenMessage,
        UnsafeToCopyNotTakenDescription,
        About.Reach(1));

    public static readonly Rule UnsafeToCopyTypeParameterNotTaken = Create(
        DiagnosticIds.UnsafeToCopyNotTaken,
        UnsafeToCopyNotTakenTitle,
        UnsafeToCopyTypeParameterNotTakenMessage,
        UnsafeToCopyNotTakenDescription,
        About.Reach(1));

    public static readonly Rule UnsafeToCopyNotTakenBehindBox = Create(
        DiagnosticIds.UnsafeToCopyNotTaken,
        UnsafeToCopyNotTakenTitle,
        UnsafeToCopyNotTakenBehindBoxMessage,
        UnsafeToCopyNotTakenDescription,
        About.Reach(3));

    public static readonly Rule UnsafeToCopyReachedNotTakenBehindBox = Create(
        DiagnosticIds.UnsafeToCopyNotTaken,
        UnsafeToCopyNotTakenTitle,
        UnsafeToCopyReachedNotTakenBehindBoxMessage,
        UnsafeToCopyNotTakenDescription,
        About.Reach(3));

    public static readonly Rule UnsafeToCopyNotTakenInField = Create(
        DiagnosticIds.UnsafeToCopyNotTaken,
        UnsafeToCopyNotTakenTitle,
        UnsafeToCopyNotTakenInFieldMessage,
        UnsafeToCopyNotTakenDescription,
        About.Reach(4));

    public static readonly Rule UnsafeToCopyReachedNotTakenInField = Create(
        DiagnosticIds.UnsafeToCopyNotTaken,
        UnsafeToCopyNotTakenTitle,
        UnsafeToCopyReachedNotTakenInFieldMessage,
        UnsafeToCopyNotTakenDescription,
        About.Reach(4));

    public static readonly Rule UnsafeToCopyNotTakenBehindTypeParameter = Create(
        DiagnosticIds.UnsafeToCopyNotTaken,
        UnsafeToCopyNotTakenTitle,
        UnsafeToCopyNotTakenBehindTypeParameterMessage,
        UnsafeToCopyNotTakenDescription,
        About.Reach(4));

    public static readonly Rule UnsafeToCopyReachedNotTakenBehindTypeParameter = Create(
        DiagnosticIds.UnsafeToCopyNotTaken,
        UnsafeToCopyNotTakenTitle,
        UnsafeToCopyReachedNotTakenBehindTypeParameterMessage,
        UnsafeToCopyNotTakenDescription,
        About.Reach(4));

    public static readonly Rule RequiresCopyableMember = Create(
        DiagnosticIds.RequiresCopyable,
        "Do not use a member that requires a copyable type with a non-copyable type",
        "'{0}' requires a copyable type and cannot be used with non-copyable type '{1}' as '{2}'. Use a member that does not copy.",
        RequiresCopyableMemberDescription,
        About.Copy(1));

    public static readonly Rule UntypedCollectionConversion = Create(
        DiagnosticIds.UntypedCollection,
        UntypedCollectionTitle,
        UntypedCollectionConversionMessage,
        UntypedCollectionDescription,
        About.GivenReach);

    public static readonly Rule UntypedCollectionVariance = Create(
        DiagnosticIds.UntypedCollection,
        UntypedCollectionTitle,
        UntypedCollectionVarianceMessage,
        UntypedCollectionDescription,
        About.Reach(0));

    public static readonly Rule UntypedCollectionMember = Create(
        DiagnosticIds.UntypedCollection,
        UntypedCollectionTitle,
        UntypedCollectionMemberMessage,
        UntypedCollectionDescription,
        About.Reach(0));

    public static readonly Rule UntypedCollectionReturn = Create(
        DiagnosticIds.UntypedCollection,
        UntypedCollectionTitle,
        UntypedCollectionReturnMessage,
        UntypedCollectionDescription,
        About.Reach(0));

    public static readonly Rule UntypedCollectionMethodGroup = Create(
        DiagnosticIds.UntypedCollection,
        UntypedCollectionTitle,
        UntypedCollectionMethodGroupMessage,
        UntypedCollectionDescription,
        About.Reach(0));

    public static readonly Rule UntypedCollectionConstraint = Create(
        DiagnosticIds.UntypedCollection,
        UntypedCollectionTitle,
        UntypedCollectionConstraintMessage,
        UntypedCollectionDescription,
        About.Reach(1));

    public static readonly Rule OpaqueConversion = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueConversionMessage,
        OpaqueTypeDescription,
        About.GivenReach);

    public static readonly Rule OpaqueVariance = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueVarianceMessage,
        OpaqueTypeDescription,
        About.Reach(0));

    public static readonly Rule OpaqueMember = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueMemberMessage,
        OpaqueTypeDescription,
        About.Reach(0));

    public static readonly Rule OpaqueMemberThatClones = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueMemberThatClonesMessage,
        OpaqueTypeDescription,
        About.Reach(0));

    public static readonly Rule OpaqueReturn = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueReturnMessage,
        OpaqueTypeDescription,
        About.Reach(0));

    public static readonly Rule OpaqueMethodGroup = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueMethodGroupMessage,
        OpaqueTypeDescription,
        About.Reach(0));

    public static readonly Rule OpaqueTypeParameterConversion = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueTypeParameterConversionMessage,
        OpaqueTypeDescription,
        About.Reach(0));

    public static readonly Rule OpaqueConstraint = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueConstraintMessage,
        OpaqueTypeDescription,
        About.Reach(1));

    public static readonly Rule OpaqueBoxing = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueBoxingMessage,
        OpaqueTypeDescription,
        About.WhatArgumentHolds(0));

    public static readonly Rule OpaqueBoxingOfContainers = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueBoxingOfContainersMessage,
        OpaqueTypeDescription,
        About.WhatArgumentHolds(0));

    public static readonly Rule OpaqueLambdaBoxing = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueLambdaBoxingMessage,
        OpaqueTypeDescription,
        About.WhatArgumentHolds(0));

    public static readonly Rule OpaqueTypeArgumentListedForOther = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueTypeArgumentListedForOtherMessage,
        OpaqueTypeDescription,
        About.Reach(1));

    public static readonly Rule OpaqueTypeArgument = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueTypeArgumentMessage,
        OpaqueTypeDescription,
        About.Reach(1));

    public static readonly Rule OpaqueTypeArgumentOfStaticField = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueTypeArgumentOfStaticFieldMessage,
        OpaqueTypeDescription,
        About.Reach(1));

    public static readonly Rule ReadFromStoredContainer = Create(
        DiagnosticIds.ReadFromStoredContainer,
        ReadFromStoredContainerTitle,
        ReadFromStoredContainerMessage,
        ReadFromStoredContainerDescription,
        About.Copy(1));

    public static readonly Rule MadeFromStoredContainer = Create(
        DiagnosticIds.ReadFromStoredContainer,
        ReadFromStoredContainerTitle,
        MadeFromStoredContainerMessage,
        ReadFromStoredContainerDescription,
        About.Copy(1));

    public static readonly Rule CreatedFromStoredContainer = Create(
        DiagnosticIds.ReadFromStoredContainer,
        ReadFromStoredContainerTitle,
        CreatedFromStoredContainerMessage,
        ReadFromStoredContainerDescription,
        About.Copy(0));

    public static readonly Rule OpaqueLambdaResult = Create(
        DiagnosticIds.OpaqueType,
        OpaqueTypeTitle,
        OpaqueLambdaResultMessage,
        OpaqueTypeDescription,
        About.GivenReach);

    public static readonly Rule UnownedContainerReturned = Create(
        DiagnosticIds.UnownedContainerReturned,
        UnownedContainerReturnedTitle,
        UnownedContainerReturnedMessage,
        UnownedContainerReturnedDescription,
        About.GivenReach);

    public static readonly Rule DelegateHandsOutStoredContainer = Create(
        DiagnosticIds.UnownedContainerReturned,
        UnownedContainerReturnedTitle,
        DelegateHandsOutStoredContainerMessage,
        UnownedContainerReturnedDescription,
        About.Reach(1));

    public static readonly Rule DelegateOverTypeParameter = Create(
        DiagnosticIds.UnownedContainerReturned,
        UnownedContainerReturnedTitle,
        DelegateOverTypeParameterMessage,
        UnownedContainerReturnedDescription,
        About.Reach(1));

    public static readonly Rule HolderReturnedByOverride = Create(
        DiagnosticIds.UnownedContainerReturned,
        UnownedContainerReturnedTitle,
        HolderReturnedByOverrideMessage,
        UnownedContainerReturnedDescription,
        About.Reach(0));

    public static readonly Rule HolderLostByVariance = Create(
        DiagnosticIds.UnownedContainerReturned,
        UnownedContainerReturnedTitle,
        HolderLostByVarianceMessage,
        UnownedContainerReturnedDescription,
        About.Reach(2, 3));

    public static readonly Rule TypeParameterMarkNotPassedOn = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        "Type parameter '{0}' is passed to [NonCopyable] type parameter '{1}' of '{2}' and must be marked [NonCopyable] as well",
        MarkNotKeptDescription,
        About.NoType);

    public static readonly Rule TypeParameterMarkNotKept = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        TypeParameterMarkNotKeptMessage,
        MarkNotKeptDescription,
        About.NoType);

    public static readonly Rule TypeParameterTakesNoUnsafeToCopy = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        TypeParameterTakesNoUnsafeToCopyMessage,
        MarkNotKeptDescription,
        About.NoType);

    public static readonly Rule RequiresCopyableNotOnBaseMember = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        "'{0}' cannot be marked [RequiresCopyable] because '{1}', which it overrides or implements, is not",
        MarkNotKeptDescription,
        About.NoType);

    public static readonly Rule RequiresCopyableNotOnBaseCopyConstructor = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        RequiresCopyableNotOnBaseCopyConstructorMessage,
        MarkNotKeptDescription,
        About.NoType);

    public static readonly Rule RequiresCopyableOnMemberNeverCalled = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        RequiresCopyableOnMemberNeverCalledMessage,
        MarkNotKeptDescription,
        About.NoType);

    public static readonly Rule BorrowedNotOnBaseMember = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        BorrowedNotOnBaseMemberMessage,
        MarkNotKeptDescription,
        About.GivenReach);

    public static readonly Rule BorrowedNotOnDelegate = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        BorrowedNotOnDelegateMessage,
        MarkNotKeptDescription,
        About.GivenReach);

    public static readonly Rule KeepsUnmarkedParameter = Create(
        DiagnosticIds.KeptParameter,
        "Mark a container parameter that the method keeps",
        KeepsUnmarkedParameterMessage,
        KeepsUnmarkedParameterDescription,
        About.GivenReach);

    public static readonly Rule ParameterUsedInAWayNotFollowed = Create(
        DiagnosticIds.KeptParameter,
        "Mark a container parameter that the method keeps",
        ParameterUsedInAWayNotFollowedMessage,
        KeepsUnmarkedParameterDescription,
        About.GivenReach);

    public static readonly Rule PassesParameterToUncheckedCode = Create(
        DiagnosticIds.KeptParameter,
        "Mark a container parameter that the method keeps",
        PassesParameterToUncheckedCodeMessage,
        KeepsUnmarkedParameterDescription,
        About.GivenReach);

    public static readonly Rule KeepsParameterAfterReturning = Create(
        DiagnosticIds.KeptParameter,
        "Mark a container parameter that the method keeps",
        KeepsParameterAfterReturningMessage,
        KeepsUnmarkedParameterDescription,
        About.GivenReach);

    public static readonly Rule ContainerMovedFromUnowned = Create(
        DiagnosticIds.ContainerNotOwned,
        ContainerNotOwnedTitle,
        ContainerMovedFromUnownedMessage,
        ContainerNotOwnedDescription,
        About.GivenReach);

    public static readonly Rule ContainerMovedFromSlotParameter = Create(
        DiagnosticIds.ContainerNotOwned,
        ContainerNotOwnedTitle,
        ContainerMovedFromSlotParameterMessage,
        ContainerNotOwnedDescription,
        About.Reach(0));

    /// <summary>
    /// The same finding for a method that is used as a method group, which is known only when the whole compilation has been seen.
    /// </summary>
    public static readonly Rule ContainerMovedFromSlotParameterOfMethodGroup = Create(
        DiagnosticIds.ContainerNotOwned,
        ContainerNotOwnedTitle,
        ContainerMovedFromSlotParameterMessage,
        ContainerNotOwnedDescription,
        About.Reach(0),
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly Rule SlotGivenToOwner = Create(
        DiagnosticIds.ContainerNotOwned,
        ContainerNotOwnedTitle,
        SlotGivenToOwnerMessage,
        ContainerNotOwnedDescription,
        About.NoType);

    public static readonly Rule MovedFromAdded = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        MovedFromAddedMessage,
        MarkNotKeptDescription,
        About.GivenCopy);

    public static readonly Rule MovedFromAddedOverUncheckedMark = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        MovedFromAddedOverUncheckedMarkMessage,
        MarkNotKeptDescription,
        About.GivenCopy);

    public static readonly Rule MovedFromDropped = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        MovedFromDroppedMessage,
        MarkNotKeptDescription,
        About.GivenCopy);

    public static readonly Rule MovedFromDroppedWithNoParameterList = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        MovedFromDroppedWithNoParameterListMessage,
        MarkNotKeptDescription,
        About.GivenCopy);

    public static readonly Rule KeptNotOnBaseMember = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        KeptNotOnBaseMemberMessage,
        MarkNotKeptDescription,
        About.GivenReach);

    public static readonly Rule KeptNotOnDelegate = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        KeptNotOnDelegateMessage,
        MarkNotKeptDescription,
        About.GivenReach);

    public static readonly Rule KeepingNotOnInterfaceMember = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        KeepingNotOnInterfaceMemberMessage,
        MarkNotKeptDescription,
        About.GivenReach);

    public static readonly Rule KeepingNotOnDelegate = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        KeepingNotOnDelegateMessage,
        MarkNotKeptDescription,
        About.GivenReach);

    public static readonly Rule ImplementationNotChecked = Create(
        DiagnosticIds.MarkNotKept,
        MarkNotKeptTitle,
        ImplementationNotCheckedMessage,
        MarkNotKeptDescription,
        About.Reach(3));

    public static readonly Rule MovedFromNotOnRefParameter = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        "[MovedFrom] can only be applied to a 'ref' parameter",
        MovedFromDescription,
        About.GivenCopy);

    public static readonly Rule MovedFromOnPrimaryConstructor = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromOnPrimaryConstructorMessage,
        MovedFromDescription,
        About.GivenCopy);

    public static readonly Rule MovedFromPathsNotChecked = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromPathsNotCheckedMessage,
        MovedFromDescription,
        About.GivenCopy,
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly Rule MovedFromParameterNotAssigned = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromParameterNotAssignedMessage,
        MovedFromDescription,
        About.GivenCopy);

    public static readonly Rule MovedFromParameterReadAgain = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromParameterReadAgainMessage,
        MovedFromDescription,
        About.GivenCopy);

    public static readonly Rule MovedFromParameterNotAssignedAtOnce = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromParameterNotAssignedAtOnceMessage,
        MovedFromDescription,
        About.GivenCopy);

    public static readonly Rule MovedFromParameterReassigned = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromParameterReassignedMessage,
        MovedFromDescription,
        About.GivenCopy);

    public static readonly Rule GenericMoveReturnsSomethingElse = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        GenericMoveReturnsSomethingElseMessage,
        MovedFromDescription,
        About.NoType);

    public static readonly Rule GenericMoveLeavesSomethingElse = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        GenericMoveLeavesSomethingElseMessage,
        MovedFromDescription,
        About.NoType);

    public static readonly Rule MovedFromContainerNotOwned = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromContainerNotOwnedMessage,
        MovedFromDescription,
        About.GivenReach);

    public static readonly Rule MovedFromContainerExposed = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromContainerExposedMessage,
        MovedFromDescription,
        About.GivenReach);

    public static readonly Rule SafeListLine = Create(
        DiagnosticIds.SafeListLine,
        "Fix the line of the symbol list file",
        "This line of the symbol list file cannot be parsed: {0}",
        SafeListLineDescription);

    public static readonly Rule SafeListNonCopyableNamesNothing = Create(
        DiagnosticIds.SafeListLine,
        "Fix the line of the symbol list file",
        SafeListNonCopyableNamesNothingMessage,
        SafeListLineDescription);

    public static readonly Rule SafeListCopyableHasNoEffect = Create(
        DiagnosticIds.SafeListLine,
        "Fix the line of the symbol list file",
        SafeListCopyableHasNoEffectMessage,
        SafeListLineDescription);

    public static readonly Rule SafeListNonCopyableHasNoEffect = Create(
        DiagnosticIds.SafeListLine,
        "Fix the line of the symbol list file",
        SafeListNonCopyableHasNoEffectMessage,
        SafeListLineDescription);

    public static readonly Rule SafeListWidensBuiltIn = Create(
        DiagnosticIds.SafeListLine,
        "Fix the line of the symbol list file",
        SafeListWidensBuiltInMessage,
        SafeListLineDescription);

    public static readonly Rule SafeListClaimsMoreOutcomesThanBuiltIn = Create(
        DiagnosticIds.SafeListLine,
        "Fix the line of the symbol list file",
        SafeListClaimsMoreOutcomesThanBuiltInMessage,
        SafeListLineDescription);

    public static readonly Rule SafeListBorrowsBeyondBuiltIn = Create(
        DiagnosticIds.SafeListLine,
        "Fix the line of the symbol list file",
        SafeListBorrowsBeyondBuiltInMessage,
        SafeListLineDescription);

    public static readonly Rule SafeListUnknownOutcome = Create(
        DiagnosticIds.SafeListLine,
        "Fix the line of the symbol list file",
        SafeListUnknownOutcomeMessage,
        SafeListLineDescription);

    public static readonly Rule SafeListContradictsClones = Create(
        DiagnosticIds.SafeListLine,
        "Fix the line of the symbol list file",
        SafeListContradictsClonesMessage,
        SafeListLineDescription);

    public static readonly Rule SafeListNamesAccessor = Create(
        DiagnosticIds.SafeListLine,
        "Fix the line of the symbol list file",
        SafeListNamesAccessorMessage,
        SafeListLineDescription);

    public static readonly Rule SafeListClonesHasNoEffect = Create(
        DiagnosticIds.SafeListLine,
        "Fix the line of the symbol list file",
        SafeListClonesHasNoEffectMessage,
        SafeListLineDescription);

    public static readonly Rule SafeListFieldHasNoEffect = Create(
        DiagnosticIds.SafeListLine,
        "Fix the line of the symbol list file",
        SafeListFieldHasNoEffectMessage,
        SafeListLineDescription);

    // Whether the line has an effect is known only once the references of the whole compilation have been looked at.
    public static readonly Rule SafeListTrustedHasNoEffect = Create(
        DiagnosticIds.SafeListLine,
        "Fix the line of the symbol list file",
        SafeListTrustedHasNoEffectMessage,
        SafeListLineDescription,
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly Rule NonCopyableAwaiter = Create(
        DiagnosticIds.NonCopyableAwaiter,
        "Do not await with an awaiter of a non-copyable type",
        NonCopyableAwaiterMessage,
        NonCopyableAwaiterDescription,
        About.Copy(0));

    public static readonly Rule ConditionalMarker = Create(
        DiagnosticIds.ConditionalMarker,
        ConditionalMarkerTitle,
        ConditionalMarkerMessage,
        ConditionalMarkerDescription,
        About.NoType);

    public static readonly Rule ConditionalMarkerBase = Create(
        DiagnosticIds.ConditionalMarker,
        ConditionalMarkerTitle,
        ConditionalMarkerBaseMessage,
        ConditionalMarkerDescription,
        About.NoType);

    public static readonly Rule ConditionalMarkerUse = Create(
        DiagnosticIds.ConditionalMarker,
        ConditionalMarkerTitle,
        ConditionalMarkerUseMessage,
        ConditionalMarkerDescription,
        About.NoType);

    public static readonly Rule ForeignNonCopyableAttribute = Create(
        DiagnosticIds.ForeignNonCopyableAttribute,
        ForeignNonCopyableAttributeTitle,
        ForeignNonCopyableAttributeMessage,
        ForeignNonCopyableAttributeDescription);

    public static readonly Rule NonCopyableAttributeWithNoSuffix = Create(
        DiagnosticIds.ForeignNonCopyableAttribute,
        ForeignNonCopyableAttributeTitle,
        NonCopyableAttributeWithNoSuffixMessage,
        ForeignNonCopyableAttributeDescription);

    public static readonly Rule ForeignNonCopyableAttributeUse = Create(
        DiagnosticIds.ForeignNonCopyableAttribute,
        ForeignNonCopyableAttributeTitle,
        ForeignNonCopyableAttributeUseMessage,
        ForeignNonCopyableAttributeDescription);

    public static readonly Rule UnsafeToCopySetInOwnCode = Create(
        DiagnosticIds.UnsafeToCopySetInOwnCode,
        UnsafeToCopySetInOwnCodeTitle,
        UnsafeToCopySetInOwnCodeMessage,
        UnsafeToCopySetInOwnCodeDescription);

    public static readonly Rule NotNullRefMisplaced = Create(
        DiagnosticIds.NotNullRefMisplaced,
        "Put [NotNullRef] on a reference parameter of a method that is not virtual",
        NotNullRefMisplacedMessage,
        NotNullRefDescription,
        About.NoType);

    public static readonly Rule NotNullRefNotChecked = Create(
        DiagnosticIds.NotNullRefNotChecked,
        "Verify a method with a [NotNullRef] parameter, then suppress this warning on it",
        NotNullRefNotCheckedMessage,
        NotNullRefDescription);

    public static readonly Rule NestedTooDeeply = Create(
        DiagnosticIds.NestedTooDeeply,
        "Break up code that is nested too deeply to be checked",
        NestedTooDeeplyMessage,
        NestedTooDeeplyDescription,
        About.GivenReach);

    public static readonly Rule TypeTooLarge = Create(
        DiagnosticIds.NestedTooDeeply,
        "Break up code that is nested too deeply to be checked",
        TypeTooLargeMessage,
        NestedTooDeeplyDescription,
        About.GivenReach);

    public static readonly Rule MovedFromParameterNotAssignedAtOnceThroughReference = Create(
        DiagnosticIds.MovedFromMisuse,
        MovedFromTitle,
        MovedFromParameterNotAssignedAtOnceThroughReferenceMessage,
        MovedFromDescription,
        About.GivenCopy);

    public static readonly Rule ErasableBaseNotKept = Create(
        DiagnosticIds.ErasableBase,
        "Keep a type that derives from an erasable type erasable",
        ErasableBaseMessage,
        ErasableBaseDescription,
        About.Reach(1));

    /// <summary>
    /// Every rule of this class. A rule that is added above is added here: a test fails if one is missing.
    /// </summary>
    public static readonly ImmutableArray<Rule> All = [
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
        UsedAfterHandOff,
        UsedAfterUsing,
        UsedAfterHandOffInHandler,
        UsedAfterHandOffBeforeNullReference,
        SwitchStatementMatchesCopy,
        SwitchExpressionWithWhenMatchesCopy,
        PartUsedTwiceInPattern,
        MemberOnReadOnlyReference,
        MemberOnReadOnlyTypeParameter,
        Boxing,
        MemberwiseCloneCall,
        MemberwiseCloneCallOnValue,
        MemberwiseCloneOfDerivedClass,
        MemberwiseCloneOfOpenClass,
        MemberwiseCloneThroughListedBase,
        MemberwiseCloneOfTypeParameterThroughListedBase,
        FieldBehindUnknownBase,
        CloningBinding,
        CloningBindingInBaseList,
        CloningUseConversion,
        CloningUseReturn,
        CloningUseMember,
        CloningUseOfCloningMember,
        CloningUseConstraint,
        CloningUseTypeArgument,
        CloningUseHandsBack,
        CloningUseOfUnknownMember,
        CloningUseOfMemberOfUncoveredClass,
        CloningUseRunsMemberThatClones,
        CloningUseRunsOverrideOfCloningClass,
        CloningUseRunsUnknownMember,
        MemberClonesWithoutLine,
        GetObjectValueCall,
        FieldOfCopyableType,
        FieldOfRecord,
        FieldByPrimaryConstructor,
        FieldOfCopyableTypeUnsafeToCopy,
        FieldByPrimaryConstructorUnsafeToCopy,
        UnsafeToCopyField,
        UnsafeToCopyFieldByPrimaryConstructor,
        AutoProperty,
        AnonymousTypeMember,
        PositionalParameterReadByDeconstruct,
        IteratorParameter,
        DefaultInterfaceMember,
        DefaultInterfaceMemberBound,
        AsyncOrIteratorMember,
        UnlistedMember,
        MemberListedForOtherTypeParameter,
        UnlistedMemberOfUncheckedCode,
        UnlistedMemberThatClones,
        UnlistedStaticField,
        UnlistedStaticFieldOfUncheckedCode,
        UncheckedMember,
        UncheckedAssembly,
        UncheckedAssemblyOfExposedValue,
        UncheckedFriendAssembly,
        UncheckedReferenceAssembly,
        RequiresCopyableMember,
        UnsafeToCopyNotTaken,
        UnsafeToCopyReachedNotTaken,
        UnsafeToCopyTypeParameterNotTaken,
        UnsafeToCopyNotTakenBehindBox,
        UnsafeToCopyReachedNotTakenBehindBox,
        UnsafeToCopyNotTakenInField,
        UnsafeToCopyReachedNotTakenInField,
        UnsafeToCopyNotTakenBehindTypeParameter,
        UnsafeToCopyReachedNotTakenBehindTypeParameter,
        UntypedCollectionConversion,
        UntypedCollectionVariance,
        UntypedCollectionMember,
        UntypedCollectionReturn,
        UntypedCollectionMethodGroup,
        UntypedCollectionConstraint,
        OpaqueConversion,
        OpaqueVariance,
        OpaqueMember,
        OpaqueMemberThatClones,
        OpaqueReturn,
        OpaqueMethodGroup,
        OpaqueTypeParameterConversion,
        OpaqueConstraint,
        OpaqueBoxing,
        OpaqueBoxingOfContainers,
        OpaqueLambdaBoxing,
        OpaqueTypeArgument,
        OpaqueTypeArgumentOfStaticField,
        OpaqueTypeArgumentListedForOther,
        ReadFromStoredContainer,
        MadeFromStoredContainer,
        CreatedFromStoredContainer,
        OpaqueLambdaResult,
        UnownedContainerReturned,
        DelegateHandsOutStoredContainer,
        DelegateOverTypeParameter,
        HolderReturnedByOverride,
        HolderLostByVariance,
        TypeParameterMarkNotPassedOn,
        TypeParameterMarkNotKept,
        TypeParameterTakesNoUnsafeToCopy,
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
        MovedFromAddedOverUncheckedMark,
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
        SafeListNonCopyableHasNoEffect,
        SafeListClonesHasNoEffect,
        SafeListFieldHasNoEffect,
        SafeListNamesAccessor,
        SafeListContradictsClones,
        SafeListWidensBuiltIn,
        SafeListClaimsMoreOutcomesThanBuiltIn,
        SafeListBorrowsBeyondBuiltIn,
        SafeListUnknownOutcome,
        SafeListTrustedHasNoEffect,
        NonCopyableAwaiter,
        ConditionalMarker,
        ConditionalMarkerBase,
        ConditionalMarkerUse,
        ForeignNonCopyableAttribute,
        NonCopyableAttributeWithNoSuffix,
        ForeignNonCopyableAttributeUse,
        UnsafeToCopySetInOwnCode,
        ErasableBaseNotKept,
        NotNullRefMisplaced,
        NotNullRefNotChecked,
        MovedFromParameterNotAssignedAtOnceThroughReference,
        NestedTooDeeply,
        TypeTooLarge,
    ];

    /// <summary>
    /// Every descriptor of every rule, which is what the analyzer supports.
    /// </summary>
    public static readonly ImmutableArray<DiagnosticDescriptor> Descriptors = [.. All.SelectMany(rule => rule.Descriptors)];

    /// <summary>
    /// Gets the group of a diagnostic ID, which decides the severities that its rules can be reported with. Every ID is in one group: a
    /// new ID is added here, or no rule of it can be created.
    /// </summary>
    public static RuleGroup GetGroup(string id) => id switch {
        DiagnosticIds.CopyByAssignment or
        DiagnosticIds.CopyByReturn or
        DiagnosticIds.UnsupportedUse or
        DiagnosticIds.Boxing or
        DiagnosticIds.MemberwiseCloneCall or
        DiagnosticIds.MemberOnReadOnlyReference or
        DiagnosticIds.GetObjectValue or
        DiagnosticIds.MemberwiseCloneOfOpenClass or
        DiagnosticIds.UnlistedMember or
        DiagnosticIds.RequiresCopyable or
        DiagnosticIds.UntypedCollection or
        DiagnosticIds.OpaqueType or
        DiagnosticIds.UncheckedMember or
        DiagnosticIds.ReadFromStoredContainer or
        DiagnosticIds.UnownedContainerReturned or
        DiagnosticIds.ContainerNotOwned or
        DiagnosticIds.CloningBase or
        DiagnosticIds.FieldOfCopyableType or
        DiagnosticIds.AutoProperty or
        DiagnosticIds.IteratorParameter or
        DiagnosticIds.DefaultInterfaceMember or
        DiagnosticIds.AsyncOrIteratorMember or
        DiagnosticIds.NonCopyableAwaiter or
        DiagnosticIds.UsedAfterHandOff or
        DiagnosticIds.UnsafeToCopyField or
        DiagnosticIds.UnsafeToCopyNotTaken or
        DiagnosticIds.KeptParameter or
        DiagnosticIds.MarkNotKept or
        DiagnosticIds.MovedFromMisuse or
        DiagnosticIds.ErasableBase or
        DiagnosticIds.ConditionalMarker or
        DiagnosticIds.NotNullRefMisplaced or
        DiagnosticIds.NestedTooDeeply => RuleGroup.Enforced,

        DiagnosticIds.SafeListLine or
        DiagnosticIds.ForeignNonCopyableAttribute or
        DiagnosticIds.UnsafeToCopySetInOwnCode or
        DiagnosticIds.NotNullRefNotChecked => RuleGroup.Plain,

        DiagnosticIds.UncheckedAssembly => RuleGroup.UncheckedAssembly,

        _ => throw new ArgumentException($"Diagnostic ID '{id}' is in no group.", nameof(id)),
    };

    /// <summary>
    /// Creates a rule of the plain group or of the group of an unchecked assembly, with the descriptors of its group.
    /// </summary>
    private static Rule Create(string id, string title, string messageFormat, string description, params string[] customTags) =>
        CreateWithCurrentRulesMessage(id, title, messageFormat, messageFormat, description, default, customTags);

    /// <summary>
    /// Creates a rule with the descriptors of its group, and with what its reports are about, which an enforced rule must say. The
    /// ordinary warning is the one of the current memory safety rules, and of a report under the updated rules that is about no type
    /// which is unsafe to copy. The error of the updated rules is not configurable and carries the tag of a compiler diagnostic, which is
    /// what makes the compiler ignore a [SuppressMessage] for it; without that tag, 'NotConfigurable' alone still lets one through. The
    /// warning of an unsafe context under the updated rules carries the tag of a compiler diagnostic too, so that only '#pragma' and
    /// NoWarn suppress it.
    /// </summary>
    private static Rule Create(string id, string title, string messageFormat, string description, About about, params string[] customTags) =>
        CreateWithCurrentRulesMessage(id, title, messageFormat, messageFormat, description, about, customTags);

    /// <summary>
    /// Creates a rule whose message under the current memory safety rules gives other advice than under the updated rules: where to
    /// suppress it. Under the updated rules only an unsafe context or a marked declaration can, for a report about a type that is unsafe
    /// to copy; a report about any other type has the message of the current rules under both.
    /// </summary>
    private static Rule CreateWithCurrentRulesMessage(
        string id, string title, string messageFormat, string currentRulesMessageFormat, string description, About about, params string[] customTags)
    {
        var group = GetGroup(id);

        if (group is RuleGroup.Enforced && !about.IsSpecified)
            throw new ArgumentException($"A rule of {id} says what its reports are about.", nameof(about));

        // The message of a report that is strict only because a walk reached its bound says so after what the rule says.
        string atBoundFormat = messageFormat + (messageFormat.EndsWith(".", StringComparison.Ordinal) ? " " : ". ") + AtBoundClause;

        return group switch {
            RuleGroup.Enforced => new Rule(
                group,
                Describe(DiagnosticSeverity.Warning, currentRulesMessageFormat),
                error: Describe(DiagnosticSeverity.Error, messageFormat, WellKnownDiagnosticTags.NotConfigurable, WellKnownDiagnosticTags.Compiler),
                unsafeWarning: Describe(DiagnosticSeverity.Warning, messageFormat, WellKnownDiagnosticTags.Compiler),
                about: about,
                errorAtBound: Describe(DiagnosticSeverity.Error, atBoundFormat, WellKnownDiagnosticTags.NotConfigurable, WellKnownDiagnosticTags.Compiler),
                unsafeWarningAtBound: Describe(DiagnosticSeverity.Warning, atBoundFormat, WellKnownDiagnosticTags.Compiler)),
            RuleGroup.UncheckedAssembly => new Rule(group, Describe(DiagnosticSeverity.Warning, messageFormat), message: Describe(DiagnosticSeverity.Info, messageFormat)),
            _ => new Rule(group, Describe(DiagnosticSeverity.Warning, messageFormat)),
        };

        DiagnosticDescriptor Describe(DiagnosticSeverity severity, string message, params string[] tags) => new(
            id,
            title,
            message,
            Category,
            severity,
            isEnabledByDefault: true,
            description,
            HelpLinkBaseUri + id.ToLowerInvariant() + ".html",
            [.. customTags, .. tags]);
    }
}
