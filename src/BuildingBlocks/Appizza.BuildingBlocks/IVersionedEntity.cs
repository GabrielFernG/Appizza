namespace Appizza.BuildingBlocks;

public interface IVersionedEntity
{
    long Version { get; set; }
}
