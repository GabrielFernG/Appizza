using Appizza.BuildingBlocks;
using Appizza.Persistence;

namespace Appizza.UnitTests;

public sealed class FoundationTests
{
    [Fact]
    public void EstablishmentGuardRejectsCrossEstablishmentAccess()
    {
        var context = new TestEstablishmentContext(Guid.NewGuid());

        Assert.Throws<UnauthorizedAccessException>(() =>
            EstablishmentAccessGuard.EnsureAccess(Guid.NewGuid(), context));
    }

    [Fact]
    public void EstablishmentGuardAcceptsCurrentEstablishment()
    {
        var establishmentId = Guid.NewGuid();
        var context = new TestEstablishmentContext(establishmentId);

        EstablishmentAccessGuard.EnsureAccess(establishmentId, context);
    }

    [Fact]
    public void FoundationDeclaresAllDocumentedSchemas()
    {
        Assert.Equal(15, AppizzaSchemas.All.Count);
        Assert.Contains("media", AppizzaSchemas.All);
        Assert.Contains("integration", AppizzaSchemas.All);
    }

    private sealed record TestEstablishmentContext(Guid? EstablishmentId) : IEstablishmentContext;
}
