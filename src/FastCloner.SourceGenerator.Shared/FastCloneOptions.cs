using System;

namespace FastCloner.SourceGenerator.Shared;

/// <summary>
/// Options for a single generated clone operation, passed to
/// <c>FastDeepClone(FastCloneOptions)</c>.
/// <br/><br/>
/// These options describe what the caller requires of <em>this</em> operation. They never change
/// the configured/default behavior of a type: calling <c>FastDeepClone()</c> without options keeps
/// resolving identity preservation exactly as FastCloner already defines it (type and member
/// attributes).
/// </summary>
[Flags]
public enum FastCloneOptions
{
    /// <summary>
    /// No requirement beyond the type's configured/default behavior.
    /// </summary>
    None = 0,

    /// <summary>
    /// Requires object identity to be preserved for this operation: repeated references in the
    /// source graph become repeated references to one clone, and the clone is detached from the
    /// source graph. This holds regardless of whether the type's normal default preserves identity.
    /// <br/><br/>
    /// Only generated roots that declare this capability expose the overload accepting options —
    /// a type discovered through <c>[FastClonerDiscoverGenericArguments(PreserveIdentity = true)]</c>,
    /// or a type that configures identity itself with <c>[FastClonerPreserveIdentity]</c>. For any
    /// other type the call does not compile rather than silently returning an untracked clone.
    /// </summary>
    PreserveIdentity = 1,
}
