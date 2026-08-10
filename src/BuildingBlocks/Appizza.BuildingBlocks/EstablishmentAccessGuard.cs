namespace Appizza.BuildingBlocks;

public static class EstablishmentAccessGuard
{
    public static void EnsureAccess(Guid resourceEstablishmentId, IEstablishmentContext context)
    {
        if (context.EstablishmentId is not { } currentEstablishmentId ||
            currentEstablishmentId != resourceEstablishmentId)
        {
            throw new UnauthorizedAccessException("The resource does not belong to the current establishment.");
        }
    }
}
