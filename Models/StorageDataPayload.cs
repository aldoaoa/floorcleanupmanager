namespace EsdCleaningSystem.Models;

public class StorageDataPayload
{
    public List<FloorMapConfig> Maps { get; set; } = new();
    public List<CleaningRequest> Requests { get; set; } = new();
}
