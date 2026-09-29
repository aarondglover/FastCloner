using FastCloner.SourceGenerator;
using FastCloner.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace FastCloner.Tests;

/// <summary>
/// Pipeline level tests for <c>[FastClonerDiscoverGenericArguments]</c>. These compile source in
/// memory (and, for the referenced-assembly scenario, execute the emitted consumer) because the
/// scenarios they cover cannot be expressed inside the test project itself.
/// </summary>
[SourceGeneratorCompatible]
public class SourceGeneratorGenericArgumentDiscoveryPipelineTests
{
    [Test]
    public async Task OpenGenericArgument_ShouldNotGenerateAnyRoot()
    {
        const string source = """
            #nullable enable
            using FastCloner.SourceGenerator.Shared;

            namespace TestNamespace;

            [FastClonerDiscoverGenericArguments]
            public interface IContainer<T>
            {
            }

            public class Holder<T>
            {
                public IContainer<T>? Container { get; set; }
            }
            """;

        List<(string HintName, string Text)> generated = RunGeneratorAndGetSources(source);

        await Assert.That(Roots(generated)).IsEmpty()
            .Because("an open type-parameter argument carries no closed generic information");
    }

    [Test]
    public async Task UnboundGenericArgument_ShouldNotGenerateAnyRoot()
    {
        const string source = """
            #nullable enable
            using System;
            using FastCloner.SourceGenerator.Shared;

            namespace TestNamespace;

            [FastClonerDiscoverGenericArguments]
            public interface IContainer<T>
            {
            }

            public class Holder
            {
                public Type Unbound { get; set; } = typeof(IContainer<>);
            }
            """;

        List<(string HintName, string Text)> generated = RunGeneratorAndGetSources(source);

        await Assert.That(Roots(generated)).IsEmpty()
            .Because("typeof(IContainer<>) is unbound and must not produce roots");
    }

    [Test]
    public async Task NonMarkedGenericUsage_ShouldNotGenerateRoots()
    {
        const string source = """
            #nullable enable
            using System.Collections.Generic;

            namespace TestNamespace;

            public class NotMarked<T>
            {
                public T? Value { get; set; }
            }

            public class Payload
            {
                public string Name { get; set; } = string.Empty;
            }

            public class Holder
            {
                public List<Payload>? Items { get; set; }
                public NotMarked<Payload>? Unmarked { get; set; }
            }
            """;

        List<(string HintName, string Text)> generated = RunGeneratorAndGetSources(source);

        await Assert.That(Roots(generated)).IsEmpty()
            .Because("discovery only applies to declarations marked with the attribute");
    }

    [Test]
    public async Task NotFound_ShouldNotGenerateRootForTypeParameter()
    {
        const string source = """
            #nullable enable
            using FastCloner.SourceGenerator.Shared;

            namespace TestNamespace;

            [FastClonerDiscoverGenericArguments]
            public static class Operations
            {
                public static void Execute<T1, T2, T3>()
                {
                }
            }

            public class Caller<T>
            {
                public void Use()
                {
                    Operations.Execute<T, T, T>();
                }
            }
            """;

        List<(string HintName, string Text)> generated = RunGeneratorAndGetSources(source);

        await Assert.That(Roots(generated)).IsEmpty();
    }

    [Test]
    public async Task ClonablePayload_ShouldConvergeRequirementsIntoItsSingleRoot()
    {
        const string source = """
            #nullable enable
            using FastCloner.SourceGenerator.Shared;

            namespace TestNamespace;

            [FastClonerDiscoverGenericArguments(PreserveIdentity = true)]
            public interface IContainer<T>
            {
            }

            [FastClonerClonable]
            public class Payload
            {
                public string Name { get; set; } = string.Empty;
            }

            public class Holder
            {
                public IContainer<Payload>? Container { get; set; }
            }
            """;

        (ImmutableArray<Diagnostic> _, ImmutableArray<Diagnostic> compilationDiagnostics) = RunGeneratorAndCompile(source);
        List<(string HintName, string Text)> generated = RunGeneratorAndGetSources(source);

        List<string> rootExtensionDeclarations = generated
            .SelectMany(source => source.Text.Split('\n'))
            .Where(line => line.Contains("class PayloadFastDeepCloneExtensions", StringComparison.Ordinal))
            .ToList();

        await Assert.That(rootExtensionDeclarations.Count).IsEqualTo(1)
            .Because("a type that is already a root through [FastClonerClonable] must not be generated twice");

        string payloadSource = generated.Single(source => source.HintName.Contains("Payload")).Text;
        await Assert.That(payloadSource).Contains("FastCloneOptions options")
            .Because("the discovery requirement must converge into the existing root as the capability to honor a supplied state");
        await Assert.That(payloadSource).Contains("FastDeepClone(this")
            .Because("the existing default entry point stays in place");

        List<Diagnostic> compileErrors = compilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        await Assert.That(compileErrors).IsEmpty()
            .Because("a duplicate root would produce duplicate extension members: " + string.Join("; ", compileErrors.Select(d => d.GetMessage())));
    }

    [Test]
    public async Task ForeignPayloadWithoutParameterlessConstructor_ShouldNotBeRooted()
    {
        const string librarySource = """
            #nullable enable
            using System;

            namespace DiscoveryLibrary;

            public class LibraryValue
            {
                public LibraryValue(int value)
                {
                    Value = value;
                }

                public int Value { get; }
            }

            public interface ILibraryContainer<T>
            {
            }
            """;

        const string consumerSource = """
            #nullable enable
            using DiscoveryLibrary;
            using FastCloner.SourceGenerator.Shared;

            namespace DiscoveryConsumer;

            [FastClonerDiscoverGenericArguments]
            public interface IConsumerSurface<T>
            {
            }

            public class Payload
            {
                public string Name { get; set; } = string.Empty;
            }

            public class Usage
            {
                public IConsumerSurface<LibraryValue>? Foreign { get; set; }
                public IConsumerSurface<Payload>? Local { get; set; }
            }
            """;

        List<(string HintName, string Text)> generated = await RunGeneratorAgainstLibrary(librarySource, consumerSource);

        List<string> roots = Roots(generated);
        await Assert.That(roots.Any(r => r.Contains("Payload"))).IsTrue()
            .Because("a payload declared in the consuming compilation is a root");
        await Assert.That(roots.Any(r => r.Contains("LibraryValue"))).IsFalse()
            .Because("member-wise cloners for foreign types without a usable constructor depend on state the generator cannot access");
    }

    [Test]
    public async Task ReferencedAssemblyDeclaration_ShouldActAsDiscoveryPointForConsumingCompilation()
    {
        const string librarySource = """
            #nullable enable
            using FastCloner.SourceGenerator.Shared;

            namespace DiscoveryLibrary;

            [FastClonerDiscoverGenericArguments]
            public interface ILibraryContainer<T>
            {
            }

            [FastClonerDiscoverGenericArguments]
            public delegate TResult LibraryProcessor<TSource, TResult>(TSource source);

            public static class LibraryOperations
            {
                [FastClonerDiscoverGenericArguments]
                public static void Execute<T1, T2>()
                {
                }
            }
            """;

        const string consumerSource = """
            #nullable enable
            using System.Collections.Generic;
            using DiscoveryLibrary;

            namespace DiscoveryConsumer;

            public class RootFromLibrary
            {
                public string Name { get; set; } = string.Empty;
                public List<string>? Tags { get; set; }
                public AddressFromLibrary? Address { get; set; }
            }

            public class AddressFromLibrary
            {
                public string City { get; set; } = string.Empty;
            }

            public class SnapshotFromLibrary
            {
                public int Version { get; set; }
            }

            public class Usage
            {
                public ILibraryContainer<RootFromLibrary>? Container { get; set; }
                public LibraryProcessor<RootFromLibrary, SnapshotFromLibrary>? Processor { get; set; }

                public void Call()
                {
                    LibraryOperations.Execute<SnapshotFromLibrary, AddressFromLibrary>();
                }
            }
            """;

        string directory = Path.Combine(Path.GetTempPath(), "FastClonerDiscovery_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            string libraryPath = Path.Combine(directory, "DiscoveryLibrary.dll");
            string consumerPath = Path.Combine(directory, "DiscoveryConsumer.dll");

            ImmutableArray<Diagnostic> libraryDiagnostics = EmitLibrary(librarySource, libraryPath);
            await Assert.That(libraryDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)).IsEmpty();

            (List<(string HintName, string Text)> generated, ImmutableArray<Diagnostic> consumerDiagnostics) =
                EmitConsumer(libraryPath, consumerSource, consumerPath);

            List<Diagnostic> errors = consumerDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            await Assert.That(errors).IsEmpty().Because(string.Join("; ", errors.Select(d => d.GetMessage())));

            List<string> roots = Roots(generated);
            await Assert.That(roots.Any(r => r.Contains("RootFromLibrary"))).IsTrue()
                .Because("the marked declaration lives in a referenced assembly, the closed usage in the consumer");
            await Assert.That(roots.Any(r => r.Contains("SnapshotFromLibrary"))).IsTrue()
                .Because("delegate and method surfaces from the referenced assembly are discovery points too");

            // Execute the emitted consumer: the root discovered through the referenced assembly
            // must clone deeply, with the same behavior a locally declared surface would give.
            // Both assemblies are loaded from memory so the temp files stay deletable.
            AssemblyLoadContext context = new AssemblyLoadContext("FastClonerDiscoveryTests", isCollectible: true);
            try
            {
                System.Reflection.Assembly libraryAssembly = context.LoadFromStream(new MemoryStream(File.ReadAllBytes(libraryPath)));
                System.Reflection.Assembly consumer = context.LoadFromStream(new MemoryStream(File.ReadAllBytes(consumerPath)));

                context.Resolving += (_, name) =>
                {
                    if (string.Equals(name.Name, "DiscoveryLibrary", StringComparison.Ordinal))
                        return libraryAssembly;

                    try
                    {
                        return AssemblyLoadContext.Default.LoadFromAssemblyName(name);
                    }
                    catch (FileNotFoundException)
                    {
                        return null;
                    }
                };

                Type rootType = consumer.GetType("DiscoveryConsumer.RootFromLibrary")!;
                object original = Activator.CreateInstance(rootType)!;
                rootType.GetProperty("Name")!.SetValue(original, "order");
                rootType.GetProperty("Tags")!.SetValue(original, new List<string> { "a", "b" });
                Type addressType = consumer.GetType("DiscoveryConsumer.AddressFromLibrary")!;
                object address = Activator.CreateInstance(addressType)!;
                addressType.GetProperty("City")!.SetValue(address, "Sydney");
                rootType.GetProperty("Address")!.SetValue(original, address);

                Type extensions = consumer.GetType("DiscoveryConsumer.RootFromLibraryFastDeepCloneExtensions")!;
                MethodInfo clone = extensions.GetMethod("FastDeepClone", BindingFlags.Public | BindingFlags.Static)!;
                object cloned = clone.Invoke(null, [original])!;

                await Assert.That(cloned).IsNotNull();
                await Assert.That(cloned).IsNotSameReferenceAs(original);
                await Assert.That(rootType.GetProperty("Name")!.GetValue(cloned)).IsEqualTo("order");
                await Assert.That(rootType.GetProperty("Address")!.GetValue(cloned)).IsNotSameReferenceAs(address);
                await Assert.That(addressType.GetProperty("City")!.GetValue(rootType.GetProperty("Address")!.GetValue(cloned))).IsEqualTo("Sydney");
            }
            finally
            {
                context.Unload();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task UnrelatedCompilationChange_ShouldNotRerunDiscoveryOutput()
    {
        const string source = """
            #nullable enable
            using FastCloner.SourceGenerator.Shared;

            namespace TestNamespace;

            [FastClonerDiscoverGenericArguments(PreserveIdentity = true)]
            public interface IContainer<T>
            {
            }

            public class Payload
            {
                public string Name { get; set; } = string.Empty;
            }

            [FastClonerClonable]
            public class ClonablePayload
            {
                public string Name { get; set; } = string.Empty;
            }

            public class Usage
            {
                public IContainer<Payload>? Container { get; set; }
                public IContainer<ClonablePayload>? Clonable { get; set; }
            }
            """;

        const string unrelatedSource = """
            #nullable enable
            namespace TestNamespace;

            public class Unrelated
            {
                public int Value { get; set; }
            }
            """;

        SyntaxTree tree = CSharpSyntaxTree.ParseText(source);
        CSharpCompilation compilation = CreateCompilation("TestAssembly", tree, []);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new FastClonerIncrementalGenerator());

        driver = driver.RunGenerators(compilation);
        GeneratedSourceResult? initialDiscovery = FindGeneratedSource(driver.GetRunResult(), "_Payload_FastDeepClone");
        await Assert.That(initialDiscovery.HasValue).IsTrue();

        // Two unrelated edits: the discovery output must not be recomputed, so the generated text
        // instance stays the very same object (a re-run would produce a fresh instance, and with it
        // a new generation timestamp).
        CSharpCompilation withFirstFile = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(unrelatedSource));
        driver = driver.RunGenerators(withFirstFile);
        GeneratedSourceResult? afterFirstEdit = FindGeneratedSource(driver.GetRunResult(), "_Payload_FastDeepClone");
        await Assert.That(afterFirstEdit.HasValue).IsTrue();
        await Assert.That(ReferenceEquals(initialDiscovery.Value.SourceText, afterFirstEdit.Value.SourceText)).IsTrue()
            .Because("adding an unrelated file must not re-run the discovery output");

        CSharpCompilation withSecondFile = withFirstFile.AddSyntaxTrees(CSharpSyntaxTree.ParseText(unrelatedSource.Replace("Unrelated", "Unrelated2")));
        driver = driver.RunGenerators(withSecondFile);
        GeneratorDriverRunResult runResult = driver.GetRunResult();
        GeneratedSourceResult? afterSecondEdit = FindGeneratedSource(runResult, "_Payload_FastDeepClone");
        await Assert.That(afterSecondEdit.HasValue).IsTrue();
        await Assert.That(ReferenceEquals(initialDiscovery.Value.SourceText, afterSecondEdit.Value.SourceText)).IsTrue()
            .Because("the discovery output stays cached for every unrelated change, not just the first");

        // The roots themselves are unchanged and still expose the capability.
        List<(string HintName, string Text)> generated = CollectGeneratedSources(runResult);
        await Assert.That(Roots(generated).Any(r => r.Contains("Payload"))).IsTrue();
        await Assert.That(generated.Any(source => source.Text.Contains("FastCloneOptions options"))).IsTrue()
            .Because("both the discovered root and the already-clonable root gain the capability");
    }

    private static GeneratedSourceResult? FindGeneratedSource(GeneratorDriverRunResult runResult, string hintNameFragment)
    {
        foreach (GeneratorRunResult result in runResult.Results)
        {
            if (result.GeneratedSources.IsDefault)
                continue;

            foreach (GeneratedSourceResult source in result.GeneratedSources)
            {
                if (source.HintName.Contains(hintNameFragment, StringComparison.Ordinal))
                    return source;
            }
        }

        return null;
    }

    [Test]
    public async Task UnmodellableDiscoveredRoot_ShouldSurfaceTheRootDiagnostic()
    {
        const string source = """
            #nullable enable
            using FastCloner.SourceGenerator.Shared;

            namespace TestNamespace;

            [FastClonerDiscoverGenericArguments]
            public interface IContainer<T>
            {
            }

            public class Payload
            {
                public NoDefaultConstructor Member { get; set; } = new NoDefaultConstructor(1);
            }

            public class NoDefaultConstructor
            {
                public NoDefaultConstructor(int value)
                {
                    Value = value;
                }

                public int Value { get; }
            }

            public class Usage
            {
                public IContainer<Payload>? Container { get; set; }
            }
            """;

        // No FastCloner runtime reference: the discovered root cannot be generated, and the
        // failure has to reach the consumer instead of silently producing no entry point.
        CSharpCompilation compilation = CreateCompilation("TestAssembly", source, [], includeFastClonerRuntime: false);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new FastClonerIncrementalGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out _);

        List<Diagnostic> diagnostics = [.. driver.GetRunResult().Diagnostics, .. outputCompilation.GetDiagnostics()];

        await Assert.That(diagnostics.Any(d => d.Id == "FCG004")).IsTrue()
            .Because("a discovered root that cannot be modelled must report the same diagnostic as a clonable root: " +
                     string.Join("; ", diagnostics.Select(d => d.Id + ":" + d.GetMessage())));
    }

    private static List<(string HintName, string Text)> CollectGeneratedSources(GeneratorDriverRunResult runResult)
    {
        List<(string HintName, string Text)> generated = [];
        foreach (GeneratorRunResult generatorResult in runResult.Results)
        {
            if (generatorResult.GeneratedSources.IsDefault)
                continue;

            foreach (GeneratedSourceResult source in generatorResult.GeneratedSources)
                generated.Add((source.HintName, source.SourceText.ToString()));
        }

        return generated;
    }

    private static List<string> Roots(List<(string HintName, string Text)> generated)
    {
        return generated
            .Where(source => source.HintName.EndsWith("_FastDeepClone.g.cs", StringComparison.Ordinal))
            .Select(source => source.HintName)
            .ToList();
    }

    private static (List<(string HintName, string Text)> Generated, ImmutableArray<Diagnostic> Diagnostics) EmitConsumer(
        string libraryPath,
        string consumerSource,
        string consumerPath)
    {
        CSharpCompilation compilation = CreateCompilation(
            "DiscoveryConsumer",
            consumerSource,
            [MetadataReference.CreateFromFile(libraryPath)]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new FastClonerIncrementalGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);

        GeneratorDriverRunResult runResult = driver.GetRunResult();

        List<(string HintName, string Text)> generated = CollectGeneratedSources(runResult);

        EmitResult emit = outputCompilation.Emit(consumerPath);

        List<Diagnostic> diagnostics = [];
        diagnostics.AddRange(emit.Diagnostics);
        if (!runResult.Diagnostics.IsDefault)
            diagnostics.AddRange(runResult.Diagnostics);

        diagnostics.AddRange(outputCompilation.GetDiagnostics());

        return (generated, diagnostics.ToImmutableArray());
    }

    private static ImmutableArray<Diagnostic> EmitLibrary(string librarySource, string libraryPath)
    {
        CSharpCompilation compilation = CreateCompilation("DiscoveryLibrary", librarySource, []);
        EmitResult emit = compilation.Emit(libraryPath);
        return emit.Diagnostics;
    }

    private static async Task<List<(string HintName, string Text)>> RunGeneratorAgainstLibrary(string librarySource, string consumerSource)
    {
        CSharpCompilation library = CreateCompilation("DiscoveryLibrary", librarySource, []);
        using MemoryStream libraryImage = new();
        EmitResult libraryEmit = library.Emit(libraryImage);
        await Assert.That(libraryEmit.Success).IsTrue()
            .Because(string.Join("; ", libraryEmit.Diagnostics.Select(d => d.GetMessage())));

        CSharpCompilation compilation = CreateCompilation(
            "DiscoveryConsumer",
            consumerSource,
            [MetadataReference.CreateFromImage(libraryImage.ToArray())]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new FastClonerIncrementalGenerator());
        driver = driver.RunGenerators(compilation);

        return driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .Select(source => (source.HintName, source.SourceText.ToString()))
            .ToList();
    }

    private static List<(string HintName, string Text)> RunGeneratorAndGetSources(string source)
    {
        CSharpCompilation compilation = CreateCompilation("TestAssembly", source, []);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new FastClonerIncrementalGenerator());
        driver = driver.RunGenerators(compilation);

        return CollectGeneratedSources(driver.GetRunResult());
    }

    private static (ImmutableArray<Diagnostic> GeneratorDiagnostics, ImmutableArray<Diagnostic> CompilationDiagnostics) RunGeneratorAndCompile(string source)
    {
        CSharpCompilation compilation = CreateCompilation("TestAssembly", source, []);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new FastClonerIncrementalGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);

        return (generatorDiagnostics, outputCompilation.GetDiagnostics());
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, string source, IEnumerable<MetadataReference> additionalReferences, bool includeFastClonerRuntime = true)
    {
        return CreateCompilation(assemblyName, CSharpSyntaxTree.ParseText(source), additionalReferences, includeFastClonerRuntime);
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, SyntaxTree syntaxTree, IEnumerable<MetadataReference> additionalReferences, bool includeFastClonerRuntime = true)
    {
        List<MetadataReference> references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(FastClonerClonableAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Collections").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Collections.Concurrent").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Collections.Immutable").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.ObjectModel").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("netstandard").Location)
        ];

        if (includeFastClonerRuntime)
            references.Add(MetadataReference.CreateFromFile(typeof(FastCloner).Assembly.Location));

        references.AddRange(additionalReferences);

        return CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }
}
