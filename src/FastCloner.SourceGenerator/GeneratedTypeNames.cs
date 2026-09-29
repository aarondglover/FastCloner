namespace FastCloner.SourceGenerator;

/// <summary>
/// Fully-qualified names emitted into generated source. Using global:: names means
/// generated files never depend on a using directive or a nested type that may not exist.
/// </summary>
internal static class GeneratedTypeNames
{
    /// <summary>
    /// Identity-tracking state used by generated clone methods and collection helpers.
    /// Always the public Shared type — never a per-file nested class.
    /// </summary>
    public const string CloneState = "global::FastCloner.SourceGenerator.Shared.FcGeneratedCloneState";

    /// <summary>
    /// <summary>
    /// State instance meaning "explicitly do not preserve identity". Emitted for members marked
    /// <c>[FastClonerPreserveIdentity(false)]</c>; <c>null</c> cannot express that because it
    /// means "no state supplied, use the target type's own default".
    /// </summary>
    public const string NoTrackingCloneState = "global::FastCloner.SourceGenerator.Shared.FcGeneratedCloneState.NoReferenceTracking";

    /// <summary>
    /// Operation-level options accepted by the generated <c>FastDeepClone</c> overload that can
    /// positively require identity preservation for a single call.
    /// </summary>
    public const string FastCloneOptions = "global::FastCloner.SourceGenerator.Shared.FastCloneOptions";
}
