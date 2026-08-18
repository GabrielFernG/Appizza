using System.Text.Json;
using Appizza.Modules.Ordering;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

internal sealed class Phase5ChangeScenarioBuilder(Phase1ApiFixture fixture)
{
    internal async Task<Phase5ChangeScenario> BuildAsync(Phase5OrderScenario order)
    {
        var configuration = new { productVariantId = order.SecondVariantId };
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{order.OrderItemId}/change-requests", new { configuration, reasonCode = "CUSTOMER_CHANGE" }, order.DeviceToken, Guid.NewGuid());
        response.EnsureSuccessStatusCode(); using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var requestId = payload.RootElement.GetProperty("requestId").GetGuid();
        await using var db = fixture.CreateDbContext(); var revision = await db.Set<OrderItemRevision>().SingleAsync(x => x.EstablishmentId == order.EstablishmentId && x.OrderItemId == order.OrderItemId && x.SourceRequestId == requestId); var candidates = await db.OutboxMessages.Where(x => x.EstablishmentId == order.EstablishmentId && x.EventType == "order-item-changed.v1").ToListAsync(); var evt = candidates.Single(x => x.Payload.ToString().Contains(order.OrderItemId.ToString(), StringComparison.OrdinalIgnoreCase));
        return new(order, requestId, evt.Id, evt.Id, revision.PreviousUnitAmount, revision.UnitAmount, revision.PriceDifference, revision.RevisionNumber, configuration);
    }
}

internal sealed record Phase5ChangeScenario(Phase5OrderScenario OrderScenario, Guid ChangeRequestId, Guid OrderItemChangedEventId, Guid OutboxId, decimal PreviousUnitAmount, decimal NewUnitAmount, decimal PriceDifference, int RevisionNumber, object NewConfiguration);
