using System;

namespace FastCloner.SourceGenerator.Shared;

/// <summary>
/// Marks a generic declaration or generic API surface as a FastCloner source-generation
/// discovery point. When a closed usage of the marked surface appears in the compilation,
/// the closed generic arguments carried by that usage are treated as clone roots and fed
/// into the same analysis and code generation pipeline used by
/// <see cref="FastClonerClonableAttribute"/>.
/// <br/><br/>
/// Discovery is a compile-time concern only; it does not change how cloning works. Types
/// discovered this way do not need to carry any FastCloner attribute themselves.
/// <br/><br/>
/// Supported surfaces: generic classes, structs, records, interfaces, delegates and
/// methods (including methods whose containing type is generic). For a marked method on a
/// generic containing type both the method's own closed type arguments and the closed type
/// arguments of the containing type are discovered.
/// <br/><br/>
/// Open or unbound generic arguments (for example <c>IContainer&lt;T&gt;</c> inside a generic
/// declaration) produce no roots. Arguments that report no cloneable state (safe types such
/// as <see cref="string"/>, delegates and similar "do not clone" types) are ignored.
/// </summary>
/// <example>
/// <code>
/// [FastClonerDiscoverGenericArguments]
/// public interface IContainer&lt;T&gt;
/// {
/// }
///
/// // Anywhere in the compilation:
/// IContainer&lt;Order&gt; container = ...;
///
/// // Order is now a source-generation root:
/// Order clone = order.FastDeepClone();
/// </code>
/// </example>
[AttributeUsage(
    AttributeTargets.Class |
    AttributeTargets.Struct |
    AttributeTargets.Interface |
    AttributeTargets.Delegate |
    AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = false)]
public sealed class FastClonerDiscoverGenericArgumentsAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether clone roots discovered through this API surface should preserve
    /// object identity within their cloned subgraph, using FastCloner's existing
    /// identity-tracking semantics. Default is <c>false</c>, which retains the default
    /// FastCloner behavior.
    /// </summary>
    public bool PreserveIdentity { get; set; }
}
