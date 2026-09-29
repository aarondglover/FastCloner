using FastCloner.SourceGenerator.Shared;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FastCloner.Tests;

/// <summary>
/// Behavioral tests for <c>[FastClonerDiscoverGenericArguments]</c>: closed usages of a marked
/// generic API surface make their closed generic arguments source-generation roots. Every
/// assertion here goes through <c>FastDeepClone()</c>, which only exists when the generator
/// produced a root for the type, so the tests fail to compile if discovery regresses.
/// </summary>
[SourceGeneratorCompatible]
public class SourceGeneratorGenericArgumentDiscoveryTests
{
    #region Generic class and struct surfaces

    public class DiscoveryOrder
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Lines { get; set; } = [];
    }

    public class DiscoveryResult
    {
        public int Value { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    [FastClonerDiscoverGenericArguments]
    public sealed class DiscoveryPipeline<TInput, TOutput>
    {
        public TInput? Input { get; set; }
        public TOutput? Output { get; set; }
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task GenericClassUsage_ShouldDiscoverEveryClosedGenericArgument()
    {
        DiscoveryPipeline<DiscoveryOrder, DiscoveryResult> pipeline = new()
        {
            Input = new DiscoveryOrder { Name = "order", Lines = ["a", "b"] },
            Output = new DiscoveryResult { Value = 42, Label = "ok" }
        };

        DiscoveryOrder orderClone = pipeline.Input!.FastDeepClone();
        DiscoveryResult resultClone = pipeline.Output!.FastDeepClone();

        await Assert.That(orderClone).IsNotSameReferenceAs(pipeline.Input);
        await Assert.That(orderClone.Name).IsEqualTo("order");
        await Assert.That(orderClone.Lines).IsNotSameReferenceAs(pipeline.Input!.Lines);
        await Assert.That(orderClone.Lines).IsEquivalentTo(["a", "b"]);

        await Assert.That(resultClone).IsNotSameReferenceAs(pipeline.Output);
        await Assert.That(resultClone.Value).IsEqualTo(42);
        await Assert.That(resultClone.Label).IsEqualTo("ok");
    }

    public struct DiscoveryStructPayload
    {
        public int Id { get; set; }
        public List<string>? Tags { get; set; }
    }

    [FastClonerDiscoverGenericArguments]
    public struct DiscoveryBox<T>
    {
        public T? Value { get; set; }
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task GenericStructUsage_ShouldDiscoverClosedGenericArgument()
    {
        DiscoveryBox<DiscoveryStructPayload> box = new()
        {
            Value = new DiscoveryStructPayload { Id = 7, Tags = ["x"] }
        };

        DiscoveryStructPayload original = box.Value;
        DiscoveryStructPayload clone = original.FastDeepClone();

        await Assert.That(clone.Id).IsEqualTo(7);
        await Assert.That(clone.Tags).IsNotSameReferenceAs(original.Tags);
        await Assert.That(clone.Tags).IsEquivalentTo(["x"]);
    }

    #endregion

    #region Generic interface and delegate surfaces

    public class DiscoveryField
    {
        public int X { get; set; }
    }

    public class DiscoveryForm
    {
        public string Title { get; set; } = string.Empty;
        public DiscoveryField Field { get; set; } = new();
    }

    [FastClonerDiscoverGenericArguments]
    public interface IDiscoveryContainer<T>
    {
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task GenericInterfaceUsage_ShouldDiscoverClosedGenericArgument()
    {
        IDiscoveryContainer<DiscoveryForm>? container = null;
        await Assert.That(container).IsNull();

        DiscoveryForm original = new() { Title = "form", Field = new DiscoveryField { X = 5 } };
        DiscoveryForm clone = original.FastDeepClone();

        await Assert.That(clone).IsNotSameReferenceAs(original);
        await Assert.That(clone.Title).IsEqualTo("form");
        await Assert.That(clone.Field).IsNotSameReferenceAs(original.Field);
        await Assert.That(clone.Field.X).IsEqualTo(5);
    }

    public class DiscoveryCustomer
    {
        public string Name { get; set; } = string.Empty;
        public DiscoverySnapshot? Snapshot { get; set; }
    }

    public class DiscoverySnapshot
    {
        public int Version { get; set; }
    }

    [FastClonerDiscoverGenericArguments]
    public delegate TResult DiscoveryProcessor<TSource, TResult>(TSource source);

    [Test]
    [SourceGeneratorCompatible]
    public async Task GenericDelegateUsage_ShouldDiscoverAllClosedGenericArguments()
    {
        DiscoveryProcessor<DiscoveryCustomer, DiscoverySnapshot>? processor = null;
        await Assert.That(processor).IsNull();

        DiscoveryCustomer customer = new()
        {
            Name = "Ada",
            Snapshot = new DiscoverySnapshot { Version = 3 }
        };

        DiscoveryCustomer customerClone = customer.FastDeepClone();

        await Assert.That(customerClone).IsNotSameReferenceAs(customer);
        await Assert.That(customerClone.Name).IsEqualTo("Ada");
        await Assert.That(customerClone.Snapshot).IsNotSameReferenceAs(customer.Snapshot);
        await Assert.That(customerClone.Snapshot!.Version).IsEqualTo(3);

        DiscoverySnapshot snapshotClone = customer.Snapshot!.FastDeepClone();
        await Assert.That(snapshotClone.Version).IsEqualTo(3);
    }

    #endregion

    #region Generic method surfaces

    public class DiscoveryRequest
    {
        public string Payload { get; set; } = string.Empty;
    }

    public class DiscoveryResponse
    {
        public int Status { get; set; }
    }

    public class DiscoveryContext
    {
        public Guid Id { get; set; }
    }

    public static class DiscoveryOperations
    {
        [FastClonerDiscoverGenericArguments]
        public static void Execute<T1, T2, T3>()
        {
        }
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task GenericMethodUsage_ShouldDiscoverAllMethodGenericArguments()
    {
        DiscoveryOperations.Execute<DiscoveryRequest, DiscoveryResponse, DiscoveryContext>();

        DiscoveryRequest request = new() { Payload = "ping" };
        DiscoveryResponse response = new() { Status = 200 };
        DiscoveryContext context = new() { Id = Guid.NewGuid() };

        DiscoveryRequest requestClone = request.FastDeepClone();
        DiscoveryResponse responseClone = response.FastDeepClone();
        DiscoveryContext contextClone = context.FastDeepClone();

        await Assert.That(requestClone).IsNotSameReferenceAs(request);
        await Assert.That(requestClone.Payload).IsEqualTo("ping");
        await Assert.That(responseClone.Status).IsEqualTo(200);
        await Assert.That(contextClone.Id).IsEqualTo(context.Id);
    }

    public interface IDiscoveryStage<TContext>
    {
        [FastClonerDiscoverGenericArguments]
        void Execute<TInput, TOutput>();
    }

    public sealed class DiscoveryStage : IDiscoveryStage<DiscoveryContext>
    {
        public void Execute<TInput, TOutput>()
        {
        }
    }

    public class DiscoveryStageForm
    {
        public string Name { get; set; } = string.Empty;
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task MarkedMethodOnGenericContainingType_ShouldDiscoverMethodAndContainingTypeArguments()
    {
        IDiscoveryStage<DiscoveryContext> stage = new DiscoveryStage();
        stage.Execute<DiscoveryRequest, DiscoveryResponse>();

        DiscoveryContext context = new() { Id = Guid.NewGuid() };
        DiscoveryContext contextClone = context.FastDeepClone();

        DiscoveryRequest requestClone = new DiscoveryRequest { Payload = "p" }.FastDeepClone();
        DiscoveryResponse responseClone = new DiscoveryResponse { Status = 1 }.FastDeepClone();

        await Assert.That(contextClone).IsNotSameReferenceAs(context);
        await Assert.That(contextClone.Id).IsEqualTo(context.Id);
        await Assert.That(requestClone.Payload).IsEqualTo("p");
        await Assert.That(responseClone.Status).IsEqualTo(1);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task ClosedUsageOfContainingTypeOnly_ShouldDiscoverClosedContainingTypeArgument()
    {
        // The attribute sits on Execute(); a closed usage of the containing type is a
        // discovery point for the containing type's closed arguments (design: IStage<Form>).
        IDiscoveryStage<DiscoveryStageForm>? stage = null;
        await Assert.That(stage).IsNull();

        DiscoveryStageForm form = new() { Name = "stage" };
        DiscoveryStageForm clone = form.FastDeepClone();

        await Assert.That(clone).IsNotSameReferenceAs(form);
        await Assert.That(clone.Name).IsEqualTo("stage");
    }

    #endregion

    #region Nested generic arguments

    public class DiscoveryNestedLeaf
    {
        public string Text { get; set; } = string.Empty;
    }

    public class DiscoveryWrapper<T>
    {
        public T? Value { get; set; }
    }

    [FastClonerDiscoverGenericArguments]
    public interface IDiscoveryNestedSurface<T>
    {
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task NestedConstructedGenericArgument_ShouldDiscoverInnermostClosedArgument()
    {
        IDiscoveryNestedSurface<DiscoveryWrapper<DiscoveryNestedLeaf>>? wrapper = null;
        await Assert.That(wrapper).IsNull();

        DiscoveryNestedLeaf leaf = new() { Text = "nested" };
        DiscoveryNestedLeaf clone = leaf.FastDeepClone();

        await Assert.That(clone).IsNotSameReferenceAs(leaf);
        await Assert.That(clone.Text).IsEqualTo("nested");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task NestedCollectionArgument_ShouldDiscoverElementType()
    {
        IDiscoveryNestedSurface<List<DiscoveryNestedLeaf>>? list = null;
        IDiscoveryNestedSurface<Dictionary<string, DiscoveryNestedLeaf>>? dictionary = null;
        await Assert.That(list).IsNull();
        await Assert.That(dictionary).IsNull();

        DiscoveryNestedLeaf leaf = new() { Text = "element" };
        DiscoveryNestedLeaf clone = leaf.FastDeepClone();

        await Assert.That(clone).IsNotSameReferenceAs(leaf);
        await Assert.That(clone.Text).IsEqualTo("element");
    }

    #endregion

    #region Open and unbound arguments

    public static IDiscoveryContainer<T>? OpenGenericUsage<T>() => null;

    [Test]
    [SourceGeneratorCompatible]
    public async Task OpenGenericArguments_ShouldNotProduceRoots()
    {
        // The declaration below carries an open type-parameter argument; it must be ignored
        // without breaking generation of the closed usages in this file.
        IDiscoveryContainer<int>? closed = null;
        await Assert.That(closed).IsNull();
        await Assert.That(typeof(IDiscoveryContainer<>)).IsNotNull();
        await Assert.That(OpenGenericUsage<DiscoveryField>()).IsNull();
    }

    #endregion

    #region Identity preservation

    public class DiscoveryIdentityNode
    {
        public int Value { get; set; }
    }

    public class DiscoveryIdentityRoot
    {
        public string Name { get; set; } = string.Empty;
        public DiscoveryIdentityNode? Left { get; set; }
        public DiscoveryIdentityNode? Right { get; set; }
    }

    [FastClonerDiscoverGenericArguments(PreserveIdentity = true)]
    public interface IDiscoveryIdentitySurface<T>
    {
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task PreserveIdentityDiscoveryPoint_ShouldKeepSharedReferencesShared()
    {
        IDiscoveryIdentitySurface<DiscoveryIdentityRoot>? surface = null;
        await Assert.That(surface).IsNull();

        DiscoveryIdentityNode shared = new() { Value = 11 };
        DiscoveryIdentityRoot original = new() { Name = "root", Left = shared, Right = shared };

        DiscoveryIdentityRoot clone = original.FastDeepClone();

        await Assert.That(clone).IsNotSameReferenceAs(original);
        await Assert.That(clone.Left).IsNotSameReferenceAs(original.Left);
        await Assert.That(clone.Left).IsSameReferenceAs(clone.Right)
            .Because("identity preservation declared on the discovery point must survive cloning");
        await Assert.That(clone.Left!.Value).IsEqualTo(11);
    }

    public class DiscoveryDefaultIdentityNode
    {
        public int Value { get; set; }
    }

    public class DiscoveryDefaultIdentityRoot
    {
        public string Name { get; set; } = string.Empty;
        public DiscoveryDefaultIdentityNode? Left { get; set; }
        public DiscoveryDefaultIdentityNode? Right { get; set; }
    }

    [FastClonerDiscoverGenericArguments]
    public interface IDiscoveryDefaultSurface<T>
    {
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task DefaultDiscoveryPoint_ShouldRetainDefaultIdentityBehavior()
    {
        IDiscoveryDefaultSurface<DiscoveryDefaultIdentityRoot>? surface = null;
        await Assert.That(surface).IsNull();

        DiscoveryDefaultIdentityNode shared = new() { Value = 3 };
        DiscoveryDefaultIdentityRoot original = new() { Name = "root", Left = shared, Right = shared };

        DiscoveryDefaultIdentityRoot clone = original.FastDeepClone();

        await Assert.That(clone).IsNotSameReferenceAs(original);
        await Assert.That(clone.Left).IsNotSameReferenceAs(original.Left);
        await Assert.That(clone.Right).IsNotSameReferenceAs(original.Right);
        await Assert.That(clone.Left).IsNotSameReferenceAs(clone.Right)
            .Because("without PreserveIdentity the default no-identity behavior is retained");
    }

    public class DiscoveryMergedRoot
    {
        public string Name { get; set; } = string.Empty;
        public DiscoveryMergedNode? Left { get; set; }
        public DiscoveryMergedNode? Right { get; set; }
    }

    public class DiscoveryMergedNode
    {
        public int Value { get; set; }
    }

    [FastClonerDiscoverGenericArguments]
    public interface IDiscoveryPlainSurface<T>
    {
    }

    [FastClonerDiscoverGenericArguments(PreserveIdentity = true)]
    public interface IDiscoveryPreservingSurface<T>
    {
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task TypeDiscoveredThroughSeveralSurfaces_ShouldPreserveIdentityWhenAnySurfaceRequestsIt()
    {
        IDiscoveryPlainSurface<DiscoveryMergedRoot>? plain = null;
        IDiscoveryPreservingSurface<DiscoveryMergedRoot>? preserving = null;
        await Assert.That(plain).IsNull();
        await Assert.That(preserving).IsNull();

        DiscoveryMergedNode shared = new() { Value = 8 };
        DiscoveryMergedRoot original = new() { Name = "merged", Left = shared, Right = shared };

        DiscoveryMergedRoot clone = original.FastDeepClone();

        await Assert.That(clone.Left).IsSameReferenceAs(clone.Right)
            .Because("a type discovered through several surfaces is generated once, and identity preservation wins");
        await Assert.That(clone.Left!.Value).IsEqualTo(8);
    }

    #endregion
}
