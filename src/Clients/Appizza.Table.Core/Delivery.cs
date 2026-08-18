using System.Net;
using System.Net.Http.Json;

namespace Appizza.Table.Core;

public sealed record TableDeliveryConfirmation(Guid? ConfirmationId, string? Status, long? Version, int? Sequence, DateTimeOffset? RequestedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? ConfirmedAt);
public sealed record TableDeliveryContest(Guid? ContestId, string? Status, long? Version, string? Reason, DateTimeOffset? ContestedAt);
public sealed record TableDeliveryState(TableDeliveryConfirmation? Confirmation, TableDeliveryContest? Contest, bool AttentionRequired);
public sealed record TableOrderItem(Guid ItemId, string ProductName, string PublicStatus, string? PublicSubstatus, long Version, TableDeliveryState? Delivery);
public sealed record TableOrder(Guid OrderId, string? PublicStatus, IReadOnlyList<TableOrderItem> Items);
public sealed record TableOrdersStatus(Guid SessionId, IReadOnlyList<TableOrder> Orders);

public sealed class TableDeliveryApi(HttpClient client)
{
    public async Task<TableOrdersStatus> GetStatusAsync(CancellationToken cancellationToken)
        => await client.GetFromJsonAsync<TableOrdersStatus>("api/v1/table-device/session/orders/status", cancellationToken) ?? throw new InvalidOperationException("Resposta de status vazia.");

    public Task<HttpResponseMessage> ConfirmAsync(Guid itemId, long expectedVersion, Guid idempotencyKey, CancellationToken cancellationToken)
        => PostAsync($"api/v1/table-device/order-items/{itemId}/delivery-confirmation", new { confirmation = "received", expectedVersion }, idempotencyKey, cancellationToken);

    public Task<HttpResponseMessage> ContestAsync(Guid itemId, long expectedVersion, string reasonCode, string? note, Guid idempotencyKey, CancellationToken cancellationToken)
        => PostAsync($"api/v1/table-device/order-items/{itemId}/delivery-contestation", new { reasonCode, customerNote = note, expectedVersion }, idempotencyKey, cancellationToken);

    private async Task<HttpResponseMessage> PostAsync(string path, object body, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey.ToString());
        return await client.SendAsync(request, cancellationToken);
    }
}

public static class DeliveryErrorMessages
{
    public static string For(HttpStatusCode status, string? errorCode) => errorCode switch
    {
        "CONCURRENCY_CONFLICT" => "O estado mudou. Atualizamos os pedidos.",
        "DELIVERY_ALREADY_CONFIRMED" => "A entrega já foi confirmada. Atualizamos os pedidos.",
        "DELIVERY_ALREADY_CONTESTED" => "O problema já foi informado.",
        "DELIVERY_CONTESTATION_WINDOW_EXPIRED" => "O prazo para contestar terminou.",
        "DEVICE_BLOCKED" or "DEVICE_CREDENTIAL_REVOKED" => "O dispositivo não está autorizado.",
        _ when status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "O dispositivo não está autorizado.",
        _ when status == HttpStatusCode.NotFound => "O item não está mais disponível.",
        _ => "Não foi possível atualizar a entrega."
    };
}
