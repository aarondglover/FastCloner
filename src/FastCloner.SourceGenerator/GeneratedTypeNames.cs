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
}
