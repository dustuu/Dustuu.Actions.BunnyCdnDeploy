using CommandLine;

namespace Dustuu.Actions.BunnyCdnDeploy;

public class ActionInputs
{
    [Option('w', "workspace", Required = true)]
    public string Workspace { get; set; } = null!;

    [Option('d', "directory", Required = true)]
    public string Directory { get; set; } = null!;

    [Option('a', "api-key", Required = true)]
    public string BunnyCdnApiKey { get; set; } = null!;

    [Option('u', "storage-zone-username", Required = true)]
    public string StorageZoneUsername { get; set; } = null!;

    [Option('p', "storage-zone-password", Required = true)]
    public string StorageZonePassword { get; set; } = null!;

    [Option('r', "storage-zone-region", Required = true)]
    public string StorageZoneRegion { get; set; } = null!;

    [Option('i', "pull-zone-id", Required = true)]
    public string PullZoneId { get; set; } = null!;
}
