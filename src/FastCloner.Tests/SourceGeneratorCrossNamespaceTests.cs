using FastCloner.SourceGenerator.Shared;
using FastCloner.Tests.SubModels;
using System.Threading.Tasks;

// Types in a different namespace to test cross-namespace extension class resolution. #29.
namespace FastCloner.Tests.SubModels
{
    [FastClonerClonable]
    public class SubModelItem
    {
        public int Id { get; set; }
        public string? Label { get; set; }
    }

    [FastClonerClonable]
    public class NestedSubModel
    {
        public int Value { get; set; }
        public SubModelItem? Child { get; set; }
    }
}

namespace FastCloner.Tests
{
    [FastClonerClonable]
    public class CrossNamespaceContainer
    {
        public SubModelItem? Item { get; set; }
        public string? Tag { get; set; }
    }

    [FastClonerClonable]
    public class CrossNamespaceListContainer
    {
        public List<SubModelItem>? Items { get; set; }
    }

    [FastClonerClonable]
    public class CrossNamespaceNestedContainer
    {
        public NestedSubModel? Nested { get; set; }
        public int Code { get; set; }
    }

    // Issue #57: collection/dictionary helpers must resolve FastDeepClone for
    // [FastClonerClonable] element/key/value types declared in another namespace.
    [FastClonerClonable]
    public class CrossNamespaceDictionaryContainer
    {
        public Dictionary<int, SubModelItem?>? Items { get; set; }
    }

    [FastClonerClonable]
    public class CrossNamespaceHashSetContainer
    {
        public HashSet<SubModelItem?>? Items { get; set; }
    }

    [FastClonerClonable]
    public class CrossNamespaceArrayContainer
    {
        public SubModelItem?[]? Items { get; set; }
    }

    [FastClonerClonable]
    public class CrossNamespaceMultiDimArrayContainer
    {
        public SubModelItem?[,]? Items { get; set; }
    }

    [FastClonerClonable]
    public class CrossNamespaceDictionaryKeyContainer
    {
        public Dictionary<SubModelItem, int>? Items { get; set; }
    }
    public class SourceGeneratorCrossNamespaceTests
    {
        [Test]
        [SourceGeneratorCompatible]
        public async Task CrossNamespace_Direct_Property_Should_Deep_Clone()
        {
            CrossNamespaceContainer original = new CrossNamespaceContainer
            {
                Item = new SubModelItem { Id = 42, Label = "Original" },
                Tag = "test"
            };

            CrossNamespaceContainer clone = original.FastDeepClone();

            await Assert.That(clone).IsNotNull();
            await Assert.That(clone!.Item).IsNotNull();
            await Assert.That(clone.Item).IsNotSameReferenceAs(original.Item);
            await Assert.That(clone.Item!.Id).IsEqualTo(42);
            await Assert.That(clone.Item.Label).IsEqualTo("Original");
            await Assert.That(clone.Tag).IsEqualTo("test");

            clone.Item.Label = "Modified";
            await Assert.That(original.Item!.Label).IsEqualTo("Original");
        }

        [Test]
        [SourceGeneratorCompatible]
        public async Task CrossNamespace_Null_Property_Should_Remain_Null()
        {
            CrossNamespaceContainer original = new CrossNamespaceContainer { Item = null, Tag = "t" };
            CrossNamespaceContainer clone = original.FastDeepClone();

            await Assert.That(clone).IsNotNull();
            await Assert.That(clone!.Item).IsNull();
            await Assert.That(clone.Tag).IsEqualTo("t");
        }
        
        [Test]
        [SourceGeneratorCompatible]
        public async Task CrossNamespace_List_Should_Deep_Clone_Elements()
        {
            CrossNamespaceListContainer original = new CrossNamespaceListContainer
            {
                Items =
                [
                    new SubModelItem { Id = 1, Label = "A" },
                    new SubModelItem { Id = 2, Label = "B" },
                    new SubModelItem { Id = 3, Label = "C" }
                ]
            };

            CrossNamespaceListContainer clone = original.FastDeepClone();

            await Assert.That(clone).IsNotNull();
            await Assert.That(clone!.Items).IsNotNull();
            await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
            await Assert.That(clone.Items!.Count).IsEqualTo(3);
            await Assert.That(clone.Items[0].Id).IsEqualTo(1);
            await Assert.That(clone.Items[1].Label).IsEqualTo("B");
            await Assert.That(clone.Items[2].Id).IsEqualTo(3);

            clone.Items[0].Label = "Modified";
            await Assert.That(original.Items[0].Label).IsEqualTo("A");
        }

        [Test]
        [SourceGeneratorCompatible]
        public async Task CrossNamespace_Null_List_Should_Remain_Null()
        {
            CrossNamespaceListContainer original = new CrossNamespaceListContainer { Items = null };
            CrossNamespaceListContainer clone = original.FastDeepClone();

            await Assert.That(clone).IsNotNull();
            await Assert.That(clone!.Items).IsNull();
        }

        [Test]
        [SourceGeneratorCompatible]
        public async Task CrossNamespace_Nested_Reference_Should_Deep_Clone()
        {
            CrossNamespaceNestedContainer original = new CrossNamespaceNestedContainer
            {
                Nested = new NestedSubModel
                {
                    Value = 99,
                    Child = new SubModelItem { Id = 7, Label = "Deep" }
                },
                Code = 123
            };

            CrossNamespaceNestedContainer clone = original.FastDeepClone();

            await Assert.That(clone).IsNotNull();
            await Assert.That(clone!.Code).IsEqualTo(123);
            await Assert.That(clone.Nested).IsNotNull();
            await Assert.That(clone.Nested).IsNotSameReferenceAs(original.Nested);
            await Assert.That(clone.Nested!.Value).IsEqualTo(99);
            await Assert.That(clone.Nested.Child).IsNotNull();
            await Assert.That(clone.Nested.Child).IsNotSameReferenceAs(original.Nested!.Child);
            await Assert.That(clone.Nested.Child!.Id).IsEqualTo(7);
            await Assert.That(clone.Nested.Child.Label).IsEqualTo("Deep");

            clone.Nested.Child.Label = "Modified";
            await Assert.That(original.Nested.Child!.Label).IsEqualTo("Deep");
        }

        [Test]
        [SourceGeneratorCompatible]
        public async Task CrossNamespace_Dictionary_Value_Should_Deep_Clone()
        {
            CrossNamespaceDictionaryContainer original = new CrossNamespaceDictionaryContainer
            {
                Items = new Dictionary<int, SubModelItem?>
                {
                    [1] = new SubModelItem { Id = 1, Label = "abc" },
                    [2] = null
                }
            };

            CrossNamespaceDictionaryContainer clone = original.FastDeepClone();

            await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
            await Assert.That(clone.Items![1]!.Label).IsEqualTo("abc");
            await Assert.That(clone.Items[1]).IsNotSameReferenceAs(original.Items![1]);
            await Assert.That(clone.Items[2]).IsNull();

            clone.Items[1]!.Label = "modified";
            await Assert.That(original.Items[1]!.Label).IsEqualTo("abc");
        }

        [Test]
        [SourceGeneratorCompatible]
        public async Task CrossNamespace_HashSet_Should_Deep_Clone_Elements()
        {
            SubModelItem kept = new SubModelItem { Id = 7, Label = "kept" };
            CrossNamespaceHashSetContainer original = new CrossNamespaceHashSetContainer
            {
                Items = [new SubModelItem { Id = 1, Label = "a" }, null, kept]
            };

            CrossNamespaceHashSetContainer clone = original.FastDeepClone();

            await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
            await Assert.That(clone.Items!.Count).IsEqualTo(3);
            await Assert.That(clone.Items.Contains(null)).IsTrue();
            await Assert.That(clone.Items.Any(x => x?.Label == "kept")).IsTrue();
            await Assert.That(clone.Items.Any(x => ReferenceEquals(x, kept))).IsFalse();
        }

        [Test]
        [SourceGeneratorCompatible]
        public async Task CrossNamespace_Array_Should_Deep_Clone_Elements()
        {
            CrossNamespaceArrayContainer original = new CrossNamespaceArrayContainer
            {
                Items = [new SubModelItem { Id = 3, Label = "arr" }, null]
            };

            CrossNamespaceArrayContainer clone = original.FastDeepClone();

            await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
            await Assert.That(clone.Items![0]!.Label).IsEqualTo("arr");
            await Assert.That(clone.Items[0]).IsNotSameReferenceAs(original.Items![0]);
            await Assert.That(clone.Items[1]).IsNull();
        }

        [Test]
        [SourceGeneratorCompatible]
        public async Task CrossNamespace_MultiDimArray_Should_Deep_Clone_Elements()
        {
            CrossNamespaceMultiDimArrayContainer original = new CrossNamespaceMultiDimArrayContainer
            {
                Items = new SubModelItem?[2, 2]
            };
            original.Items[0, 0] = new SubModelItem { Id = 4, Label = "md" };

            CrossNamespaceMultiDimArrayContainer clone = original.FastDeepClone();

            await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
            await Assert.That(clone.Items![0, 0]!.Label).IsEqualTo("md");
            await Assert.That(clone.Items[0, 0]).IsNotSameReferenceAs(original.Items![0, 0]);
            await Assert.That(clone.Items[1, 1]).IsNull();
        }

        [Test]
        [SourceGeneratorCompatible]
        public async Task CrossNamespace_Dictionary_Key_Should_Deep_Clone()
        {
            SubModelItem key = new SubModelItem { Id = 9, Label = "key" };
            CrossNamespaceDictionaryKeyContainer original = new CrossNamespaceDictionaryKeyContainer
            {
                Items = new Dictionary<SubModelItem, int> { [key] = 42 }
            };

            CrossNamespaceDictionaryKeyContainer clone = original.FastDeepClone();

            await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
            await Assert.That(clone.Items!.Count).IsEqualTo(1);
            SubModelItem clonedKey = clone.Items.Keys.Single();
            await Assert.That(clonedKey.Id).IsEqualTo(9);
            await Assert.That(clonedKey.Label).IsEqualTo("key");
            await Assert.That(clonedKey).IsNotSameReferenceAs(key);
            await Assert.That(clone.Items.Values.Single()).IsEqualTo(42);
        }
    }
}