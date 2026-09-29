using System;

namespace FastCloner.SourceGenerator.Shared;

/// <summary>
/// Controls identity preservation behavior during cloning.
/// <br/><br/>
/// Identity preservation ensures that if the same object instance appears multiple times
/// in the source graph (e.g., two properties pointing to the same object, or the same
/// object appearing twice in a collection), the cloned graph maintains this relationship
/// (both cloned references point to the same cloned instance).
/// <br/><br/>
/// By default, identity preservation is disabled for performance. Circular reference
/// detection is always enabled.
/// <br/><br/>
/// This attribute can be applied to:<br/>
/// - Classes/structs: Controls identity preservation for the entire type's subgraph<br/>
/// - Properties/fields: Controls identity preservation for that specific member's subgraph
/// <br/><br/>
/// Member-level attributes override type-level attributes, which override the default behaviour.
/// <br/><br/>
/// Exception: a type whose subgraph can contain circular references keeps tracking references even
/// when a member opts out with <c>[FastClonerPreserveIdentity(false)]</c>. Cycle detection and
/// identity preservation share one reference map, so dropping the map for such a type would turn a
/// cycle into unbounded recursion. Cycles inside the opted-out subgraph are therefore still cloned
/// as shared instances.
/// <br/><br/>
/// The override covers members cloned by generated code: clonable members, implicit (unannotated)
/// POCOs, collections, arrays and dictionaries. It does not reach members the generator cannot clone
/// itself — <c>object</c>, type parameters, and any other member handled by the runtime cloner — nor
/// non-public members on target frameworks below .NET 8, which are cloned through the runtime bridge.
/// Those paths track references on the runtime engine's own terms. Types registered in a
/// <c>FastClonerContext</c> are cloned by the context's own generated cloners, which decide reference
/// tracking from the registered-type graph, so a member-level opt-out inside a context graph can be
/// overridden by that analysis.
/// </summary>
/// <example>
/// <code>
/// // Enable identity preservation (default when attribute is present)
/// [FastClonerPreserveIdentity]
/// public class MyClass { }
/// 
/// // Explicitly enable
/// [FastClonerPreserveIdentity(true)]
/// public class MyClass { }
/// 
/// // Disable identity preservation for a member
/// [FastClonerPreserveIdentity(false)]
/// public List&lt;Item&gt; Items { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class FastClonerPreserveIdentityAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether identity preservation is enabled.
    /// Default is true when the attribute is applied.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Enables identity preservation for this type or member's subgraph.
    /// </summary>
    public FastClonerPreserveIdentityAttribute()
    {
    }

    /// <summary>
    /// Controls identity preservation for this type or member's subgraph.
    /// </summary>
    /// <param name="enabled">True to enable identity preservation, false to disable.</param>
    public FastClonerPreserveIdentityAttribute(bool enabled)
    {
        Enabled = enabled;
    }
}
