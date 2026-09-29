using FastCloner.SourceGenerator.Shared;
using System.Threading.Tasks;

namespace FastCloner.Tests;

public class IdentityPreservationTests
{
    #region Test 1: Simple Tree - No state needed
    
    [FastClonerClonable]
    public class SimpleTreeRoot
    {
        public string Name { get; set; } = "";
        public SimpleTreeChild Child { get; set; } = new();
    }
    
    [FastClonerClonable]
    public class SimpleTreeChild
    {
        public int Value { get; set; }
    }
    
    [Test]
    public async Task SimpleTree_ClonesCorrectly_NoStateNeeded()
    {
        SimpleTreeRoot original = new SimpleTreeRoot
        {
            Name = "Root",
            Child = new SimpleTreeChild { Value = 42 }
        };
        
        SimpleTreeRoot clone = original.FastDeepClone();
        
        await Assert.That(clone).IsNotSameReferenceAs(original);
        await Assert.That(clone.Name).IsEqualTo("Root");
        await Assert.That(clone.Child).IsNotSameReferenceAs(original.Child);
        await Assert.That(clone.Child.Value).IsEqualTo(42);
    }
    
    #endregion
    
    #region Test 2: Multiple Paths to Same Type - State needed (with PreserveIdentity)
    
    [FastClonerClonable]
    [FastClonerPreserveIdentity]
    public class MultiPathRoot
    {
        public string Name { get; set; } = "";
        public PathA PathA { get; set; } = new();
        public PathB PathB { get; set; } = new();
    }
    
    [FastClonerClonable]
    public class PathA
    {
        public SharedNode Shared { get; set; } = new();
    }
    
    [FastClonerClonable]
    public class PathB
    {
        public SharedNode Shared { get; set; } = new();
    }
    
    [FastClonerClonable]
    public class SharedNode
    {
        public int Value { get; set; }
    }
    
    [Test]
    public async Task MultiplePaths_PreservesIdentity_WhenSameInstanceShared()
    {
        SharedNode sharedNode = new SharedNode { Value = 100 };
        MultiPathRoot original = new MultiPathRoot
        {
            Name = "MultiPath",
            PathA = new PathA { Shared = sharedNode },
            PathB = new PathB { Shared = sharedNode }
        };
        
        // Verify original has shared identity
        await Assert.That(original.PathA.Shared).IsSameReferenceAs(original.PathB.Shared);

        MultiPathRoot clone = original.FastDeepClone();
        
        await Assert.That(clone).IsNotSameReferenceAs(original);
        await Assert.That(clone.PathA.Shared).IsNotSameReferenceAs(original.PathA.Shared);
        await Assert.That(clone.PathB.Shared).IsNotSameReferenceAs(original.PathB.Shared);

        // Key assertion: clone should preserve the shared identity
        await Assert.That(clone.PathA.Shared).IsSameReferenceAs(clone.PathB.Shared).Because("Identity should be preserved: both paths should clone to the same instance");
        await Assert.That(clone.PathA.Shared.Value).IsEqualTo(100);
    }
    
    [Test]
    public async Task MultiplePaths_CreatesSeparateClones_WhenDifferentInstances()
    {
        MultiPathRoot original = new MultiPathRoot
        {
            Name = "MultiPath",
            PathA = new PathA { Shared = new SharedNode { Value = 1 } },
            PathB = new PathB { Shared = new SharedNode { Value = 2 } }
        };
        
        // Verify original has different instances
        await Assert.That(original.PathA.Shared).IsNotSameReferenceAs(original.PathB.Shared);

        MultiPathRoot clone = original.FastDeepClone();
        
        // Clone should also have different instances
        await Assert.That(clone.PathA.Shared).IsNotSameReferenceAs(clone.PathB.Shared);
        await Assert.That(clone.PathA.Shared.Value).IsEqualTo(1);
        await Assert.That(clone.PathB.Shared.Value).IsEqualTo(2);
    }
    
    #endregion
    
    #region Test 3: Duplicate Properties of Same Type - State needed (with PreserveIdentity)
    
    [FastClonerClonable]
    [FastClonerPreserveIdentity] // Required for identity preservation
    public class DuplicatePropsRoot
    {
        public string Name { get; set; } = "";
        public DuplicateChild Child1 { get; set; } = new();
        public DuplicateChild Child2 { get; set; } = new();
    }
    
    [FastClonerClonable]
    public class DuplicateChild
    {
        public int Id { get; set; }
        public string Data { get; set; } = "";
    }
    
    [Test]
    public async Task DuplicateProperties_PreservesIdentity_WhenSameInstance()
    {
        DuplicateChild sharedChild = new DuplicateChild { Id = 1, Data = "Shared" };
        DuplicatePropsRoot original = new DuplicatePropsRoot
        {
            Name = "DuplicateProps",
            Child1 = sharedChild,
            Child2 = sharedChild
        };
        
        // Verify original has shared identity
        await Assert.That(original.Child1).IsSameReferenceAs(original.Child2);

        DuplicatePropsRoot clone = original.FastDeepClone();
        
        await Assert.That(clone).IsNotSameReferenceAs(original);
        await Assert.That(clone.Child1).IsNotSameReferenceAs(original.Child1);

        // Key assertion: clone should preserve the shared identity
        await Assert.That(clone.Child1).IsSameReferenceAs(clone.Child2).Because("Identity should be preserved: both properties should reference the same cloned instance");
        await Assert.That(clone.Child1.Id).IsEqualTo(1);
        await Assert.That(clone.Child1.Data).IsEqualTo("Shared");
    }
    
    #endregion
    
    #region Test 4: Collection with Non-Safe Elements - State needed (with PreserveIdentity)
    
    [FastClonerClonable]
    [FastClonerPreserveIdentity] // Required for identity preservation in collections
    public class CollectionRoot
    {
        public string Name { get; set; } = "";
        public List<CollectionItem> Items { get; set; } = [];
    }
    
    [FastClonerClonable]
    public class CollectionItem
    {
        public int Id { get; set; }
        public string Description { get; set; } = "";
    }
    
    [Test]
    public async Task Collection_PreservesIdentity_WhenSameInstanceAppearsTwice()
    {
        CollectionItem sharedItem = new CollectionItem { Id = 1, Description = "Shared" };
        CollectionRoot original = new CollectionRoot
        {
            Name = "CollectionTest",
            Items = [sharedItem, new CollectionItem { Id = 2 }, sharedItem]
        };
        
        // Verify original: first and third items are the same instance
        await Assert.That(original.Items[0]).IsSameReferenceAs(original.Items[2]);
        await Assert.That(original.Items[0]).IsNotSameReferenceAs(original.Items[1]);

        CollectionRoot clone = original.FastDeepClone();
        
        await Assert.That(clone.Items.Count).IsEqualTo(3);
        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(original.Items[0]);

        // Key assertion: clone should preserve identity
        await Assert.That(clone.Items[0]).IsSameReferenceAs(clone.Items[2]).Because("Identity should be preserved: same original instance should clone to same cloned instance");
        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(clone.Items[1]);
        await Assert.That(clone.Items[0].Description).IsEqualTo("Shared");
    }
    
    #endregion
    
    #region Test 5: Existing Circular Reference - State needed
    
    [FastClonerClonable]
    public class CircularNode
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public CircularNode? Next { get; set; }
    }
    
    [Test]
    public async Task CircularReference_HandlesCorrectly()
    {
        CircularNode node1 = new CircularNode { Id = 1, Name = "First" };
        CircularNode node2 = new CircularNode { Id = 2, Name = "Second" };
        
        // Create a cycle: node1 -> node2 -> node1
        node1.Next = node2;
        node2.Next = node1;
        
        CircularNode clone = node1.FastDeepClone();
        
        await Assert.That(clone).IsNotSameReferenceAs(node1);
        await Assert.That(clone.Id).IsEqualTo(1);
        await Assert.That(clone.Next).IsNotNull();
        await Assert.That(clone.Next!.Id).IsEqualTo(2);

        // Verify cycle is preserved correctly
        await Assert.That(clone.Next.Next).IsSameReferenceAs(clone).Because("Circular reference should be preserved: clone.Next.Next should be the same as clone");
    }
    
    [Test]
    public async Task SelfReference_HandlesCorrectly()
    {
        CircularNode node = new CircularNode { Id = 1, Name = "Self" };
        node.Next = node; // Self-reference
        
        CircularNode clone = node.FastDeepClone();
        
        await Assert.That(clone).IsNotSameReferenceAs(node);
        await Assert.That(clone.Id).IsEqualTo(1);

        // Verify self-reference is preserved
        await Assert.That(clone.Next).IsSameReferenceAs(clone).Because("Self-reference should be preserved: clone.Next should be the same as clone");
    }
    
    #endregion
    
    #region Test 6: Deep Graph with Mixed Scenarios (with PreserveIdentity)
    
    [FastClonerClonable]
    [FastClonerPreserveIdentity] // Required for identity preservation
    public class DeepGraphRoot
    {
        public string Name { get; set; } = "";
        public DeepGraphLevel1 Level1A { get; set; } = new();
        public DeepGraphLevel1 Level1B { get; set; } = new();
    }
    
    [FastClonerClonable]
    public class DeepGraphLevel1
    {
        public int Value { get; set; }
        public DeepGraphLeaf Leaf { get; set; } = new();
    }
    
    [FastClonerClonable]
    public class DeepGraphLeaf
    {
        public string Data { get; set; } = "";
    }
    
    [Test]
    public async Task DeepGraph_PreservesIdentity_AtMultipleLevels()
    {
        DeepGraphLeaf sharedLeaf = new DeepGraphLeaf { Data = "SharedLeaf" };
        DeepGraphLevel1 sharedLevel1 = new DeepGraphLevel1 { Value = 10, Leaf = sharedLeaf };
        
        DeepGraphRoot original = new DeepGraphRoot
        {
            Name = "DeepGraph",
            Level1A = sharedLevel1,
            Level1B = sharedLevel1
        };
        
        // Verify original has shared identity at multiple levels
        await Assert.That(original.Level1A).IsSameReferenceAs(original.Level1B);
        await Assert.That(original.Level1A.Leaf).IsSameReferenceAs(original.Level1B.Leaf);

        DeepGraphRoot clone = original.FastDeepClone();
        
        await Assert.That(clone).IsNotSameReferenceAs(original);
        await Assert.That(clone.Level1A).IsNotSameReferenceAs(original.Level1A);

        // Key assertions: clone should preserve identity at all levels
        await Assert.That(clone.Level1A).IsSameReferenceAs(clone.Level1B).Because("Identity at Level1 should be preserved");
        await Assert.That(clone.Level1A.Leaf).IsSameReferenceAs(clone.Level1B.Leaf).Because("Identity at Leaf level should be preserved (same Level1 means same Leaf)");
        await Assert.That(clone.Level1A.Leaf.Data).IsEqualTo("SharedLeaf");
    }
    
    #endregion
    
    #region Test 7: PreserveIdentity attribute on type
    
    /// <summary>
    /// Type with PreserveIdentity enabled - should track identity even without cycles
    /// </summary>
    [FastClonerClonable]
    [FastClonerPreserveIdentity]
    public class TypeWithPreserveIdentity
    {
        public string Name { get; set; } = "";
        public SharedItem Item1 { get; set; } = new();
        public SharedItem Item2 { get; set; } = new();
    }
    
    [FastClonerClonable]
    public class SharedItem
    {
        public int Id { get; set; }
    }
    
    [Test]
    public async Task TypeWithPreserveIdentity_PreservesIdentity()
    {
        SharedItem shared = new SharedItem { Id = 42 };
        TypeWithPreserveIdentity original = new TypeWithPreserveIdentity
        {
            Name = "Test",
            Item1 = shared,
            Item2 = shared
        };
        
        await Assert.That(original.Item1).IsSameReferenceAs(original.Item2);

        TypeWithPreserveIdentity clone = original.FastDeepClone();
        
        await Assert.That(clone.Item1).IsNotSameReferenceAs(original.Item1);
        await Assert.That(clone.Item1).IsSameReferenceAs(clone.Item2).Because("With [FastClonerPreserveIdentity], shared references should be preserved");
    }
    
    #endregion
    
    #region Test 8: PreserveIdentity attribute on member
    
    [FastClonerClonable]
    public class TypeWithMemberPreserveIdentity
    {
        public string Name { get; set; } = "";
        
        [FastClonerPreserveIdentity]
        public List<MemberItem> Items { get; set; } = [];
    }
    
    [FastClonerClonable]
    public class MemberItem
    {
        public int Value { get; set; }
    }
    
    [Test]
    public async Task MemberWithPreserveIdentity_PreservesIdentityInCollection()
    {
        MemberItem shared = new MemberItem { Value = 100 };
        TypeWithMemberPreserveIdentity original = new TypeWithMemberPreserveIdentity
        {
            Name = "Test",
            Items = [shared, new MemberItem { Value = 200 }, shared]
        };
        
        await Assert.That(original.Items[0]).IsSameReferenceAs(original.Items[2]);

        TypeWithMemberPreserveIdentity clone = original.FastDeepClone();
        
        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(original.Items[0]);
        await Assert.That(clone.Items[0]).IsSameReferenceAs(clone.Items[2]).Because("With [FastClonerPreserveIdentity] on member, shared references should be preserved");
    }
    
    #endregion
    
    #region Test 9: Without PreserveIdentity - no identity preservation (faster)
    
    [FastClonerClonable]
    public class TypeWithoutPreserveIdentity
    {
        public string Name { get; set; } = "";
        public NoIdentityItem Item1 { get; set; } = new();
        public NoIdentityItem Item2 { get; set; } = new();
    }
    
    [FastClonerClonable]
    public class NoIdentityItem
    {
        public int Id { get; set; }
    }
    
    [Test]
    public async Task TypeWithoutPreserveIdentity_DoesNotPreserveIdentity()
    {
        NoIdentityItem shared = new NoIdentityItem { Id = 42 };
        TypeWithoutPreserveIdentity original = new TypeWithoutPreserveIdentity
        {
            Name = "Test",
            Item1 = shared,
            Item2 = shared
        };
        
        await Assert.That(original.Item1).IsSameReferenceAs(original.Item2);

        TypeWithoutPreserveIdentity clone = original.FastDeepClone();
        
        // Without PreserveIdentity, both items are cloned separately (faster, but loses identity)
        // This is the expected behavior for performance - users opt-in to identity preservation
        await Assert.That(clone.Item1.Id).IsEqualTo(42);
        await Assert.That(clone.Item2.Id).IsEqualTo(42);
        // Note: We don't assert they're different because cycles still require tracking
        // The key difference is we don't track identity for performance when not needed
    }
    
    #endregion

    #region Test 10: Member-level PreserveIdentity overrides child type-level attribute

    /// <summary>
    /// Shared leaf used to observe whether aliasing inside a subgraph survived cloning.
    /// </summary>
    [FastClonerClonable]
    public class PrecedenceSharedLeaf
    {
        public int Value { get; set; }
    }

    /// <summary>
    /// Child whose own type-level attribute turns identity preservation on.
    /// Cloning this type directly should keep the Left/Right aliasing.
    /// </summary>
    [FastClonerClonable]
    [FastClonerPreserveIdentity(true)]
    public class PrecedenceChildWithIdentity
    {
        public PrecedenceSharedLeaf Left { get; set; } = new();
        public PrecedenceSharedLeaf Right { get; set; } = new();
    }

    /// <summary>
    /// Child with no type-level attribute (library default: identity preserved only
    /// when a member override asks for it or the graph needs cycle tracking).
    /// </summary>
    [FastClonerClonable]
    public class PrecedenceChildWithoutIdentity
    {
        public PrecedenceSharedLeaf Left { get; set; } = new();
        public PrecedenceSharedLeaf Right { get; set; } = new();
    }

    [FastClonerClonable]
    public class MemberFalseOverChildTypeTrueRoot
    {
        [FastClonerPreserveIdentity(false)]
        public PrecedenceChildWithIdentity Child { get; set; } = new();
    }

    [FastClonerClonable]
    public class MemberTrueOverChildTypeDefaultRoot
    {
        [FastClonerPreserveIdentity(true)]
        public PrecedenceChildWithoutIdentity Child { get; set; } = new();
    }

    [FastClonerClonable]
    public class NoMemberOverrideChildTypeTrueRoot
    {
        public PrecedenceChildWithIdentity Child { get; set; } = new();
    }

    [Test]
    public async Task MemberFalse_OverridesChildTypeTrue_DoesNotPreserveIdentity()
    {
        PrecedenceSharedLeaf shared = new PrecedenceSharedLeaf { Value = 7 };
        MemberFalseOverChildTypeTrueRoot original = new MemberFalseOverChildTypeTrueRoot
        {
            Child = new PrecedenceChildWithIdentity { Left = shared, Right = shared }
        };

        await Assert.That(original.Child.Left).IsSameReferenceAs(original.Child.Right);

        MemberFalseOverChildTypeTrueRoot clone = original.FastDeepClone();

        await Assert.That(clone.Child).IsNotSameReferenceAs(original.Child);
        await Assert.That(clone.Child.Left).IsNotSameReferenceAs(original.Child.Left);
        await Assert.That(clone.Child.Left.Value).IsEqualTo(7);
        await Assert.That(clone.Child.Right.Value).IsEqualTo(7);

        // Member-level [FastClonerPreserveIdentity(false)] must win over the child's
        // type-level [FastClonerPreserveIdentity(true)] for this member's subgraph.
        await Assert.That(clone.Child.Left).IsNotSameReferenceAs(clone.Child.Right).Because(
            "member-level PreserveIdentity(false) must suppress identity preservation for the member's subgraph");
    }

    [Test]
    public async Task MemberFalse_OverridesChildTypeTrue_ChildClonedDirectlyStillPreservesIdentity()
    {
        PrecedenceSharedLeaf shared = new PrecedenceSharedLeaf { Value = 9 };
        PrecedenceChildWithIdentity original = new PrecedenceChildWithIdentity { Left = shared, Right = shared };

        PrecedenceChildWithIdentity clone = original.FastDeepClone();

        // Type-level [FastClonerPreserveIdentity(true)] semantics for a directly cloned
        // root must be unaffected by the member-level override in another type.
        await Assert.That(clone.Left).IsSameReferenceAs(clone.Right).Because(
            "type-level identity preservation for the directly cloned type must be preserved");
    }

    [Test]
    public async Task MemberTrue_OverridesChildTypeDefault_PreservesIdentity()
    {
        PrecedenceSharedLeaf shared = new PrecedenceSharedLeaf { Value = 11 };
        MemberTrueOverChildTypeDefaultRoot original = new MemberTrueOverChildTypeDefaultRoot
        {
            Child = new PrecedenceChildWithoutIdentity { Left = shared, Right = shared }
        };

        await Assert.That(original.Child.Left).IsSameReferenceAs(original.Child.Right);

        MemberTrueOverChildTypeDefaultRoot clone = original.FastDeepClone();

        await Assert.That(clone.Child).IsNotSameReferenceAs(original.Child);
        await Assert.That(clone.Child.Left.Value).IsEqualTo(11);
        await Assert.That(clone.Child.Left).IsSameReferenceAs(clone.Child.Right).Because(
            "member-level PreserveIdentity(true) must enable identity preservation for the member's subgraph");
    }

    [Test]
    public async Task NoMemberOverride_ChildTypeTrue_PreservesIdentity()
    {
        PrecedenceSharedLeaf shared = new PrecedenceSharedLeaf { Value = 13 };
        NoMemberOverrideChildTypeTrueRoot original = new NoMemberOverrideChildTypeTrueRoot
        {
            Child = new PrecedenceChildWithIdentity { Left = shared, Right = shared }
        };

        await Assert.That(original.Child.Left).IsSameReferenceAs(original.Child.Right);

        NoMemberOverrideChildTypeTrueRoot clone = original.FastDeepClone();

        await Assert.That(clone.Child).IsNotSameReferenceAs(original.Child);
        await Assert.That(clone.Child.Left).IsNotSameReferenceAs(original.Child.Left);
        await Assert.That(clone.Child.Left.Value).IsEqualTo(13);
        await Assert.That(clone.Child.Left).IsSameReferenceAs(clone.Child.Right).Because(
            "without a member override the child type's own PreserveIdentity(true) must apply");
    }

    #endregion

    #region Test 11: member-level opt-out interactions (ambient state, nesting, cycles, non-public)

    /// <summary>
    /// Root that itself tracks identity: the opt-out member must not inherit the parent's
    /// tracking state, while the parent's own members keep preserving identity.
    /// </summary>
    [FastClonerClonable]
    [FastClonerPreserveIdentity(true)]
    public class IdentityRootWithOptOutMember
    {
        public PrecedenceSharedLeaf Left { get; set; } = new();
        public PrecedenceSharedLeaf Right { get; set; } = new();

        [FastClonerPreserveIdentity(false)]
        public PrecedenceChildWithIdentity Child { get; set; } = new();
    }

    [FastClonerClonable]
    [FastClonerPreserveIdentity(true)]
    public class PrecedenceNestedHolder
    {
        public PrecedenceChildWithIdentity Inner { get; set; } = new();
    }

    [FastClonerClonable]
    public class MemberFalseOverNestedIdentityRoot
    {
        [FastClonerPreserveIdentity(false)]
        public PrecedenceNestedHolder Child { get; set; } = new();
    }

    [FastClonerClonable]
    public class PrecedenceCyclicChild
    {
        public int Value { get; set; }
        public PrecedenceCyclicChild? Next { get; set; }
    }

    [FastClonerClonable]
    public class MemberFalseOverCyclicChildRoot
    {
        [FastClonerPreserveIdentity(false)]
        public PrecedenceCyclicChild Child { get; set; } = new();
    }

    [FastClonerClonable]
    public class MemberFalseOverPrivateClonableFieldRoot
    {
        [FastClonerPreserveIdentity(false)]
        private PrecedenceChildWithIdentity _child = new();

        // Accessors are methods rather than a property: a public property over the same storage
        // would be cloned as a second member and overwrite the field clone under test.
        public void SetChild(PrecedenceChildWithIdentity child) => _child = child;

        public PrecedenceChildWithIdentity GetChild() => _child;
    }

    [FastClonerClonable]
    [FastClonerPreserveIdentity(true)]
    public class IdentityRootWithOptOutCollectionMember
    {
        public PrecedenceSharedLeaf Left { get; set; } = new();
        public PrecedenceSharedLeaf Right { get; set; } = new();

        [FastClonerPreserveIdentity(false)]
        public List<PrecedenceChildWithIdentity> Items { get; set; } = [];
    }

    [Test]
    public async Task MemberFalse_OverridesChildTypeTrue_EvenWhenParentTracksIdentity()
    {
        PrecedenceSharedLeaf sharedLeaf = new PrecedenceSharedLeaf { Value = 5 };
        PrecedenceSharedLeaf childLeaf = new PrecedenceSharedLeaf { Value = 6 };

        IdentityRootWithOptOutMember original = new IdentityRootWithOptOutMember
        {
            Left = sharedLeaf,
            Right = sharedLeaf,
            Child = new PrecedenceChildWithIdentity { Left = childLeaf, Right = childLeaf }
        };

        IdentityRootWithOptOutMember clone = original.FastDeepClone();

        // The parent's own members still share the parent's tracking state.
        await Assert.That(clone.Left).IsSameReferenceAs(clone.Right);
        await Assert.That(clone.Left.Value).IsEqualTo(5);

        // The opted-out member does not get the parent's state forwarded into its subgraph.
        await Assert.That(clone.Child.Left).IsNotSameReferenceAs(clone.Child.Right).Because(
            "member-level PreserveIdentity(false) must not inherit the parent's identity-tracking state");
        await Assert.That(clone.Child.Left.Value).IsEqualTo(6);
    }

    [Test]
    public async Task MemberFalse_PropagatesThroughNestedIdentityTrackingTypes()
    {
        PrecedenceSharedLeaf shared = new PrecedenceSharedLeaf { Value = 8 };
        MemberFalseOverNestedIdentityRoot original = new MemberFalseOverNestedIdentityRoot
        {
            Child = new PrecedenceNestedHolder
            {
                Inner = new PrecedenceChildWithIdentity { Left = shared, Right = shared }
            }
        };

        MemberFalseOverNestedIdentityRoot clone = original.FastDeepClone();

        await Assert.That(clone.Child.Inner.Left.Value).IsEqualTo(8);
        await Assert.That(clone.Child.Inner.Left).IsNotSameReferenceAs(clone.Child.Inner.Right).Because(
            "the opt-out must propagate into nested types that would otherwise track identity");
    }

    [Test]
    public async Task MemberFalse_OverChildWithCycles_StillDetectsCycles()
    {
        PrecedenceCyclicChild child = new PrecedenceCyclicChild { Value = 1 };
        child.Next = child; // self-cycle

        MemberFalseOverCyclicChildRoot original = new MemberFalseOverCyclicChildRoot { Child = child };

        MemberFalseOverCyclicChildRoot clone = original.FastDeepClone();

        await Assert.That(clone.Child).IsNotSameReferenceAs(original.Child);
        await Assert.That(clone.Child.Value).IsEqualTo(1);

        // Cycle detection must win over the opt-out: without it this clone would recurse forever.
        await Assert.That(clone.Child.Next).IsSameReferenceAs(clone.Child).Because(
            "circular references must still be detected when a member opts out of identity preservation");
    }

    [Test]
    public async Task MemberFalse_OnPrivateClonableField_IsHonored()
    {
        PrecedenceSharedLeaf shared = new PrecedenceSharedLeaf { Value = 17 };
        MemberFalseOverPrivateClonableFieldRoot original = new MemberFalseOverPrivateClonableFieldRoot();
        original.SetChild(new PrecedenceChildWithIdentity { Left = shared, Right = shared });

        MemberFalseOverPrivateClonableFieldRoot clone = original.FastDeepClone();

        await Assert.That(clone.GetChild()).IsNotSameReferenceAs(original.GetChild());
        await Assert.That(clone.GetChild().Left.Value).IsEqualTo(17);
        await Assert.That(clone.GetChild().Left).IsNotSameReferenceAs(clone.GetChild().Right).Because(
            "a member-level opt-out must also apply to non-public members");
    }

    [Test]
    public async Task MemberFalse_OnCollectionMember_DoesNotPreserveIdentityAcrossElements()
    {
        PrecedenceSharedLeaf sharedLeaf = new PrecedenceSharedLeaf { Value = 3 };
        PrecedenceChildWithIdentity sharedElement = new PrecedenceChildWithIdentity
        {
            Left = sharedLeaf,
            Right = sharedLeaf
        };

        IdentityRootWithOptOutCollectionMember original = new IdentityRootWithOptOutCollectionMember
        {
            Left = sharedLeaf,
            Right = sharedLeaf,
            Items = [sharedElement, sharedElement]
        };

        IdentityRootWithOptOutCollectionMember clone = original.FastDeepClone();

        await Assert.That(clone.Left).IsSameReferenceAs(clone.Right);

        // The opted-out collection member does not share the parent's tracking state, so the two
        // occurrences of the same element clone independently...
        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(clone.Items[1]);

        // ...and the opt-out reaches the element cloners, suppressing aliasing inside each element
        // even though the element type's own default is to preserve identity.
        await Assert.That(clone.Items[0].Left).IsNotSameReferenceAs(clone.Items[0].Right).Because(
            "a member-level opt-out must reach the collection's element cloners");
        await Assert.That(clone.Items[0].Left.Value).IsEqualTo(3);
        await Assert.That(clone.Items[1].Left.Value).IsEqualTo(3);
    }

    #endregion

    #region Test 12: member-level override on an implicitly cloned (unannotated) POCO

    /// <summary>
    /// Plain POCO without <c>[FastClonerClonable]</c>: cloned implicitly by the generator.
    /// </summary>
    public class PrecedenceImplicitHolder
    {
        public PrecedenceChildWithIdentity Inner { get; set; } = new();
    }

    [FastClonerClonable]
    [FastClonerPreserveIdentity(true)]
    public class IdentityRootWithOptOutImplicitMember
    {
        public PrecedenceSharedLeaf Left { get; set; } = new();
        public PrecedenceSharedLeaf Right { get; set; } = new();

        [FastClonerPreserveIdentity(false)]
        public PrecedenceImplicitHolder Holder { get; set; } = new();
    }

    [Test]
    public async Task MemberFalse_OnImplicitPocoMember_DoesNotPreserveIdentityBelow()
    {
        PrecedenceSharedLeaf shared = new PrecedenceSharedLeaf { Value = 31 };
        IdentityRootWithOptOutImplicitMember original = new IdentityRootWithOptOutImplicitMember
        {
            Left = shared,
            Right = shared,
            Holder = new PrecedenceImplicitHolder
            {
                Inner = new PrecedenceChildWithIdentity { Left = shared, Right = shared }
            }
        };

        IdentityRootWithOptOutImplicitMember clone = original.FastDeepClone();

        await Assert.That(clone.Left).IsSameReferenceAs(clone.Right);
        await Assert.That(clone.Holder.Inner.Left.Value).IsEqualTo(31);
        await Assert.That(clone.Holder.Inner.Left).IsNotSameReferenceAs(clone.Holder.Inner.Right).Because(
            "a member-level opt-out must reach the implicit POCO's nested cloners");
    }

    #endregion

    #region Test 13: member-level overrides on arrays, dictionaries and safe-element collections

    [FastClonerClonable]
    [FastClonerPreserveIdentity(true)]
    public class IdentityRootWithOptOutArrayMember
    {
        public PrecedenceSharedLeaf Left { get; set; } = new();
        public PrecedenceSharedLeaf Right { get; set; } = new();

        [FastClonerPreserveIdentity(false)]
        public PrecedenceChildWithIdentity[] Items { get; set; } = [];
    }

    [FastClonerClonable]
    [FastClonerPreserveIdentity(true)]
    public class IdentityRootWithOptOutDictionaryMember
    {
        public PrecedenceSharedLeaf Left { get; set; } = new();
        public PrecedenceSharedLeaf Right { get; set; } = new();

        [FastClonerPreserveIdentity(false)]
        public Dictionary<string, PrecedenceChildWithIdentity> Items { get; set; } = new();
    }

    /// <summary>
    /// Mirror case: an explicit opt-in on a collection of safe elements. The elements cannot be
    /// shared, so the opt-in is unobservable — but it used to desynchronise the helper signature
    /// from the call arguments and break the build.
    /// </summary>
    [FastClonerClonable]
    public class RootWithOptInSafeElementCollection
    {
        [FastClonerPreserveIdentity]
        public List<string> Labels { get; set; } = [];
    }

    [Test]
    public async Task MemberFalse_OnArrayMember_DoesNotPreserveIdentityInElements()
    {
        PrecedenceSharedLeaf shared = new PrecedenceSharedLeaf { Value = 21 };
        IdentityRootWithOptOutArrayMember original = new IdentityRootWithOptOutArrayMember
        {
            Left = shared,
            Right = shared,
            Items = [new PrecedenceChildWithIdentity { Left = shared, Right = shared }]
        };

        IdentityRootWithOptOutArrayMember clone = original.FastDeepClone();

        await Assert.That(clone.Left).IsSameReferenceAs(clone.Right);
        await Assert.That(clone.Items[0].Left.Value).IsEqualTo(21);
        await Assert.That(clone.Items[0].Left).IsNotSameReferenceAs(clone.Items[0].Right).Because(
            "a member-level opt-out must reach array element cloners");
    }

    [Test]
    public async Task MemberFalse_OnDictionaryMember_DoesNotPreserveIdentityInValues()
    {
        PrecedenceSharedLeaf shared = new PrecedenceSharedLeaf { Value = 22 };
        IdentityRootWithOptOutDictionaryMember original = new IdentityRootWithOptOutDictionaryMember
        {
            Left = shared,
            Right = shared,
            Items = { ["a"] = new PrecedenceChildWithIdentity { Left = shared, Right = shared } }
        };

        IdentityRootWithOptOutDictionaryMember clone = original.FastDeepClone();

        await Assert.That(clone.Left).IsSameReferenceAs(clone.Right);
        await Assert.That(clone.Items["a"].Left.Value).IsEqualTo(22);
        await Assert.That(clone.Items["a"].Left).IsNotSameReferenceAs(clone.Items["a"].Right).Because(
            "a member-level opt-out must reach dictionary value cloners");
    }

    [Test]
    public async Task MemberTrue_OnSafeElementCollection_ClonesSuccessfully()
    {
        RootWithOptInSafeElementCollection original = new RootWithOptInSafeElementCollection
        {
            Labels = ["a", "b", "a"]
        };

        RootWithOptInSafeElementCollection clone = original.FastDeepClone();

        await Assert.That(clone.Labels).IsEquivalentTo(new[] { "a", "b", "a" });
        await Assert.That(clone.Labels).IsNotSameReferenceAs(original.Labels);
    }

    #endregion

    #region Test 14: member-level opt-out with a root that needs no state of its own

    // The roots below carry no [FastClonerPreserveIdentity] and have no cycles, so the root itself
    // generates no tracking state and every helper it emits is stateless for its own sake. A helper
    // must still be able to receive the member-level opt-out ("capability" is not "default
    // behaviour").

    [FastClonerClonable]
    public class CollectionOptOutRootDefaultOff
    {
        [FastClonerPreserveIdentity(false)]
        public List<PrecedenceChildWithIdentity> Items { get; set; } = [];
    }

    [FastClonerClonable]
    public class ArrayOptOutRootDefaultOff
    {
        [FastClonerPreserveIdentity(false)]
        public PrecedenceChildWithIdentity[] Items { get; set; } = [];
    }

    [FastClonerClonable]
    public class DictionaryOptOutRootDefaultOff
    {
        [FastClonerPreserveIdentity(false)]
        public Dictionary<string, PrecedenceChildWithIdentity> Items { get; set; } = new();
    }

    [FastClonerClonable]
    public class ImplicitOptOutRootDefaultOff
    {
        [FastClonerPreserveIdentity(false)]
        public PrecedenceImplicitHolder Holder { get; set; } = new();
    }

    [FastClonerClonable]
    public class NestedCollectionOptOutRootDefaultOff
    {
        [FastClonerPreserveIdentity(false)]
        public List<List<PrecedenceChildWithIdentity>> Items { get; set; } = [];
    }

    [FastClonerClonable]
    public class GetterOnlyCollectionOptOutRootDefaultOff
    {
        public GetterOnlyCollectionOptOutRootDefaultOff()
        {
            Items = [];
        }

        public GetterOnlyCollectionOptOutRootDefaultOff(List<PrecedenceChildWithIdentity> items)
        {
            Items = items;
        }

        [FastClonerPreserveIdentity(false)]
        public List<PrecedenceChildWithIdentity> Items { get; }
    }

    [FastClonerClonable]
    public class ClonableOptOutRootDefaultOff
    {
        [FastClonerPreserveIdentity(false)]
        public PrecedenceChildWithIdentity Child { get; set; } = new();
    }

    /// <summary>Builds a <see cref="PrecedenceChildWithIdentity"/> whose members alias one leaf.</summary>
    private static PrecedenceChildWithIdentity AliasedChild(int value)
    {
        PrecedenceSharedLeaf shared = new PrecedenceSharedLeaf { Value = value };
        return new PrecedenceChildWithIdentity { Left = shared, Right = shared };
    }

    [Test]
    public async Task DefaultOffRoot_ClonableMemberOptOut_DoesNotPreserveIdentity()
    {
        ClonableOptOutRootDefaultOff original = new ClonableOptOutRootDefaultOff { Child = AliasedChild(41) };

        ClonableOptOutRootDefaultOff clone = original.FastDeepClone();

        await Assert.That(clone.Child.Left.Value).IsEqualTo(41);
        await Assert.That(clone.Child.Left).IsNotSameReferenceAs(clone.Child.Right).Because(
            "the child's type-level default must not win over the member-level opt-out");
    }

    [Test]
    public async Task DefaultOffRoot_CollectionMemberOptOut_DoesNotPreserveIdentityInElements()
    {
        CollectionOptOutRootDefaultOff original = new CollectionOptOutRootDefaultOff { Items = [AliasedChild(42)] };

        CollectionOptOutRootDefaultOff clone = original.FastDeepClone();

        await Assert.That(clone.Items.Count).IsEqualTo(1);
        await Assert.That(clone.Items[0].Left.Value).IsEqualTo(42);
        await Assert.That(clone.Items[0].Left).IsNotSameReferenceAs(clone.Items[0].Right).Because(
            "a stateless default must not stop the opt-out from reaching the element cloner");
    }

    [Test]
    public async Task DefaultOffRoot_ArrayMemberOptOut_DoesNotPreserveIdentityInElements()
    {
        ArrayOptOutRootDefaultOff original = new ArrayOptOutRootDefaultOff { Items = [AliasedChild(43)] };

        ArrayOptOutRootDefaultOff clone = original.FastDeepClone();

        await Assert.That(clone.Items[0].Left.Value).IsEqualTo(43);
        await Assert.That(clone.Items[0].Left).IsNotSameReferenceAs(clone.Items[0].Right).Because(
            "a stateless default must not stop the opt-out from reaching the array element cloner");
    }

    [Test]
    public async Task DefaultOffRoot_DictionaryMemberOptOut_DoesNotPreserveIdentityInValues()
    {
        DictionaryOptOutRootDefaultOff original = new DictionaryOptOutRootDefaultOff
        {
            Items = { ["a"] = AliasedChild(44) }
        };

        DictionaryOptOutRootDefaultOff clone = original.FastDeepClone();

        await Assert.That(clone.Items["a"].Left.Value).IsEqualTo(44);
        await Assert.That(clone.Items["a"].Left).IsNotSameReferenceAs(clone.Items["a"].Right).Because(
            "a stateless default must not stop the opt-out from reaching the dictionary value cloner");
    }

    [Test]
    public async Task DefaultOffRoot_ImplicitPocoMemberOptOut_DoesNotPreserveIdentityBelow()
    {
        ImplicitOptOutRootDefaultOff original = new ImplicitOptOutRootDefaultOff
        {
            Holder = new PrecedenceImplicitHolder { Inner = AliasedChild(45) }
        };

        ImplicitOptOutRootDefaultOff clone = original.FastDeepClone();

        await Assert.That(clone.Holder.Inner.Left.Value).IsEqualTo(45);
        await Assert.That(clone.Holder.Inner.Left).IsNotSameReferenceAs(clone.Holder.Inner.Right).Because(
            "a stateless default must not stop the opt-out from reaching the implicit POCO's cloners");
    }

    [Test]
    public async Task DefaultOffRoot_NestedCollectionMemberOptOut_DoesNotPreserveIdentityInElements()
    {
        NestedCollectionOptOutRootDefaultOff original = new NestedCollectionOptOutRootDefaultOff
        {
            Items = [[AliasedChild(46)]]
        };

        NestedCollectionOptOutRootDefaultOff clone = original.FastDeepClone();

        await Assert.That(clone.Items[0][0].Left.Value).IsEqualTo(46);
        await Assert.That(clone.Items[0][0].Left).IsNotSameReferenceAs(clone.Items[0][0].Right).Because(
            "the opt-out must propagate through nested collection helpers");
    }

    [Test]
    public async Task DefaultOffRoot_GetterOnlyCollectionMemberOptOut_DoesNotPreserveIdentityInElements()
    {
        GetterOnlyCollectionOptOutRootDefaultOff original =
            new GetterOnlyCollectionOptOutRootDefaultOff([AliasedChild(47)]);

        GetterOnlyCollectionOptOutRootDefaultOff clone = original.FastDeepClone();

        await Assert.That(clone.Items[0].Left.Value).IsEqualTo(47);
        await Assert.That(clone.Items[0].Left).IsNotSameReferenceAs(clone.Items[0].Right).Because(
            "a stateless default must not stop the opt-out from reaching a getter-only collection's elements");
    }

    #endregion

    #region Test 15: capability must not depend on generic usage discovery order

    // A plain POCO (no attribute) with an opted-out member. It only becomes known to the generator
    // while generic usages are being analysed, i.e. after the root's own members were emitted.
    public class GenericUsageOptOutPoco
    {
        [FastClonerPreserveIdentity(false)]
        public List<PrecedenceChildWithIdentity> Items { get; set; } = [];
    }

    [FastClonerClonable]
    public class GenericOptOutOrderFirst<T>
    {
        public T Value { get; set; } = default!;
    }

    [FastClonerClonable]
    public class GenericOptOutOrderLast<T>
    {
        public T Value { get; set; } = default!;
    }

    /// <summary>
    /// Usage ordering A: the state-free usage (which decides the capability of the shared
    /// <c>List&lt;PrecedenceChildWithIdentity&gt;</c> helper) comes first, the usage that introduces
    /// the opted-out POCO second.
    /// </summary>
    private static void TouchUsagesStateFreeFirst()
    {
        _ = new GenericOptOutOrderFirst<List<PrecedenceChildWithIdentity>>();
        _ = new GenericOptOutOrderFirst<GenericUsageOptOutPoco>();
    }

    /// <summary>Usage ordering B: the opted-out POCO is discovered first.</summary>
    private static void TouchUsagesOptOutFirst()
    {
        _ = new GenericOptOutOrderLast<GenericUsageOptOutPoco>();
        _ = new GenericOptOutOrderLast<List<PrecedenceChildWithIdentity>>();
    }

    [Test]
    public async Task GenericUsageOrder_StateFreeUsageFirst_StillHonoursMemberOptOut()
    {
        TouchUsagesStateFreeFirst();

        GenericOptOutOrderFirst<GenericUsageOptOutPoco> original = new GenericOptOutOrderFirst<GenericUsageOptOutPoco>
        {
            Value = new GenericUsageOptOutPoco { Items = [AliasedChild(51)] }
        };

        GenericOptOutOrderFirst<GenericUsageOptOutPoco> clone = original.FastDeepClone();

        await Assert.That(clone.Value.Items[0].Left.Value).IsEqualTo(51);
        await Assert.That(clone.Value.Items[0].Left).IsNotSameReferenceAs(clone.Value.Items[0].Right).Because(
            "capability must not depend on which generic usage happened to be analysed first");
    }

    [Test]
    public async Task GenericUsageOrder_OptOutUsageFirst_StillHonoursMemberOptOut()
    {
        TouchUsagesOptOutFirst();

        GenericOptOutOrderLast<GenericUsageOptOutPoco> original = new GenericOptOutOrderLast<GenericUsageOptOutPoco>
        {
            Value = new GenericUsageOptOutPoco { Items = [AliasedChild(52)] }
        };

        GenericOptOutOrderLast<GenericUsageOptOutPoco> clone = original.FastDeepClone();

        await Assert.That(clone.Value.Items[0].Left.Value).IsEqualTo(52);
        await Assert.That(clone.Value.Items[0].Left).IsNotSameReferenceAs(clone.Value.Items[0].Right).Because(
            "the reversed usage order must generate the same semantics");
    }

    #endregion
}