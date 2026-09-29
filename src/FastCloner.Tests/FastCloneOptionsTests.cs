using FastCloner.SourceGenerator.Shared;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FastCloner.Tests;

/// <summary>
/// Tests for the explicit operation-level requirement
/// <c>FastDeepClone(FastCloneOptions.PreserveIdentity)</c>.
/// <br/><br/>
/// The option is the strongest identity-preservation requirement for that invocation: it must hold
/// for every helper path the generated graph uses (direct clonable members, implicit POCOs,
/// collections, dictionaries and arrays), while ordinary calls keep resolving the type's and
/// members' configured defaults exactly as before.
/// </summary>
[SourceGeneratorCompatible]
public class FastCloneOptionsTests
{
    public class OptionNode
    {
        public int Value { get; set; }
    }

    #region Collection member with a negative member override (the reported shape)

    [FastClonerClonable]
    public class CollectionNegativeRoot
    {
        [FastClonerPreserveIdentity(false)]
        public List<OptionNode> Nodes { get; set; } = [];
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task NegativeMemberOverride_ShouldKeepDefaultAndStillServeThePreservingOperation()
    {
        OptionNode shared = new() { Value = 1 };
        CollectionNegativeRoot original = new() { Nodes = [shared, shared] };

        CollectionNegativeRoot ordinary = original.FastDeepClone();
        await Assert.That(ordinary.Nodes[0]).IsNotSameReferenceAs(ordinary.Nodes[1])
            .Because("the member opted out of identity preservation for ordinary calls");

        CollectionNegativeRoot preserving = original.FastDeepClone(FastCloneOptions.PreserveIdentity);
        await Assert.That(preserving.Nodes[0]).IsSameReferenceAs(preserving.Nodes[1])
            .Because("an explicit operation-level requirement is the strongest requirement for that invocation");
        await Assert.That(preserving.Nodes[0]).IsNotSameReferenceAs(shared);
        await Assert.That(preserving.Nodes[0].Value).IsEqualTo(1);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task PreservingOperation_ShouldAlsoDeduplicateTheRootItself()
    {
        OptionNode shared = new() { Value = 2 };
        CollectionNegativeRoot original = new() { Nodes = [shared, shared] };

        CollectionNegativeRoot preserving = original.FastDeepClone(FastCloneOptions.PreserveIdentity);
        OptionNode[] again = [preserving.Nodes[0], preserving.Nodes[0]];

        await Assert.That(again[0]).IsSameReferenceAs(again[1]);
        await Assert.That(preserving).IsNotSameReferenceAs(original);
    }

    #endregion

    #region Direct clonable member

    [FastClonerClonable]
    public class ClonableChild
    {
        public string Name { get; set; } = string.Empty;
        public List<OptionNode> Items { get; set; } = [];
    }

    [FastClonerClonable]
    public class ClonableMemberRoot
    {
        [FastClonerPreserveIdentity(false)]
        public ClonableChild Child { get; set; } = new();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task ClonableMemberPath_ShouldHonorTheOperationLevelRequirement()
    {
        OptionNode shared = new() { Value = 3 };
        ClonableMemberRoot original = new()
        {
            Child = new ClonableChild { Name = "child", Items = [shared, shared] }
        };

        ClonableMemberRoot ordinary = original.FastDeepClone();
        await Assert.That(ordinary.Child.Items[0]).IsNotSameReferenceAs(ordinary.Child.Items[1])
            .Because("the member opted out for ordinary calls");

        ClonableMemberRoot preserving = original.FastDeepClone(FastCloneOptions.PreserveIdentity);
        await Assert.That(preserving.Child.Items[0]).IsSameReferenceAs(preserving.Child.Items[1])
            .Because("the explicit operation must reach through the clonable member path");
        await Assert.That(preserving.Child).IsNotSameReferenceAs(original.Child);
    }

    #endregion

    #region Implicit POCO member

    public class ImplicitChild
    {
        public OptionNode? Left { get; set; }
        public OptionNode? Right { get; set; }
    }

    [FastClonerClonable]
    public class ImplicitMemberRoot
    {
        [FastClonerPreserveIdentity(false)]
        public ImplicitChild Child { get; set; } = new();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task ImplicitMemberPath_ShouldHonorTheOperationLevelRequirement()
    {
        OptionNode shared = new() { Value = 4 };
        ImplicitMemberRoot original = new() { Child = new ImplicitChild { Left = shared, Right = shared } };

        ImplicitMemberRoot ordinary = original.FastDeepClone();
        await Assert.That(ordinary.Child.Left).IsNotSameReferenceAs(ordinary.Child.Right)
            .Because("the member opted out for ordinary calls");

        ImplicitMemberRoot preserving = original.FastDeepClone(FastCloneOptions.PreserveIdentity);
        await Assert.That(preserving.Child.Left).IsSameReferenceAs(preserving.Child.Right)
            .Because("the explicit operation must reach through the implicit POCO path");
        await Assert.That(preserving.Child).IsNotSameReferenceAs(original.Child);
    }

    #endregion

    #region Dictionary, array and init-only collection members

    [FastClonerClonable]
    public class DictionaryNegativeRoot
    {
        [FastClonerPreserveIdentity(false)]
        public Dictionary<string, OptionNode> Nodes { get; set; } = [];
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task DictionaryPath_ShouldHonorTheOperationLevelRequirement()
    {
        OptionNode shared = new() { Value = 5 };
        DictionaryNegativeRoot original = new() { Nodes = { ["first"] = shared, ["second"] = shared } };

        DictionaryNegativeRoot ordinary = original.FastDeepClone();
        await Assert.That(ordinary.Nodes["first"]).IsNotSameReferenceAs(ordinary.Nodes["second"]);

        DictionaryNegativeRoot preserving = original.FastDeepClone(FastCloneOptions.PreserveIdentity);
        await Assert.That(preserving.Nodes["first"]).IsSameReferenceAs(preserving.Nodes["second"])
            .Because("the explicit operation must reach through the dictionary path");
    }

    [FastClonerClonable]
    public class ArrayNegativeRoot
    {
        [FastClonerPreserveIdentity(false)]
        public OptionNode[] Nodes { get; set; } = [];
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task ArrayPath_ShouldHonorTheOperationLevelRequirement()
    {
        OptionNode shared = new() { Value = 6 };
        ArrayNegativeRoot original = new() { Nodes = [shared, shared] };

        ArrayNegativeRoot ordinary = original.FastDeepClone();
        await Assert.That(ordinary.Nodes[0]).IsNotSameReferenceAs(ordinary.Nodes[1]);

        ArrayNegativeRoot preserving = original.FastDeepClone(FastCloneOptions.PreserveIdentity);
        await Assert.That(preserving.Nodes[0]).IsSameReferenceAs(preserving.Nodes[1])
            .Because("the explicit operation must reach through the array path");
    }

    [FastClonerClonable]
    public class InitOnlyCollectionRoot
    {
        [FastClonerPreserveIdentity(false)]
        public List<OptionNode> Nodes { get; init; } = [];
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task InitOnlyCollectionPath_ShouldHonorTheOperationLevelRequirement()
    {
        OptionNode shared = new() { Value = 7 };
        InitOnlyCollectionRoot original = new() { Nodes = [shared, shared] };

        InitOnlyCollectionRoot ordinary = original.FastDeepClone();
        await Assert.That(ordinary.Nodes[0]).IsNotSameReferenceAs(ordinary.Nodes[1]);

        InitOnlyCollectionRoot preserving = original.FastDeepClone(FastCloneOptions.PreserveIdentity);
        await Assert.That(preserving.Nodes[0]).IsSameReferenceAs(preserving.Nodes[1]);
    }

    [FastClonerClonable]
    public class NestedCollectionNegativeRoot
    {
        [FastClonerPreserveIdentity(false)]
        public List<List<OptionNode>> Buckets { get; set; } = [];
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task NestedCollectionPath_ShouldHonorTheOperationLevelRequirement()
    {
        OptionNode shared = new() { Value = 8 };
        NestedCollectionNegativeRoot original = new() { Buckets = [[shared, shared]] };

        NestedCollectionNegativeRoot ordinary = original.FastDeepClone();
        await Assert.That(ordinary.Buckets[0][0]).IsNotSameReferenceAs(ordinary.Buckets[0][1]);

        NestedCollectionNegativeRoot preserving = original.FastDeepClone(FastCloneOptions.PreserveIdentity);
        await Assert.That(preserving.Buckets[0][0]).IsSameReferenceAs(preserving.Buckets[0][1])
            .Because("the explicit operation must reach through nested collection helpers");
    }

    #endregion

    #region Ordinary calls keep their configured defaults

    [FastClonerClonable]
    [FastClonerPreserveIdentity]
    public class PreservingDefaultRoot
    {
        public List<OptionNode> Defaulted { get; set; } = [];

        [FastClonerPreserveIdentity(false)]
        public List<OptionNode> OptedOut { get; set; } = [];
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task OrdinaryCall_ShouldResolveTypeDefaultAndMemberOverridePerMember()
    {
        OptionNode shared = new() { Value = 9 };
        PreservingDefaultRoot original = new()
        {
            Defaulted = [shared, shared],
            OptedOut = [shared, shared]
        };

        PreservingDefaultRoot clone = original.FastDeepClone();

        await Assert.That(clone.Defaulted[0]).IsSameReferenceAs(clone.Defaulted[1])
            .Because("the type-level default preserves identity for members without their own configuration");
        await Assert.That(clone.OptedOut[0]).IsNotSameReferenceAs(clone.OptedOut[1])
            .Because("a member-level false keeps overriding the type-level default for ordinary calls");

        PreservingDefaultRoot preserving = original.FastDeepClone(FastCloneOptions.PreserveIdentity);
        await Assert.That(preserving.Defaulted[0]).IsSameReferenceAs(preserving.Defaulted[1]);
        await Assert.That(preserving.OptedOut[0]).IsSameReferenceAs(preserving.OptedOut[1])
            .Because("the explicit operation overrides the member-level opt-out for that invocation");
    }

    [FastClonerClonable]
    public class MemberOptInRoot
    {
        [FastClonerPreserveIdentity]
        public List<OptionNode> OptedIn { get; set; } = [];
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task MemberOptIn_ShouldKeepTrackingForOrdinaryCalls()
    {
        OptionNode shared = new() { Value = 10 };
        MemberOptInRoot original = new() { OptedIn = [shared, shared] };

        MemberOptInRoot clone = original.FastDeepClone();
        await Assert.That(clone.OptedIn[0]).IsSameReferenceAs(clone.OptedIn[1])
            .Because("a member-level opt-in is existing behavior and must not change");
    }

    #endregion

    #region Runtime-delegated members (generated/runtime boundary)

    [FastClonerClonable]
    [FastClonerPreserveIdentity]
    public class ObjectMemberRoot
    {
        public object? First { get; set; }
        public object? Second { get; set; }
    }

    /// <summary>
    /// An <c>object</c>-typed member is resolved at runtime, so the generator cannot prove that one
    /// tracking state covers the whole graph. Rather than promising a guarantee it cannot keep, it
    /// withholds the operation-level entry point (and reports a warning explaining why). The ordinary
    /// entry point keeps its existing, best-effort behavior.
    /// </summary>
    [Test]
    public async Task ObjectTypedMember_ShouldWithholdThePreservingOperation()
    {
        await Assert.That(HasOptionsOverload("FastCloner.Tests.FastCloneOptionsTests.ObjectMemberRootFastDeepCloneExtensions")).IsFalse()
            .Because("an object-typed member is handed to the runtime cloner, which runs its own tracking state");

        OptionNode shared = new() { Value = 11 };
        ObjectMemberRoot original = new() { First = shared, Second = shared };

        ObjectMemberRoot ordinary = original.FastDeepClone();
        await Assert.That(ordinary.First).IsNotSameReferenceAs(ordinary.Second)
            .Because("the default fast path is unchanged");
        await Assert.That(((OptionNode)ordinary.First!).Value).IsEqualTo(11);
    }

    #endregion

    #region Capability surface

    [FastClonerClonable]
    public class NoIdentityConfigurationRoot
    {
        public OptionNode? Node { get; set; }
    }

    [FastClonerClonable]
    public class MemberConfigurationOnlyRoot
    {
        [FastClonerPreserveIdentity(false)]
        public OptionNode? Node { get; set; }
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task OperationOverload_ShouldExistWhereIdentityIsConfigured()
    {
        // A type that says nothing about identity keeps its surface unchanged: the option cannot be
        // requested where the generator was not asked to support it.
        await Assert.That(HasOptionsOverload("FastCloner.Tests.NoIdentityConfigurationRootFastDeepCloneExtensions")).IsFalse();

        // A type that configures identity - for the type or for one of its members - must expose it.
        await Assert.That(HasOptionsOverload("FastCloner.Tests.MemberConfigurationOnlyRootFastDeepCloneExtensions")).IsTrue();
        await Assert.That(HasOptionsOverload("FastCloner.Tests.CollectionNegativeRootFastDeepCloneExtensions")).IsTrue();
    }

    private static bool HasOptionsOverload(string extensionTypeName)
    {
        System.Type? extensions = typeof(FastCloneOptionsTests).Assembly.GetType(extensionTypeName);
        return extensions != null && extensions.GetMethods().Any(method =>
            method.Name == "FastDeepClone" &&
            method.GetParameters().Length == 2 &&
            method.GetParameters()[1].ParameterType == typeof(FastCloneOptions));
    }

    #endregion
}
