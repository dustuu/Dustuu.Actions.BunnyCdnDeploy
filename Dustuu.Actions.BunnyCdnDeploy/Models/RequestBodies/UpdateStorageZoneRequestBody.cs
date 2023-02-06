namespace Dustuu.Actions.BunnyCdnDeploy.Models.RequestBodies;

public record UpdateStorageZoneRequestBody
{
    public required bool Rewrite404To200 { get; init; }
}
