using Appizza.BuildingBlocks;

namespace Appizza.Modules.Communications;

public static class CommunicationStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Paused = "paused";
    public const string Expired = "expired";
    public const string Archived = "archived";
}

public static class CommunicationPermissions
{
    public static readonly string[] All = ["communications.view", "communications.create", "communications.publish", "communications.edit"];
}

public sealed class Communication : IVersionedEntity
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public string Title { get; set; } = null!;
    public string? Body { get; set; }
    public Guid? MediaAssetId { get; set; }
    public string MediaType { get; set; } = "image";
    public string Status { get; set; } = CommunicationStatuses.Draft;
    public int Priority { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}
