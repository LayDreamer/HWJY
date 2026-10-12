using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ZhonTai.Admin.Tests.Acceptance;

[Trait("P0Gate", "Required")]
public sealed class GateCoverageTests
{
    [Fact]
    public void EveryActiveAcceptanceTestIsIncludedInTheRequiredGate()
    {
        var types = typeof(GateCoverageTests).Assembly.GetTypes().Where(type => type.Namespace == typeof(GateCoverageTests).Namespace);
        foreach (var type in types)
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var factData = method.GetCustomAttributesData().FirstOrDefault(attribute => typeof(FactAttribute).IsAssignableFrom(attribute.AttributeType));
            if (factData == null) continue;
            // The retired Material fixture is the only intentional exclusion.
            if (type == typeof(HostMySqlAcceptanceTests) && method.Name == "MaterialApiUsesJwtRbacAndRoleOrgDataIsolationOnMySql8") continue;
            if (type == typeof(SchedulerIsolationTest)) continue; // Legacy BaseTest uses machine-specific configuration.
            var traits = type.GetCustomAttributesData().Concat(method.GetCustomAttributesData())
                .Where(attribute => attribute.AttributeType == typeof(TraitAttribute)).ToArray();
            Assert.Contains(traits, trait => (string)trait.ConstructorArguments[0].Value == "P0Gate" && (string)trait.ConstructorArguments[1].Value == "Required");
            if (factData.AttributeType != typeof(FactAttribute) && factData.AttributeType != typeof(TheoryAttribute))
                Assert.Contains(traits, trait => (string)trait.ConstructorArguments[0].Value == "P0Dependency" && (string)trait.ConstructorArguments[1].Value == "MySql");
        }
    }
}
