using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Xunit;

namespace ZhonTai.Admin.Tests.Acceptance;

[Trait("P0Gate", "Required")]
public sealed class RepositoryBoundaryTests
{
    internal static string RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "src", "ZhonTai.sln"))) return directory.FullName;
            throw new InvalidOperationException("Cannot locate repository for architecture checks.");
        }
    }

    [Fact]
    public void ActualProjectReferencesRespectModuleOwnership()
    {
        var root = RepositoryRoot;
        var projects = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);
        var violations = new List<string>();
        foreach (var project in projects)
        {
            if (IsLegacyFixture(project) || IsTest(project)) continue;
            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference"))
            {
                var include = (string)reference.Attribute("Include");
                Assert.False(string.IsNullOrEmpty(include) || include.Contains("$("), "Unresolved ProjectReference: " + project);
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project), include.Replace('\\', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(target), "Missing referenced project: " + target);
                if (IsLegacyFixture(target) || !IsAllowed(project, target))
                    violations.Add(Path.GetFileNameWithoutExtension(project) + " -> " + Path.GetFileNameWithoutExtension(target));
            }
        }
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("modules/admin/ZhonTai.Admin.Core", "modules/admin/ZhonTai.Admin", false)]
    [InlineData("modules/company/Company.Sales/Company.Sales", "modules/admin/ZhonTai.Admin", false)]
    [InlineData("modules/company/Company.Sales/Company.Sales", "modules/admin/ZhonTai.Admin.Contracts", true)]
    [InlineData("modules/company/Company.Sales/Company.Sales.Contracts", "modules/company/Company.Sales/Company.Sales", false)]
    [InlineData("platform/ZhonTai.Common", "modules/admin/ZhonTai.Admin.Contracts", false)]
    public void ForbiddenAndContractEdgesAreDistinguished(string consumer, string owner, bool expected)
    {
        Assert.Equal(expected, IsAllowed(ProjectPath(consumer), ProjectPath(owner)));
    }

    [Fact]
    public void CompiledProductionModulesDoNotImportOtherOwnersInternals()
    {
        var root = RepositoryRoot;
        var solution = File.ReadAllText(Path.Combine(root, "src", "ZhonTai.sln"));
        var projects = Directory.GetFiles(Path.Combine(root, "src", "modules"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsLegacyFixture(path) && !IsTest(path)).ToArray();
        var byAssembly = projects.ToDictionary(path => Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal);
        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = outputDirectory.Parent.Name;
        var framework = outputDirectory.Name;
        var scanned = 0;
        foreach (var project in projects)
        {
            var name = Path.GetFileNameWithoutExtension(project);
            // Active modules must be built in the release solution, otherwise a gate cannot inspect their IL.
            Assert.Contains('"' + name + '"', solution);
            var assembly = Path.Combine(Path.GetDirectoryName(project), "bin", configuration, framework, name + ".dll");
            Assert.True(File.Exists(assembly), "Missing compiled module: " + name);
            using var stream = File.OpenRead(assembly);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            scanned++;
            foreach (var handle in metadata.TypeReferences)
            {
                var type = metadata.GetTypeReference(handle);
                var scope = type.ResolutionScope;
                while (scope.Kind == HandleKind.TypeReference) scope = metadata.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;
                if (scope.Kind != HandleKind.AssemblyReference) continue;
                var owner = metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
                if (!byAssembly.TryGetValue(owner, out var ownerProject) || Module(project) == Module(ownerProject) || IsCore(ownerProject)) continue;
                var qualifiedName = metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
                var result = P0ModuleBoundaryChecker.Check(new ModuleDependency(Module(project), Module(ownerProject), [qualifiedName]));
                Assert.True(result.ExitCode == 0, string.Join(Environment.NewLine, result.Violations));
            }
        }
        Assert.True(scanned > 0, "No compiled modules were inspected.");
    }

    private static string ProjectPath(string directory) => Path.Combine(RepositoryRoot, "src", directory.Replace('/', Path.DirectorySeparatorChar), directory.Split('/')[^1] + ".csproj");
    private static string Normalized(string path) => path.Replace('\\', '/');
    private static bool IsLegacyFixture(string path) => Normalized(path).Contains("/company/Company.MasterData/", StringComparison.Ordinal);
    private static bool IsTest(string path) => Path.GetFileNameWithoutExtension(path).EndsWith(".Tests", StringComparison.Ordinal);
    private static bool IsCore(string path) => Path.GetFileNameWithoutExtension(path) == "ZhonTai.Admin.Core";
    private static string Module(string path)
    {
        var parts = Normalized(path).Split('/');
        var index = Array.IndexOf(parts, "modules");
        if (index < 0) return "platform";
        return parts[index + 1] == "company" ? parts[index + 2] : parts[index + 1];
    }
    private static bool IsAllowed(string consumer, string owner)
    {
        var consumerModule = Module(consumer);
        var ownerModule = Module(owner);
        if (ownerModule == "platform" || IsCore(owner)) return true;
        if (consumerModule == "platform" || IsCore(consumer)) return false;
        var consumerName = Path.GetFileNameWithoutExtension(consumer);
        var ownerName = Path.GetFileNameWithoutExtension(owner);
        if (consumerName.EndsWith(".Contracts", StringComparison.Ordinal)) return ownerName.EndsWith(".Contracts", StringComparison.Ordinal);
        return consumerName.EndsWith(".Host", StringComparison.Ordinal) || consumerModule == ownerModule || ownerName.EndsWith(".Contracts", StringComparison.Ordinal);
    }
}
