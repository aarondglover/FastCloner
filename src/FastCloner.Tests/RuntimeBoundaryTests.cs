using FastCloner.SourceGenerator.Shared;
using System.Collections.Generic;
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
}
