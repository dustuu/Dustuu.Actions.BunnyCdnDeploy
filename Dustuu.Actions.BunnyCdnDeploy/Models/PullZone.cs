namespace Dustuu.Actions.BunnyCdnDeploy.Models;

public record PullZone
{
    public const int TYPE_SMALL_FILES = 0;

    public const int ORIGIN_TYPE_STORAGE_ZONE = 2;

    public required int Id { get; init; }
    public required string Name { get; init; }
    public required HostName[] Hostnames { get; init; }

    public record HostName
    {
        public required string Value { get; init; }
        public required bool IsSystemHostname { get; init; }
    }
}
