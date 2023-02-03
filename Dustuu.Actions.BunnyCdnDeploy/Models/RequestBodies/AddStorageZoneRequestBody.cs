namespace Dustuu.Actions.BunnyCdnDeploy.Models.RequestBodies;

public record AddStorageZoneRequestBody
{
    public required string Name { get; init; }

    public required string Region { get; init; }

    public required int ZoneTier { get; init; }
}
