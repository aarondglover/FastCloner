using System.Collections.Immutable;
using FastCloner.SourceGenerator;
using FastCloner.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;
using System.Threading.Tasks;

namespace FastCloner.Tests;

/// <summary>
/// Issue #56: FastClonerContext-generated code referenced <c>FcGeneratedCloneState</c>
/// without defining or importing it whenever a registered type needed identity-tracking
/// helpers (any collection / array / dictionary of a non-safe element type) but circular
/// analysis among registered types found no cycle — so the nested state class was skipped.
/// </summary>
public class SourceGeneratorIssue56Tests
{
    [Test]
    [Arguments("IssueRepro_List", """
        public class Customer
        {
            public string Name { get; set; } = string.Empty;
            public Address? Address { get; set; }
            public List<Address>? PreviousAddresses { get; set; }
        }

        public class Address
        {
            public string City { get; set; } = string.Empty;
        }

        [FastClonerRegister(typeof(Customer), typeof(Address))]
        public partial class MyCloningContext : FastClonerContext
        {
        }
        """)]
    [Arguments("Array", """
        public class Holder
        {
            public Item[]? Items { get; set; }
        }

        public class Item
        {
            public int Id { get; set; }
        }

        [FastClonerRegister(typeof(Holder), typeof(Item))]
        public partial class ArrayContext : FastClonerContext
        {
        }
        """)]
    [Arguments("Dictionary", """
        public class Holder
        {
            public Dictionary<string, Item>? Map { get; set; }
        }

        public class Item
        {
            public string Name { get; set; } = string.Empty;
        }

        [FastClonerRegister(typeof(Holder), typeof(Item))]
        public partial class DictionaryContext : FastClonerContext
        {
        }
        """)]
    [Arguments("HashSet", """
        public class Holder
        {
            public HashSet<Item>? Items { get; set; }
        }

        public class Item
        {
            public int Id { get; set; }
        }

        [FastClonerRegister(typeof(Holder), typeof(Item))]
        public partial class HashSetContext : FastClonerContext
        {
        }
        """)]
    [Arguments("NestedList", """
        public class Holder
        {
            public List<List<Item>>? Groups { get; set; }
        }

        public class Item
        {
            public int Id { get; set; }
        }

        [FastClonerRegister(typeof(Holder), typeof(Item))]
        public partial class NestedListContext : FastClonerContext
        {
        }
        """)]
    [Arguments("DictionaryOfList", """
        public class Holder
        {
            public Dictionary<string, List<Item>>? Groups { get; set; }
        }

        public class Item
        {
            public int Id { get; set; }
        }

        [FastClonerRegister(typeof(Holder), typeof(Item))]
        public partial class DictionaryOfListContext : FastClonerContext
        {
        }
        """)]
    [Arguments("ElementOnlyReachableThroughCollection", """
        public class Holder
        {
            public List<Item>? Items { get; set; }
        }

        public class Item
        {
            public int Id { get; set; }
        }

        [FastClonerRegister(typeof(Holder))]
        public partial class CollectionOnlyContext : FastClonerContext
        {
        }
        """)]
    [Arguments("NoCollection_StillCompiles", """
        public class Customer
        {
            public string Name { get; set; } = string.Empty;
            public Address? Address { get; set; }
        }

        public class Address
        {
            public string City { get; set; } = string.Empty;
        }

        [FastClonerRegister(typeof(Customer), typeof(Address))]
        public partial class PlainContext : FastClonerContext
        {
        }
        """)]
    [Arguments("CycleThroughCollection", """
        public class Node
        {
            public List<Node>? Children { get; set; }
        }

        [FastClonerRegister(typeof(Node))]
        public partial class CycleContext : FastClonerContext
        {
        }
        """)]
    public async Task Context_WithIdentityTrackingMember_Should_Compile(string scenario, string declarations)
    {
        string source = """
            #nullable enable
            using System.Collections.Generic;
            using FastCloner.SourceGenerator.Shared;

            namespace TestNamespace;

            """ + declarations;

        (ImmutableArray<Diagnostic> generatorDiags, ImmutableArray<Diagnostic> compilationDiags) = RunGeneratorAndCompile(source);

        List<Diagnostic> errors = compilationDiags
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        await Assert.That(generatorDiags.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse()
            .Because($"Scenario '{scenario}': generator should not fail. " +
                     string.Join("; ", generatorDiags.Select(d => $"{d.Id}: {d.GetMessage()}")));

        await Assert.That(errors).IsEmpty()
            .Because($"Scenario '{scenario}': generated FastClonerContext must compile. " +
                     string.Join("; ", errors.Select(d => $"{d.Id}: {d.GetMessage()}")));
    }

    [Test]
    public async Task Issue56_Repro_GeneratedSource_Should_Not_Reference_Unresolved_CloneState()
    {
        string source = """
            #nullable enable
            using System.Collections.Generic;
            using FastCloner.SourceGenerator.Shared;

            namespace TestNamespace;

            public class Customer
            {
                public string Name { get; set; } = string.Empty;
                public Address? Address { get; set; }
                public List<Address>? PreviousAddresses { get; set; }
            }

            public class Address
            {
                public string City { get; set; } = string.Empty;
            }

            [FastClonerRegister(typeof(Customer), typeof(Address))]
            public partial class MyCloningContext : FastClonerContext
            {
            }
            """;

        (ImmutableArray<Diagnostic> _, ImmutableArray<Diagnostic> compilationDiags) = RunGeneratorAndCompile(source);

        List<Diagnostic> missingType = compilationDiags
            .Where(d => d.Id == "CS0246" && d.GetMessage().Contains("FcGeneratedCloneState"))
            .ToList();

        await Assert.That(missingType).IsEmpty()
            .Because("Generated context code must not reference FcGeneratedCloneState unless the type is in scope. " +
                     string.Join("; ", missingType.Select(d => d.GetMessage())));
    }

    [Test]
    public async Task Context_GeneratedSource_Should_Use_Shared_CloneState_Not_Nested_Type()
    {
        string source = """
            #nullable enable
            using System.Collections.Generic;
            using FastCloner.SourceGenerator.Shared;

            namespace TestNamespace;

            public class Customer
            {
                public List<Address>? PreviousAddresses { get; set; }
            }

            public class Address
            {
                public string City { get; set; } = string.Empty;
            }

            [FastClonerRegister(typeof(Customer), typeof(Address))]
            public partial class MyCloningContext : FastClonerContext
            {
            }
            """;

        List<(string HintName, string Text)> generated = RunGeneratorAndGetSources(source);
        string combined = string.Join("\n", generated.Select(g => g.Text));

        await Assert.That(combined).Contains("partial class MyCloningContext").Because("a context partial must be generated");
        await Assert.That(combined).Contains("global::FastCloner.SourceGenerator.Shared.FcGeneratedCloneState")
            .Because("identity-tracking helpers must name the Shared state type with a global:: qualifier");
        await Assert.That(combined).DoesNotContain("private class FcGeneratedCloneState")
            .Because("a nested state class can be skipped when circular analysis finds no cycle");
    }

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

    private static List<(string HintName, string Text)> RunGeneratorAndGetSources(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);

        List<MetadataReference> references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(FastClonerClonableAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(FastCloner).Assembly.Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Collections").Location),
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
}
