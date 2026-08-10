using Appizza.BuildingBlocks;
using Appizza.Modules.Auditing;
using Appizza.Modules.Catalog;
using Appizza.Modules.Communications;
using Appizza.Modules.Devices;
using Appizza.Modules.Establishments;
using Appizza.Modules.Identity;
using Appizza.Modules.Integration;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Media;
using Appizza.Modules.Operations;
using Appizza.Modules.Ordering;
using Appizza.Modules.Payments;
using Appizza.Modules.Promotions;
using Appizza.Modules.Reporting;
using Appizza.Modules.Tables;
using System.Reflection;

namespace Appizza.ArchitectureTests;

public sealed class ModuleBoundaryTests
{
    private static readonly Type[] ModuleTypes =
    [
        typeof(EstablishmentsModule), typeof(IdentityModule), typeof(CatalogModule),
        typeof(OrderingModule), typeof(KitchenModule), typeof(TablesModule), typeof(PaymentsModule),
        typeof(PromotionsModule), typeof(MediaModule), typeof(CommunicationsModule),
        typeof(DevicesModule), typeof(OperationsModule), typeof(ReportingModule),
        typeof(AuditingModule), typeof(IntegrationModule)
    ];

    [Fact]
    public void ModulesDoNotReferenceOtherModuleAssemblies()
    {
        foreach (var moduleType in ModuleTypes)
        {
            var forbiddenReferences = moduleType.Assembly.GetReferencedAssemblies()
                .Where(reference => reference.Name?.StartsWith("Appizza.Modules.", StringComparison.Ordinal) == true)
                .ToArray();

            Assert.Empty(forbiddenReferences);
        }
    }

    [Fact]
    public void EveryModuleHasAUniqueDiscoverableName()
    {
        var modules = ModuleTypes
            .Select(type => Assert.IsAssignableFrom<IAppizzaModule>(Activator.CreateInstance(type)))
            .ToArray();

        Assert.Equal(15, modules.Length);
        Assert.Equal(modules.Length, modules.Select(module => module.Name).Distinct().Count());
    }
}
