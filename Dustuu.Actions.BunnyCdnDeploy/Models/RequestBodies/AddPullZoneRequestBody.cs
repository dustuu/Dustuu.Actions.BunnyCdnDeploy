namespace Dustuu.Actions.BunnyCdnDeploy.Models.RequestBodies;

public record AddPullZoneRequestBody
{
    public required string Name { get; init; }
    public required bool EnableGeoZoneUS { get; init; }
    public required bool EnableGeoZoneEU { get; init; }
    public required bool EnableGeoZoneASIA { get; init; }
    public required bool EnableGeoZoneSA { get; init; }
    public required bool EnableGeoZoneAF { get; init; }
    public required int OriginType { get; init; }
    public required int StorageZoneId { get; init; }
    public required int Type { get; init; }
}
