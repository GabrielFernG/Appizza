using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Appizza.Api;

[Authorize]
public sealed class Phase1Hub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var user = Context.User!;
        if (user.FindFirst("establishment_id")?.Value is { } establishmentId) await Groups.AddToGroupAsync(Context.ConnectionId, $"establishment:{establishmentId}");
        if (user.FindFirst("token_type")?.Value == "device" && user.FindFirst("sub")?.Value is { } deviceId) await Groups.AddToGroupAsync(Context.ConnectionId, $"device:{deviceId}");
        if (user.FindFirst("token_type")?.Value == "user" && user.FindFirst("sub")?.Value is { } userId) await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        await base.OnConnectedAsync();
    }
}
