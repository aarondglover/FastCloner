using System;

namespace FastCloner.SourceGenerator;

/// <summary>
/// A closed generic argument observed for a generic type registered with <c>[FastClonerInclude]</c>.
/// <br/><br/>
/// <paramref name="IsDeclaredInCompilation"/> records whether the argument type's own generated
/// entry point lives in this compilation. Only then can the generated <c>Cloner&lt;T&gt;</c> helper
/// hand its tracking state over: another assembly's generated
/// <c>InternalFastDeepClone(source, state)</c> is internal and out of reach, so those usages keep
/// using the public entry point and are reported as a runtime boundary instead.
/// </summary>
internal readonly record struct GenericUsage(
    string GenericTypeMetadataName,
    string ArgumentTypeMetadataName,
    string? ExtensionClassFQN,
    MemberModel? CollectionModel,
    EquatableArray<MemberModel> NestedHelpers,
    EquatableArray<TypeModel> ImplicitTypes,
    bool IsSafe,
    bool IsClonable,
    bool IsDeclaredInCompilation = true
) : IEquatable<GenericUsage>;
