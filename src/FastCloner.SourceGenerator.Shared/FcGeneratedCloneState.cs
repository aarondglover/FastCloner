using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FastCloner.SourceGenerator.Shared;

/// <summary>
/// State for tracking circular references during cloning.
/// Used by source-generated clone methods to detect and handle cycles.
/// Thread-safe for concurrent clone operations.
/// </summary>
public sealed class FcGeneratedCloneState
{
    /// <summary>
    /// State that records nothing, used to express an explicit "do not preserve identity"
    /// request for a member marked <c>[FastClonerPreserveIdentity(false)]</c>.
    /// <br/><br/>
    /// Passing <c>null</c> cannot express this: <c>null</c> means "no state was supplied",
    /// which makes a type whose own default is to track references allocate tracking state
    /// and re-enable identity preservation for the member's subgraph. Passing this instance
    /// instead keeps tracking off for the whole subgraph.
    /// <br/><br/>
    /// Types whose subgraph can contain cycles ignore the request and allocate real state,
    /// because circular reference detection must keep working.
    /// </summary>
    public static readonly FcGeneratedCloneState NoReferenceTracking = new FcGeneratedCloneState(trackReferences: false);

    private readonly bool _trackReferences;
    private readonly ConcurrentDictionary<object, object> _knownRefs = new ConcurrentDictionary<object, object>(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Creates a state that tracks cloned references.
    /// </summary>
    public FcGeneratedCloneState()
        : this(trackReferences: true)
    {
    }

    private FcGeneratedCloneState(bool trackReferences)
    {
        _trackReferences = trackReferences;
    }

    /// <summary>
    /// False for <see cref="NoReferenceTracking"/>: the state ignores every recorded or
    /// requested reference, so no identity is preserved through it.
    /// </summary>
    public bool TrackReferences => _trackReferences;

    /// <summary>
    /// Registers a known reference mapping from original to clone.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddKnownRef(object original, object clone)
    {
        if (!_trackReferences || original == null)
        {
            return;
        }

        _knownRefs.TryAdd(original, clone);
    }

    /// <summary>
    /// Gets the previously cloned object for the given original, if any.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? GetKnownRef(object original)
    {
        if (!_trackReferences || original == null) return null;
        return _knownRefs.TryGetValue(original, out var clone) ? clone : null;
    }

    /// <summary>
    /// Reference equality comparer for proper circular reference detection.
    /// Uses object identity (ReferenceEquals) rather than value equality.
    /// </summary>
    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
        
        bool IEqualityComparer<object>.Equals(object? x, object? y) => ReferenceEquals(x, y);
        int IEqualityComparer<object>.GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

