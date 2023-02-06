namespace Dustuu.Actions.BunnyCdnDeploy.Models;

public record StorageZone
{
    public const string REGION_GERMANY = "DE";

    public const int ZONE_TIER_SSD = 1;

    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Password { get; init; }
    public required PullZone[] PullZones { get; set; }
    public required string Region { get; init; }
    public required bool Rewrite404To200 { get; init; }
}
