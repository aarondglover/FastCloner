using FastCloner.SourceGenerator.Shared;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FastCloner.Tests;

/// <summary>
/// Regression tests for generated-to-generated transitions that used to hand part of the graph to
/// the runtime cloner, which runs its own tracking state. A supplied
/// <c>FcGeneratedCloneState</c> must survive every transition between generated files and helpers.
/// <br/><br/>
/// Where a root also has a genuinely runtime-resolved member (a type parameter, an
/// <c>object</c> member) the operation-level overload is withheld and reported instead, so the state
/// is supplied directly through the generated <c>InternalFastDeepClone(source, state)</c> entry point
/// to test the transition itself.
/// </summary>
[SourceGeneratorCompatible]
public class RuntimeBoundaryTests
{
    /// <summary>
    /// A preserving discovery surface: naming a type here is what makes it expose
    /// <c>FastDeepClone(FastCloneOptions)</c>.
    /// </summary>
    [FastClonerDiscoverGenericArguments(PreserveIdentity = true)]
    public interface IPreservingSurface<T>
    {
    }

    /// <summary>
    /// The discovery points for this file. Only the roots named here expose the operation-level
    /// overload; everything they reach transitively only gains the capability to honor the state.
    /// </summary>
    private static readonly System.Type[] DiscoveryPoints =
    [
        typeof(IPreservingSurface<NonPublicCollectionRoot>),
        typeof(IPreservingSurface<TransitiveParentRoot>)
    ];

    public class BoundaryLeaf
    {
        public int Value { get; set; }
    }

    #region Non-public members

    [FastClonerClonable]
    [FastClonerPreserveIdentity(false)]
    public class NonPublicCollectionRoot
    {
        private List<BoundaryLeaf> _items = [];

        public List<BoundaryLeaf> Read() => _items;

        public void Seed(BoundaryLeaf item) => _items = [item, item];
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task NonPublicCollectionMember_ShouldShareTheSuppliedState()
    {
        BoundaryLeaf shared = new() { Value = 1 };
        NonPublicCollectionRoot original = new();
        original.Seed(shared);

        NonPublicCollectionRoot ordinary = original.FastDeepClone();
        await Assert.That(ordinary.Read()[0]).IsNotSameReferenceAs(ordinary.Read()[1])
            .Because("an ordinary call supplies no state");

        NonPublicCollectionRoot preserving = original.FastDeepClone(FastCloneOptions.PreserveIdentity);
        await Assert.That(preserving.Read()[0]).IsSameReferenceAs(preserving.Read()[1])
            .Because("a non-public member must be cloned by this file's generated helper with the supplied state, not by the runtime cloner");
    }

    #endregion

    #region Cloner<T> transitions

    [FastClonerClonable]
    public class BoundaryArgument
    {
        public List<BoundaryLeaf> Items { get; set; } = [];
    }

    [FastClonerClonable]
    [FastClonerPreserveIdentity(false)]
    [FastClonerInclude(typeof(BoundaryArgument))]
    public class IncludedArgumentHolder<T>
    {
        public T? Item { get; set; }
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task IncludedClonableArgument_ShouldShareTheSuppliedStateThroughCloner()
    {
        BoundaryLeaf shared = new() { Value = 2 };
        IncludedArgumentHolder<BoundaryArgument> original = new()
        {
            Item = new BoundaryArgument { Items = [shared, shared] }
        };

        IncludedArgumentHolder<BoundaryArgument> ordinary = original.FastDeepClone();
        await Assert.That(ordinary.Item!.Items[0]).IsNotSameReferenceAs(ordinary.Item.Items[1])
            .Because("an ordinary call supplies no state");

        IncludedArgumentHolder<BoundaryArgument>? preserving =
            original.InternalFastDeepClone(new FcGeneratedCloneState(preservingOperation: true));

        await Assert.That(preserving!.Item!.Items[0]).IsSameReferenceAs(preserving.Item.Items[1])
            .Because("the Cloner<T> branch for a clonable argument declared in this compilation must hand the state over");
    }

    [FastClonerClonable]
    [FastClonerPreserveIdentity(false)]
    [FastClonerInclude(typeof(BoundaryArgument))]
    public class IncludedArgumentListHolder<T>
    {
        public List<T> Items { get; set; } = [];
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task GenericCollectionOfTypeParameter_ShouldShareTheSuppliedStateThroughCloner()
    {
        BoundaryArgument shared = new() { Items = [] };
        IncludedArgumentListHolder<BoundaryArgument> original = new() { Items = [shared, shared] };

        IncludedArgumentListHolder<BoundaryArgument> ordinary = original.FastDeepClone();
        await Assert.That(ordinary.Items[0]).IsNotSameReferenceAs(ordinary.Items[1])
            .Because("an ordinary call supplies no state");

        IncludedArgumentListHolder<BoundaryArgument>? preserving =
            original.InternalFastDeepClone(new FcGeneratedCloneState(preservingOperation: true));

        await Assert.That(preserving!.Items[0]).IsSameReferenceAs(preserving.Items[1])
            .Because("a collection of the root's type parameter must be cloned through the generated Cloner<T> with the supplied state");
    }

    #endregion

    #region Public operation API exposure vs internal state capability

    [FastClonerClonable]
    public class CycleOnlyRoot
    {
        public string Name { get; set; } = string.Empty;
        public CycleOnlyRoot? Self { get; set; }
    }

    /// <summary>
    /// Needing a tracking state internally (here for circular references) is a capability, not a
    /// request for the public operation API.
    /// </summary>
    [Test]
    [SourceGeneratorCompatible]
    public async Task CycleOnlyRoot_ShouldNotGainTheOperationOverload()
    {
        CycleOnlyRoot original = new() { Name = "cycle" };
        original.Self = original;

        CycleOnlyRoot clone = original.FastDeepClone();

        await Assert.That(clone).IsNotSameReferenceAs(original);
        await Assert.That(clone.Self).IsSameReferenceAs(clone)
            .Because("the root's generated implementation already carries state machinery for the cycle");

        await Assert.That(HasOptionsOverload<CycleOnlyRoot>()).IsFalse()
            .Because("being state capable is not a reason to expose the operation-level API");
    }

    [FastClonerClonable]
    public class TransitiveChild
    {
        public List<BoundaryLeaf> Items { get; set; } = [];
    }

    [FastClonerClonable]
    [FastClonerPreserveIdentity(false)]
    public class TransitiveParentRoot
    {
        [FastClonerPreserveIdentity(false)]
        public TransitiveChild Child { get; set; } = new();
    }

    [FastClonerClonable]
    [FastClonerPreserveIdentity]
    public class IdentityConfiguredOnlyRoot
    {
        public List<BoundaryLeaf> Items { get; set; } = [];
    }

    /// <summary>
    /// An existing <c>[FastClonerPreserveIdentity]</c> root keeps exactly the generated surface it
    /// had before this feature. Its ordinary behaviour is unchanged; it simply is not part of the
    /// discovery feature that introduces the overload.
    /// </summary>
    [Test]
    [SourceGeneratorCompatible]
    public async Task IdentityConfiguredRoot_ShouldNotGainTheOperationOverload()
    {
        await Assert.That(DiscoveryPoints.Length).IsGreaterThan(0)
            .Because("the discovery points listed above are the only source of the overload");

        await Assert.That(HasOptionsOverload<IdentityConfiguredOnlyRoot>()).IsFalse()
            .Because("configuring identity predates this API and is not a reason to add a new public overload");

        BoundaryLeaf shared = new() { Value = 6 };
        IdentityConfiguredOnlyRoot original = new() { Items = [shared, shared] };

        IdentityConfiguredOnlyRoot clone = original.FastDeepClone();

        await Assert.That(clone.Items[0]).IsSameReferenceAs(clone.Items[1])
            .Because("the existing configured identity default keeps driving ordinary cloning exactly as before");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task TransitivelyCapableType_ShouldNotGainTheOperationOverload()
    {
        await Assert.That(HasOptionsOverload<TransitiveParentRoot>()).IsTrue()
            .Because("the root is named directly by a preserving discovery surface");

        await Assert.That(HasOptionsOverload<TransitiveChild>()).IsFalse()
            .Because("the child is only state capable because the parent's graph reaches it");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task TransitivelyCapableType_ShouldStillHonorTheParentsPreservingState()
    {
        BoundaryLeaf shared = new() { Value = 7 };
        TransitiveParentRoot original = new();
        original.Child.Items = [shared, shared];

        TransitiveParentRoot preserving = original.FastDeepClone(FastCloneOptions.PreserveIdentity);

        await Assert.That(preserving.Child.Items[0]).IsSameReferenceAs(preserving.Child.Items[1])
            .Because("the internal generated path must keep accepting the parent's preserving state");
    }

    #endregion

    #region Two-hop required graph

    [FastClonerClonable]
    public class HopLeaf
    {
        public int Value { get; set; }
    }

    [FastClonerClonable]
    public class HopB
    {
        public HopLeaf? Left { get; set; }
        public HopLeaf? Right { get; set; }
    }

    [FastClonerClonable]
    public class HopA
    {
        public HopB? B { get; set; }
    }

    [FastClonerClonable]
    public class HopRoot
    {
        public HopA? A { get; set; }
    }

    [FastClonerDiscoverGenericArguments(PreserveIdentity = true)]
    public interface IHopSurface<T>
    {
    }

    /// <summary>
    /// The requirement, the capability and the state have to survive <c>HopRoot → HopA → HopB</c>
    /// (two generated file hops), while only the directly required root grows the public API.
    /// </summary>
    [Test]
    [SourceGeneratorCompatible]
    public async Task TwoHopRequiredGraph_ShouldCarryTheRequirementAndTheState()
    {
        IHopSurface<HopRoot>? surface = null;
        await Assert.That(surface).IsNull();

        await Assert.That(HasOptionsOverload<HopRoot>()).IsTrue()
            .Because("the PreserveIdentity = true surface names this root directly");
        await Assert.That(HasOptionsOverload<HopA>()).IsFalse()
            .Because("it is required only because the root's graph reaches it");
        await Assert.That(HasOptionsOverload<HopB>()).IsFalse()
            .Because("it is required only because the root's graph reaches it two hops down");

        HopLeaf shared = new() { Value = 8 };
        HopRoot original = new() { A = new HopA { B = new HopB { Left = shared, Right = shared } } };

        HopRoot preserving = original.FastDeepClone(FastCloneOptions.PreserveIdentity);

        await Assert.That(preserving.A!.B!.Left).IsSameReferenceAs(preserving.A.B.Right)
            .Because("capability closure must reach the second generated hop");
    }

    #endregion

    private static bool HasOptionsOverload<T>()
    {
        string extensionTypeName = $"FastCloner.Tests.{typeof(T).Name}FastDeepCloneExtensions";
        System.Type? extensions = typeof(RuntimeBoundaryTests).Assembly.GetType(extensionTypeName)
            ?? throw new System.InvalidOperationException($"No generated extension class '{extensionTypeName}' for {typeof(T)}");

        return extensions.GetMethods().Any(method =>
            method.Name == "FastDeepClone" &&
            method.GetParameters().Length == 2 &&
            method.GetParameters()[1].ParameterType == typeof(FastCloneOptions));
    }
}
