namespace Appizza.Persistence;

public static class AppizzaSchemas
{
    public static IReadOnlyList<string> All { get; } =
    [
        "establishments", "identity", "catalog", "promotions", "media", "communications",
        "tables", "ordering", "kitchen", "payments", "devices", "operations", "reporting",
        "auditing", "integration"
    ];
}
