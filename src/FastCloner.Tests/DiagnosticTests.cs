using FastCloner.SourceGenerator;
using FastCloner.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace FastCloner.Tests;
[SourceGeneratorCompatible]
public class DiagnosticTests
{
    // This class tests that the Source Generator correctly reports diagnostics.
    // Since FCG004 is an error that breaks the build, we cannot test it by defining the class directly in the test project.
    // Instead, we use CSharpGeneratorDriver to run the generator on a code string in memory and verify the diagnostics.
    [Test]
    public async Task ClassWithUnclonableMember_And_NoFastClonerRuntime_Should_NOT_Report_FCG004_If_ImplicitlyClonable()
    {
        // Arrange
        string source = @"
using FastCloner.SourceGenerator.Shared;
using System.Collections.Generic;

namespace TestNamespace;

// UnclonableClass is effectively safe (only int member), so it should be implicitly clonable
public class UnclonableClass
{
    public int Value { get; set; }
}

[FastClonerClonable]
[FastClonerSimulateNoRuntime]
public class ClassWithUnclonableMember
{
    public UnclonableClass Member { get; set; }
}
";
        // Act
        ImmutableArray<Diagnostic> diagnostics = RunGenerator(source);

        // Assert
        Diagnostic? fcg004 = diagnostics.FirstOrDefault(d => d.Id == "FCG004");
        await Assert.That(fcg004).IsNull().Because("Should NOT report FCG004 because UnclonableClass is implicitly clonable (only safe members)");
    }

    [Test]
    public async Task ClassWithTrulyUnsafeMember_Should_Report_FCG004()
    {
        // Arrange
        string source = @"
using FastCloner.SourceGenerator.Shared;
using System;

namespace TestNamespace;

public class TrulyUnsafe
{
    public TrulyUnsafe(int x) { } // No parameterless ctor
}

[FastClonerClonable]
[FastClonerSimulateNoRuntime]
public class ClassWithUnsafe
{
    public TrulyUnsafe Member { get; set; }
}
";
        // Act
        ImmutableArray<Diagnostic> diagnostics = RunGenerator(source);

        // Assert
        Diagnostic? fcg004 = diagnostics.FirstOrDefault(d => d.Id == "FCG004");
        await Assert.That(fcg004).IsNotNull().Because("Should report FCG004 for truly unsafe type (no parameterless ctor)");
    }

    [Test]
    public async Task ClassWithHttpClient_Should_Report_FCG004()
    {
        // HttpClient is unsafe (internal state) and has members like BaseAddress (Uri) which are not implicitly clonable
        // (Uri has no parameterless ctor). So it should fail when FastCloner is missing.
        string source = @"
using FastCloner.SourceGenerator.Shared;
using System.Net.Http;

namespace TestNamespace;

[FastClonerClonable]
[FastClonerSimulateNoRuntime]
public class ClassWithHttpClient
{
    public HttpClient Client { get; set; }
}
";
        // Act
        ImmutableArray<Diagnostic> diagnostics = RunGenerator(source);

        // Assert
        Diagnostic? fcg004 = diagnostics.FirstOrDefault(d => d.Id == "FCG004");
        await Assert.That(fcg004).IsNotNull().Because("Should report FCG004 for HttpClient (complex BCL type)");
    }

    [Test]
    public async Task ClassWithIndirectHttpClient_Should_Report_FCG004()
    {
        // Wrapper contains HttpClient. Wrapper itself looks like a POCO, but its member is unsafe.
        // So Wrapper is NOT implicitly clonable.
        // So ClassWithIndirectHttpClient should fail because Wrapper requires FastCloner.
        string source = @"
using FastCloner.SourceGenerator.Shared;
using System.Net.Http;

namespace TestNamespace;

public class WrapperOfHttpClient
{
    public HttpClient Client { get; set; }
}

[FastClonerClonable]
[FastClonerSimulateNoRuntime]
public class ClassWithIndirectHttpClient
{
    public WrapperOfHttpClient Wrapper { get; set; }
}
";
        // Act
        ImmutableArray<Diagnostic> diagnostics = RunGenerator(source);

        // Assert
        Diagnostic? fcg004 = diagnostics.FirstOrDefault(d => d.Id == "FCG004");
        await Assert.That(fcg004).IsNotNull().Because("Should report FCG004 for indirect HttpClient (Wrapper contains unsafe member)");
        await Assert.That(fcg004.GetMessage()).Contains("Wrapper").Because("Should identify Wrapper as the offending member in the root type");
    }

    [Test]
    public async Task GenericClass_And_NoFastClonerRuntime_Should_NOT_Report_FCG004()
    {
        // Arrange
        string source = @"
using FastCloner.SourceGenerator.Shared;
using System.Collections.Generic;

namespace TestNamespace;

[FastClonerClonable]
[FastClonerSimulateNoRuntime]
public class GenericClass<T>
{
    public T Value { get; set; }
}
";
        // Act
        ImmutableArray<Diagnostic> diagnostics = RunGenerator(source);

        // Assert
        Diagnostic? fcg004 = diagnostics.FirstOrDefault(d => d.Id == "FCG004");
        await Assert.That(fcg004).IsNull().Because("Should NOT report FCG004 for generic types, as they now fallback gracefully");
    }
    
    [Test]
    public async Task GenericListClass_Should_Use_FastCloner_For_Items()
    {
        // This test verifies that we can clone a generic list of unclonable items using FastCloner runtime
        // Note: In the test environment, FastCloner runtime IS available, so we don't get FCG004 here.
        
        GenericListClass<UnclonableClass> original = new GenericListClass<UnclonableClass>
        {
            Items =
            [
                new UnclonableClass { Value = 1 },
                new UnclonableClass { Value = 2 }
            ]
        };

        GenericListClass<UnclonableClass> clone = original.FastDeepClone();

        await Assert.That(clone).IsNotNull();
        await Assert.That(clone.Items).IsNotNull();
        await Assert.That(clone.Items.Count).IsEqualTo(2);
        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(original.Items[0]); // Should be deep cloned
    }

    [Test]
    public async Task GeneratedCode_ForCollectionWithNonClonableElements_ShouldNotProduceNullableWarnings()
    {
        string source = @"
#nullable enable
using FastCloner.SourceGenerator.Shared;
using System.Collections.Generic;

namespace TestNamespace;

public class NonClonableItem
{
    public NonClonableItem(int x) { Value = x; }
    public int Value { get; set; }
}

[FastClonerClonable]
public class ClassWithNonClonableCollection
{
    public List<NonClonableItem> Items { get; set; } = new();
}
";
        (ImmutableArray<Diagnostic> _, ImmutableArray<Diagnostic> compilationDiags) = RunGeneratorAndCompile(source);

        List<Diagnostic> nullableWarnings = compilationDiags
            .Where(d => d.Id is "CS8604" or "CS8600" or "CS8601" or "CS8603")
            .Where(d => d.Severity == DiagnosticSeverity.Warning)
            .ToList();

        await Assert.That(nullableWarnings).IsEmpty().Because("Generated code should not produce nullable warnings. " +
            $"Found: {string.Join("; ", nullableWarnings.Select(d => $"{d.Id}: {d.GetMessage()}"))}");
    }

    [Test]
    public async Task GeneratedCode_ForArrayWithNonClonableElements_ShouldNotProduceNullableWarnings()
    {
        string source = @"
#nullable enable
using FastCloner.SourceGenerator.Shared;

namespace TestNamespace;

public class NonClonableItem
{
    public NonClonableItem(int x) { Value = x; }
    public int Value { get; set; }
}

[FastClonerClonable]
public class ClassWithNonClonableArray
{
    public NonClonableItem[] Items { get; set; } = [];
}
";
        (ImmutableArray<Diagnostic> _, ImmutableArray<Diagnostic> compilationDiags) = RunGeneratorAndCompile(source);

        List<Diagnostic> nullableWarnings = compilationDiags
            .Where(d => d.Id is "CS8604" or "CS8600" or "CS8601" or "CS8603")
            .Where(d => d.Severity == DiagnosticSeverity.Warning)
            .ToList();

        await Assert.That(nullableWarnings).IsEmpty().Because("Generated code should not produce nullable warnings for arrays. " +
            $"Found: {string.Join("; ", nullableWarnings.Select(d => $"{d.Id}: {d.GetMessage()}"))}");
    }

    [Test]
    public async Task GeneratedCode_ForDictionaryWithNonClonableValues_ShouldNotProduceNullableWarnings()
    {
        string source = @"
#nullable enable
using FastCloner.SourceGenerator.Shared;
using System.Collections.Generic;

namespace TestNamespace;

public class NonClonableItem
{
    public NonClonableItem(int x) { Value = x; }
    public int Value { get; set; }
}

[FastClonerClonable]
public class ClassWithNonClonableDictionary
{
    public Dictionary<string, NonClonableItem> Items { get; set; } = new();
}
";
        (ImmutableArray<Diagnostic> _, ImmutableArray<Diagnostic> compilationDiags) = RunGeneratorAndCompile(source);

        List<Diagnostic> nullableWarnings = compilationDiags
            .Where(d => d.Id is "CS8604" or "CS8600" or "CS8601" or "CS8603")
            .Where(d => d.Severity == DiagnosticSeverity.Warning)
            .ToList();

        await Assert.That(nullableWarnings).IsEmpty().Because("Generated code should not produce nullable warnings for dictionaries. " +
            $"Found: {string.Join("; ", nullableWarnings.Select(d => $"{d.Id}: {d.GetMessage()}"))}");
    }

    [Test]
    public async Task GeneratedCode_ForNullableGetterOnlyObservableCollection_ShouldNotProduceNullableWarnings()
    {
        string source = @"
#nullable enable
using FastCloner.SourceGenerator.Shared;
using System.Collections.ObjectModel;

namespace TestNamespace;

public class InternalClass
{
}

[FastClonerClonable]
public class TestClass1
{
    public ObservableCollection<InternalClass>? Items2
    {
        get;
    }
}";
        (ImmutableArray<Diagnostic> _, ImmutableArray<Diagnostic> compilationDiags) = RunGeneratorAndCompile(source);

        List<Diagnostic> nullableWarnings = compilationDiags
            .Where(d => d.Id is "CS8604" or "CS8602" or "CS8600" or "CS8601" or "CS8603")
            .Where(d => d.Severity == DiagnosticSeverity.Warning)
            .ToList();

        await Assert.That(nullableWarnings).IsEmpty().Because("Generated code should not produce nullable warnings for nullable getter-only ObservableCollection members. " +
            $"Found: {string.Join("; ", nullableWarnings.Select(d => $"{d.Id}: {d.GetMessage()}"))}");
    }

    [Test]
    public async Task GeneratedCode_ForNonNullableGetterOnlyObservableCollection_ShouldNotProduceNullableWarnings()
    {
        string source = @"
#nullable enable
using FastCloner.SourceGenerator.Shared;
using System.Collections.ObjectModel;

namespace TestNamespace;

public class InternalClass
{
}

[FastClonerClonable]
public class TestClass2
{
    public TestClass2()
    {
        Items1 = [];
    }

    public ObservableCollection<InternalClass> Items1
    {
        get;
    }
}";
        (ImmutableArray<Diagnostic> _, ImmutableArray<Diagnostic> compilationDiags) = RunGeneratorAndCompile(source);

        List<Diagnostic> nullableWarnings = compilationDiags
            .Where(d => d.Id is "CS8604" or "CS8602" or "CS8600" or "CS8601" or "CS8603")
            .Where(d => d.Severity == DiagnosticSeverity.Warning)
            .ToList();

        await Assert.That(nullableWarnings).IsEmpty().Because("Generated code should not produce nullable warnings for non-nullable getter-only ObservableCollection members. " +
            $"Found: {string.Join("; ", nullableWarnings.Select(d => $"{d.Id}: {d.GetMessage()}"))}");
    }

    [Test]
    public async Task GeneratedCode_ForAbstractClonableWithClonableDerived_ShouldNotProduceNullableWarnings()
    {
        // Regression test: an abstract [FastClonerClonable] type with derived types that also carry
        // [FastClonerClonable] dispatches to the derived extension's InternalFastDeepClone, which returns
        // a nullable reference. The generated dispatcher must not cast that result to a non-nullable type.
        string source = @"
#nullable enable
using FastCloner.SourceGenerator.Shared;

namespace TestNamespace;

[FastClonerClonable]
public abstract class Animal;

[FastClonerClonable]
public class Dog : Animal
{
    public string Bark { get; set; } = string.Empty;
}

[FastClonerClonable]
public class Cat : Animal
{
    public string Meow { get; set; } = string.Empty;
}

public static class CloningVat
{
    public static Animal Clone(Animal animal) => animal.FastDeepClone()!;
}
";
        (ImmutableArray<Diagnostic> _, ImmutableArray<Diagnostic> compilationDiags) = RunGeneratorAndCompile(source);

        List<Diagnostic> nullableWarnings = compilationDiags
            .Where(d => d.Id is "CS8604" or "CS8602" or "CS8600" or "CS8601" or "CS8603")
            .Where(d => d.Severity == DiagnosticSeverity.Warning)
            .ToList();

        await Assert.That(nullableWarnings).IsEmpty().Because("Generated dispatcher for abstract clonable types should not produce nullable warnings. " +
            $"Found: {string.Join("; ", nullableWarnings.Select(d => $"{d.Id}: {d.GetMessage()}"))}");
    }

    [Test]
    [Arguments("DictionaryValue", """
        public Dictionary<string, Payload?>? Map { get; set; }
        """)]
    [Arguments("ListElement", """
        public List<Payload?>? Items { get; set; }
        """)]
    [Arguments("ArrayElement", """
        public Payload?[]? Items { get; set; }
        """)]
    [Arguments("JaggedArray", """
        public Payload?[][]? Items { get; set; }
        """)]
    [Arguments("NullableKey", """
        public Dictionary<string?, Payload>? Map { get; set; }
        """)]
    [Arguments("NestedGeneric", """
        public Dictionary<string, List<Payload?>>? Map { get; set; }
        """)]
    [Arguments("ReadOnlyDictionary", """
        public IReadOnlyDictionary<string, Payload?>? Map { get; set; }
        """)]
    [Arguments("ReadOnlyList", """
        public IReadOnlyList<Payload?>? Items { get; set; }
        """)]
    [Arguments("CustomGeneric", """
        public Wrapper<Payload?>? Item { get; set; }
        """)]
    [Arguments("BothNullabilityVariants", """
        public Wrapper<Payload>? NonNullArg { get; set; }
        public Wrapper<Payload?>? NullableArg { get; set; }
        """)]
    [Arguments("NullableStringList", """
        public List<string?>? Items { get; set; }
        """)]
    [Arguments("TrustNullabilityDoesNotRegress", """
        [FastClonerClonable]
        [FastClonerTrustNullability]
        public sealed class TrustContainer
        {
            public Dictionary<string, Payload?>? Map { get; set; }
        }
        """)]
    [Arguments("ValueTypeNullableStillWorks", """
        public Dictionary<string, PayloadStruct?>? Map { get; set; }
        """)]
    [Arguments("HashSet", """
        public HashSet<Payload?>? Items { get; set; }
        """)]
    [Arguments("Queue", """
        public Queue<Payload?>? Items { get; set; }
        """)]
    [Arguments("Stack", """
        public Stack<Payload?>? Items { get; set; }
        """)]
    [Arguments("LinkedList", """
        public LinkedList<Payload?>? Items { get; set; }
        """)]
    [Arguments("IEnumerable", """
        public IEnumerable<Payload?>? Items { get; set; }
        """)]
    [Arguments("IList", """
        public IList<Payload?>? Items { get; set; }
        """)]
    [Arguments("ISet", """
        public ISet<Payload?>? Items { get; set; }
        """)]
    [Arguments("ICollection", """
        public ICollection<Payload?>? Items { get; set; }
        """)]
    [Arguments("ObservableCollection", """
        public ObservableCollection<Payload?>? Items { get; set; }
        """)]
    [Arguments("HashSetOfNullableString", """
        public HashSet<string?>? Items { get; set; }
        """)]
    [Arguments("NestedList", """
        public List<List<Payload?>>? Items { get; set; }
        """)]
    [Arguments("NestedWrapperList", """
        public Wrapper<List<Payload?>>? Item { get; set; }
        """)]
    [Arguments("MultiDimArray", """
        public Payload?[,]? Items { get; set; }
        """)]
    [Arguments("DictionaryOfArrays", """
        public Dictionary<string, Payload?[]>? Map { get; set; }
        """)]
    [Arguments("SortedDictionary", """
        public SortedDictionary<string, Payload?>? Map { get; set; }
        """)]
    [Arguments("ConcurrentDictionary", """
        public ConcurrentDictionary<string, Payload?>? Map { get; set; }
        """)]
    [Arguments("ImmutableList", """
        public ImmutableList<Payload?>? Items { get; set; }
        """)]
    [Arguments("FieldMember", """
        public Dictionary<string, Payload?>? Map;
        """)]
    public async Task Issue54_GeneratedCode_ForNullableGenericTypeArguments_ShouldCompile(string scenario, string members)
    {
        // https://github.com/lofcz/FastCloner/issues/54
        // Helpers must keep NRT annotations on generic arguments / array elements,
        // not only the outer '?' on the collection itself.
        bool isStandaloneType = members.Contains("[FastClonerClonable]", StringComparison.Ordinal)
            || members.Contains("public sealed class", StringComparison.Ordinal);

        string containerDecl = isStandaloneType
            ? members
            : "[FastClonerClonable]\npublic sealed class Container\n{\n" + members + "\n}\n";

        string source =
            "#nullable enable\n" +
            "using FastCloner.SourceGenerator.Shared;\n" +
            "using System.Collections.Concurrent;\n" +
            "using System.Collections.Generic;\n" +
            "using System.Collections.Immutable;\n" +
            "using System.Collections.ObjectModel;\n\n" +
            "namespace TestNamespace;\n\n" +
            "public record Payload(string Text);\n" +
            "public record struct PayloadStruct(string Text);\n\n" +
            "public class Wrapper<T>\n" +
            "{\n" +
            "    public T Value { get; set; } = default!;\n" +
            "}\n\n" +
            containerDecl;

        (ImmutableArray<Diagnostic> _, ImmutableArray<Diagnostic> compilationDiags) = RunGeneratorAndCompile(source);

        List<Diagnostic> nrtErrors = compilationDiags
            .Where(d => d.Id is "CS8620" or "CS8619")
            .ToList();

        await Assert.That(nrtErrors).IsEmpty().Because(
            $"Scenario '{scenario}': generated clone helpers must preserve nullable annotations on generic type arguments. " +
            $"Found: {string.Join("; ", nrtErrors.Select(d => $"{d.Id}: {d.GetMessage()}"))}");
    }

    [Test]
    public async Task Issue54_GeneratedHelper_Should_Emit_NullableGenericTypeArgument()
    {
        string source = @"
#nullable enable
using FastCloner.SourceGenerator.Shared;
using System.Collections.Generic;

namespace TestNamespace;

public record Payload(string Text);

[FastClonerClonable]
public sealed class Container
{
    public Dictionary<string, Payload?>? Map { get; set; }
}
";
        List<(string HintName, string Text)> generated = RunGeneratorAndGetSourcesNullable(source);
        string combined = string.Join("\n", generated.Select(g => g.Text));

        await Assert.That(combined).Contains("Dictionary<string, global::TestNamespace.Payload?>");
    }

    #region Issue 57 — cross-namespace clonable collection elements

    private const string Issue57SharedTypes = """
        using FastCloner.SourceGenerator.Shared;
        using System.Collections.Generic;

        namespace Repro.NamespaceA
        {
            [FastClonerClonable]
            public sealed record Value
            {
                public string Text { get; init; } = string.Empty;
            }
        }
        """;

    [Test]
    [Arguments("DictionaryValue", "public Dictionary<int, global::Repro.NamespaceA.Value?>? Items { get; set; }")]
    [Arguments("DictionaryKey", "public Dictionary<global::Repro.NamespaceA.Value, int>? Items { get; set; }")]
    [Arguments("List", "public List<global::Repro.NamespaceA.Value?>? Items { get; set; }")]
    [Arguments("HashSet", "public HashSet<global::Repro.NamespaceA.Value?>? Items { get; set; }")]
    [Arguments("Array", "public global::Repro.NamespaceA.Value?[]? Items { get; set; }")]
    [Arguments("MultiDimArray", "public global::Repro.NamespaceA.Value?[,]? Items { get; set; }")]
    [Arguments("NestedDictOfList", "public Dictionary<int, List<global::Repro.NamespaceA.Value?>>? Items { get; set; }")]
    public async Task Issue57_CrossNamespaceClonableInCollection_ShouldCompile(string scenario, string members)
    {
        // https://github.com/lofcz/FastCloner/issues/57
        // Generated helpers live in the containing type's namespace. Calling
        // TValue.FastDeepClone() as an extension method is unresolvable unless that
        // namespace imports TValue's namespace — so helpers must emit a fully-qualified
        // static call to TValue's extension class instead.
        string source =
            "#nullable enable\n" +
            Issue57SharedTypes +
            "\nnamespace Repro.NamespaceB\n{\n" +
            "    [FastClonerClonable]\n" +
            "    public sealed class Container\n    {\n        " +
            members +
            "\n    }\n}\n";

        (ImmutableArray<Diagnostic> _, ImmutableArray<Diagnostic> compilationDiags) = RunGeneratorAndCompile(source);

        List<Diagnostic> errors = compilationDiags.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        await Assert.That(errors).IsEmpty().Because(
            $"Scenario '{scenario}': generated clone helpers must resolve FastDeepClone for clonables in another namespace. " +
            $"Found: {string.Join("; ", errors.Select(d => $"{d.Id}: {d.GetMessage()}"))}");
    }

    [Test]
    public async Task Issue57_GeneratedHelper_Must_Not_Use_ExtensionMethod_Syntax()
    {
        string source = """
            #nullable enable
            using FastCloner.SourceGenerator.Shared;
            using System.Collections.Generic;

            namespace Repro.NamespaceA
            {
                [FastClonerClonable]
                public sealed record Value
                {
                    public string Text { get; init; } = string.Empty;
                }
            }

            namespace Repro.NamespaceB
            {
                [FastClonerClonable]
                public sealed class Container
                {
                    public Dictionary<int, global::Repro.NamespaceA.Value?>? Items { get; set; }
                    public global::Repro.NamespaceA.Value?[]? Values { get; set; }
                }
            }
            """;

        List<(string HintName, string Text)> generated = RunGeneratorAndGetSourcesNullable(source);
        string combined = string.Join("\n", generated.Select(g => g.Text));

        await Assert.That(combined).DoesNotContain("?.FastDeepClone()").Because(
            "helpers must not call FastDeepClone as an extension method; it is unresolvable across namespaces (issue #57)");
        await Assert.That(combined).Contains("global::Repro.NamespaceA.ValueFastDeepCloneExtensions.InternalFastDeepClone");
    }

    #endregion

    #region Polymorphic attribute validation

    [Test]
    public async Task Polymorphic_OnSealedClass_Should_Report_FCG011()
    {
        // Arrange
        string source = @"
using FastCloner.SourceGenerator.Shared;

namespace TestNamespace;

[FastClonerClonable]
[FastClonerPolymorphic]
public sealed class SealedRoot
{
    public int Value { get; set; }
}
";
        // Act
        ImmutableArray<Diagnostic> diagnostics = RunGenerator(source);

        // Assert
        Diagnostic? fcg011 = diagnostics.FirstOrDefault(d => d.Id == "FCG011");
        await Assert.That(fcg011).IsNotNull().Because("Sealed classes cannot have subtypes, so [FastClonerPolymorphic] has no effect");
    }

    [Test]
    public async Task Polymorphic_WithoutClonable_Should_Report_FCG012()
    {
        // Arrange
        string source = @"
using FastCloner.SourceGenerator.Shared;

namespace TestNamespace;

[FastClonerPolymorphic]
public class NotClonable
{
    public int Value { get; set; }
}
";
        // Act
        ImmutableArray<Diagnostic> diagnostics = RunGenerator(source);

        // Assert
        Diagnostic? fcg012 = diagnostics.FirstOrDefault(d => d.Id == "FCG012");
        await Assert.That(fcg012).IsNotNull().Because("[FastClonerPolymorphic] requires [FastClonerClonable] to have any effect");
    }

    [Test]
    public async Task Polymorphic_OnGenericClass_Should_Be_Supported_Without_MisuseDiagnostics()
    {
        // Generic roots are supported: closed constructions of subtypes are dispatched.
        // Arrange
        string source = @"
using FastCloner.SourceGenerator.Shared;

namespace TestNamespace;

[FastClonerClonable]
[FastClonerPolymorphic]
public class GenericRoot<T>
{
    public int Value { get; set; }
}

public class StringRoot : GenericRoot<string>
{
    public bool Flag { get; set; }
}
";
        // Act
        ImmutableArray<Diagnostic> diagnostics = RunGenerator(source);

        // Assert
        Diagnostic? misuse = diagnostics.FirstOrDefault(d => d.Id is "FCG011" or "FCG012" or "FCG013");
        await Assert.That(misuse).IsNull().Because("generic roots are valid polymorphic cloning roots");
    }

    [Test]
    public async Task Polymorphic_OnAbstractClass_Should_Not_Report_Misuse()
    {
        // Arrange - abstract types dispatch by runtime type already; the attribute
        // is accepted as explicit self-documentation.
        string source = @"
using FastCloner.SourceGenerator.Shared;

namespace TestNamespace;

[FastClonerClonable]
[FastClonerPolymorphic]
public abstract class AbstractRoot
{
    public int Value { get; set; }
}
";
        // Act
        ImmutableArray<Diagnostic> diagnostics = RunGenerator(source);

        // Assert
        Diagnostic? misuse = diagnostics.FirstOrDefault(d => d.Id is "FCG011" or "FCG012" or "FCG013");
        await Assert.That(misuse).IsNull().Because("[FastClonerPolymorphic] on an abstract clonable root is valid");
    }

    [Test]
    public async Task PlainClonable_GeneratedSource_Should_Not_Contain_TypeDispatch()
    {
        // Zero-cost guard: types that do not opt into [FastClonerPolymorphic] must not
        // pay for it - their generated code contains no runtime type check at all.
        // Arrange
        string source = @"
using FastCloner.SourceGenerator.Shared;

namespace TestNamespace;

[FastClonerClonable]
public class SimpleClass
{
    public int Value { get; set; }
    public string Name { get; set; }
}
";
        // Act
        List<(string HintName, string Text)> generated = RunGeneratorAndGetSources(source);
        string combined = string.Join("\n", generated.Select(g => g.Text));

        // Assert
        await Assert.That(combined).Contains("FastDeepClone").Because("the cloner must be generated");
        await Assert.That(combined).DoesNotContain("GetType()").Because("plain clonables must not pay any dispatch cost");
    }

    [Test]
    public async Task PolymorphicRoot_GeneratedSource_Should_Check_ExactType_First()
    {
        // Arrange
        string source = @"
using FastCloner.SourceGenerator.Shared;

namespace TestNamespace;

[FastClonerClonable]
[FastClonerPolymorphic]
public class PolyRoot
{
    public int Value { get; set; }
}

public class PolySub : PolyRoot
{
    public bool Flag { get; set; }
}
";
        // Act
        List<(string HintName, string Text)> generated = RunGeneratorAndGetSources(source);
        string combined = string.Join("\n", generated.Select(g => g.Text));

        // Assert - public method guards with an exact-type check before falling back to dispatch
        await Assert.That(combined).Contains("source.GetType() != typeof(");
        // internal dispatcher branches on exact runtime types of discovered subtypes
        await Assert.That(combined).Contains("runtimeType == typeof(");
    }

    #endregion

    #region PreserveIdentity capability diagnostics (FCG013 / FCG014)

    private const string DiscoverySurfacePrefix = """
        #nullable enable
        using System.Collections.Generic;
        using FastCloner.SourceGenerator.Shared;

        namespace TestNamespace;

        """;

    /// <summary>
    /// A root required by <c>[FastClonerDiscoverGenericArguments(PreserveIdentity = true)]</c> whose
    /// graph necessarily delegates part of itself to the runtime cloner cannot serve a graph-wide
    /// identity-preserving operation. That has to be an error rather than a silent best-effort
    /// implementation, because the requirement is what the caller is relying on.
    /// </summary>
    [Test]
    public async Task CapabilityRequiredRootWithRuntimeResolvedMember_ShouldReportFCG013()
    {
        const string source = DiscoverySurfacePrefix + """

            public class Form
            {
                public object? Payload { get; set; }
            }

            [FastClonerDiscoverGenericArguments(PreserveIdentity = true)]
            public interface ISurface<T>
            {
            }

            public class Consumer
            {
                public ISurface<Form>? Surface { get; set; }
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiags, ImmutableArray<Diagnostic> _) = RunGeneratorAndCompile(source);

        Diagnostic[] required = [.. generatorDiags.Where(d => d.Id == "FCG013")];

        await Assert.That(required.Length).IsEqualTo(1)
            .Because("the required capability cannot be supplied: " +
                     string.Join("; ", generatorDiags.Select(d => $"{d.Id}:{d.GetMessage()}")));
        await Assert.That(required[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(required[0].GetMessage()).Contains("PreserveIdentity");
        await Assert.That(required[0].GetMessage()).Contains("not generated");

        List<(string HintName, string Text)> generated = RunGeneratorAndGetSourcesNullable(source);
        string formFile = generated.Single(g => g.HintName == "TestNamespace_Form_FastDeepClone.g.cs").Text;

        await Assert.That(formFile).DoesNotContain("FastCloneOptions options")
            .Because("an unsatisfied requirement must not be exposed as a best-effort operation");
    }

    /// <summary>
    /// The same requirement over a graph the generator fully models must produce no diagnostics, an
    /// operation-level entry point, and no delegation to the runtime cloner anywhere in the file.
    /// </summary>
    [Test]
    public async Task CapabilityRequiredRootWithModelledGraph_ShouldGenerateWithoutRuntimeEscapePath()
    {
        const string source = DiscoverySurfacePrefix + """

            public class Node
            {
                public int Value { get; set; }
            }

            public class Root
            {
                public string Name { get; set; } = string.Empty;
                public Node? Left { get; set; }
                public Node? Right { get; set; }
                public List<Node> Items { get; set; } = new();
            }

            [FastClonerDiscoverGenericArguments(PreserveIdentity = true)]
            public interface ISurface<T>
            {
            }

            public class Consumer
            {
                public ISurface<Root>? Surface { get; set; }
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiags, ImmutableArray<Diagnostic> _) = RunGeneratorAndCompile(source);

        await Assert.That(generatorDiags.Where(d => d.Id is "FCG013" or "FCG014")).IsEmpty()
            .Because("the graph can carry one tracking state: " +
                     string.Join("; ", generatorDiags.Select(d => $"{d.Id}:{d.GetMessage()}")));
        List<(string HintName, string Text)> generated = RunGeneratorAndGetSourcesNullable(source);
        string rootFile = generated.Single(g => g.HintName == "TestNamespace_Root_FastDeepClone.g.cs").Text;

        await Assert.That(rootFile).Contains("FastCloneOptions options")
            .Because("the operation-level entry point is what makes the requirement usable");
        await Assert.That(rootFile).DoesNotContain("FastCloner.DeepClone(")
            .Because("no part of a supported preserving operation may be handed to the runtime cloner");
    }

    /// <summary>
    /// A root that never asked for identity preservation keeps its surface and its diagnostics
    /// untouched: the runtime fallback for its unmodellable member is existing behavior.
    /// </summary>
    [Test]
    public async Task OrdinaryRootWithRuntimeResolvedMember_ShouldNotReportBoundaryDiagnostics()
    {
        const string source = """
            #nullable enable
            using FastCloner.SourceGenerator.Shared;

            namespace TestNamespace;

            [FastClonerClonable]
            public class Plain
            {
                public object? Payload { get; set; }
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiags, ImmutableArray<Diagnostic> _) = RunGeneratorAndCompile(source);

        await Assert.That(generatorDiags.Where(d => d.Id is "FCG013" or "FCG014")).IsEmpty()
            .Because("a root that never requested the capability must not gain new diagnostics: " +
                     string.Join("; ", generatorDiags.Select(d => $"{d.Id}:{d.GetMessage()}")));

        List<(string HintName, string Text)> generated = RunGeneratorAndGetSourcesNullable(source);
        string file = generated.Single(g => g.HintName == "TestNamespace_Plain_FastDeepClone.g.cs").Text;

        await Assert.That(file).DoesNotContain("FastCloneOptions options")
            .Because("the operation cannot be guaranteed here, so it is not offered");
        await Assert.That(file).Contains("FastCloner.DeepClone(")
            .Because("the existing runtime fallback behavior of ordinary roots is unchanged");
    }

    /// <summary>
    /// A type that configures identity itself predates the new operation-level API. If its graph
    /// crosses a runtime boundary, its existing <c>FastDeepClone()</c> behavior is unchanged and it
    /// simply does not gain the new overload. Reporting a warning would be noise for consumers who
    /// never asked for that API.
    /// </summary>
    [Test]
    public async Task IdentityConfiguredRootWithRuntimeResolvedMember_ShouldStaySilentAndKeepItsDefault()
    {
        const string source = """
            #nullable enable
            using FastCloner.SourceGenerator.Shared;

            namespace TestNamespace;

            public class Payload
            {
                public int Value { get; set; }
            }

            [FastClonerClonable]
            [FastClonerPreserveIdentity]
            public class Configured
            {
                public object? Opaque { get; set; }
                public Payload? First { get; set; }
                public Payload? Second { get; set; }
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiags, ImmutableArray<Diagnostic> _) = RunGeneratorAndCompile(source);

        await Assert.That(generatorDiags.Where(d => d.Id.StartsWith("FCG") && d.Severity == DiagnosticSeverity.Warning)).IsEmpty()
            .Because("an existing identity configuration must not gain new build noise: " +
                     string.Join("; ", generatorDiags.Select(d => $"{d.Id}:{d.GetMessage()}")));

        List<(string HintName, string Text)> generated = RunGeneratorAndGetSourcesNullable(source);
        string file = generated.Single(g => g.HintName == "TestNamespace_Configured_FastDeepClone.g.cs").Text;

        await Assert.That(file).DoesNotContain("FastCloneOptions options")
            .Because("the guarantee cannot be met for this graph, so the new overload is simply not added");
        await Assert.That(file).Contains("FastCloner.DeepClone(")
            .Because("the existing runtime fallback behavior of the type is unchanged");
        await Assert.That(file).Contains("FastDeepClone(this global::TestNamespace.Configured? source)")
            .Because("the existing default entry point stays exactly as it was");
    }

    #endregion

    // Helper method to run the generator (returns only generator diagnostics)
    private static ImmutableArray<Diagnostic> RunGenerator(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);
        
        List<MetadataReference> references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(FastClonerClonableAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("netstandard").Location)
        ];

        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        FastClonerIncrementalGenerator generator = new FastClonerIncrementalGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();

        return result.Diagnostics;
    }

    /// <summary>
    /// Runs the generator and compiles the result, returning both generator and compilation diagnostics.
    /// Includes FastCloner runtime reference so DeepClone fallback code is generated.
    /// </summary>
    private static (ImmutableArray<Diagnostic> GeneratorDiags, ImmutableArray<Diagnostic> CompilationDiags) RunGeneratorAndCompile(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);

        List<MetadataReference> references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(FastClonerClonableAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(FastCloner).Assembly.Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Collections").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Collections.Concurrent").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Collections.Immutable").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.ObjectModel").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("netstandard").Location)
        ];

        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        FastClonerIncrementalGenerator generator = new FastClonerIncrementalGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiags);

        ImmutableArray<Diagnostic> compilationDiags = outputCompilation.GetDiagnostics();
        return (generatorDiags, compilationDiags);
    }

    /// <summary>
    /// Like <see cref="RunGeneratorAndGetSources"/> but with NRT enabled and collection references,
    /// so generated helpers for generic collections can be inspected.
    /// </summary>
    private static List<(string HintName, string Text)> RunGeneratorAndGetSourcesNullable(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);

        List<MetadataReference> references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(FastClonerClonableAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(FastCloner).Assembly.Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Collections").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.ObjectModel").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("netstandard").Location)
        ];

        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        FastClonerIncrementalGenerator generator = new FastClonerIncrementalGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();

        return result.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(g => (g.HintName, g.SourceText.ToString()))
            .ToList();
    }

    /// <summary>
    /// Runs the generator and returns the generated sources (hint name + text)
    /// for structural assertions about emitted code.
    /// </summary>
    private static List<(string HintName, string Text)> RunGeneratorAndGetSources(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);

        List<MetadataReference> references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(FastClonerClonableAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("netstandard").Location)
        ];

        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        FastClonerIncrementalGenerator generator = new FastClonerIncrementalGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();

        return result.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(g => (g.HintName, g.SourceText.ToString()))
            .ToList();
    }

    public class UnclonableClass
    {
        public int Value { get; set; }
    }

    // Test for List<T> where T is generic - should use FastCloner
    [FastClonerClonable]
    public class GenericListClass<T>
    {
        public List<T> Items { get; set; }
    }
}