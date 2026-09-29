using System;

namespace FastCloner.SourceGenerator;

/// <summary>
/// Represents a type model for code generation.
/// CRITICAL: This is a record for proper equality comparison to enable incremental caching.
/// It stores NO Roslyn symbols (ISymbol, Syntax nodes) as they break caching.
/// </summary>
internal sealed record TypeModel(
    string Namespace,
    string Name,
    string FullyQualifiedName,
    bool IsStruct,
    bool IsSealed,
    bool IsAbstract,
    bool IsRecord,
    bool HasClonableBaseClass,
    bool CanHaveCircularReferences,
    bool NeedsStateTracking, // True if state needed for cycles OR identity preservation
    bool IsFastClonerAvailable,
    EquatableArray<MemberModel> Members,
    EquatableArray<string> TypeParameters,
    EquatableArray<string> TypeConstraints,
    EquatableArray<TypeModel> RelatedTypes, // Implicitly clonable types that we generate helpers for
    EquatableArray<MemberModel> NestedTypes, // Nested collection types that need helpers
    EquatableArray<TypeModel> DerivedTypes, // Concrete derived types for abstract class dispatch
    bool NullabilityEnabled,
    bool TrustNullability, // Whether to trust nullability annotations and skip null checks
    bool? PreserveIdentity = null, // null=default (off), true=preserve identity in subgraph, false=explicitly disabled
    bool IsRefLikeType = false, // Whether the type is a ref struct (cannot be boxed/used as generic)
    bool HasParameterlessConstructor = true, // Whether the type has a public parameterless constructor (defaults to true for safety)
    bool CodeAnalysisAvailable = false, // Whether System.Diagnostics.CodeAnalysis attributes are available
    bool IsPolymorphicRoot = false, // Non-abstract root marked [FastClonerPolymorphic]: dispatch by runtime type to subtype cloners
    TargetFramework TargetFramework = TargetFramework.NetStandard20, // Detected target framework for TFM-specific optimizations
    EquatableArray<string> CircularAnalysisLog = default,
    // Capability, not behavior: a tracking state supplied to InternalFastDeepClone is honored
    // throughout this type's generated graph, so an explicit identity-preserving operation
    // (FastDeepClone(FastCloneOptions.PreserveIdentity)) can rely on it. NeedsStateTracking stays
    // the only thing that decides the default behavior of the public entry point. Implied by
    // NeedsStateTracking, by identity configuration on the type itself, and by a
    // [FastClonerDiscoverGenericArguments(PreserveIdentity = true)] requirement.
    bool SupportsStateTracking = false,
    // Hard requirement, not a capability: a [FastClonerDiscoverGenericArguments(PreserveIdentity = true)]
    // surface (directly or through another required type's graph) needs this root to be able to serve
    // an explicit identity-preserving operation for its whole graph. When it cannot, the operation is
    // withheld and the requirement is reported instead of being silently downgraded.
    bool IdentityPreservationRequired = false) : IEquatable<TypeModel>
{
    /// <summary>
    /// True when <paramref name="typeName"/> is the root's only type parameter, i.e. the exact shape
    /// the generated <c>Cloner&lt;T&gt;</c> nested helper is declared for. Other type parameters
    /// cannot be routed through it: that helper's method signature is typed by the first type
    /// parameter only.
    /// </summary>
    public bool IsSoleTypeParameter(string typeName)
    {
        string[]? typeParameters = TypeParameters.GetArray();
        return typeParameters is { Length: 1 } && typeParameters[0] == typeName;
    }

    /// <summary>
    /// True when any element/key/value of a collection-shaped member is the root's sole type
    /// parameter - the case where a collection helper can hand work to the generated
    /// <c>Cloner&lt;T&gt;</c> instead of the runtime cloner.
    /// </summary>
    public bool UsesSoleTypeParameterCloner(MemberModel member)
    {
        return IsSoleTypeParameter(member.ElementTypeName) ||
               IsSoleTypeParameter(member.KeyTypeName) ||
               IsSoleTypeParameter(member.ValueTypeName);
    }
}
