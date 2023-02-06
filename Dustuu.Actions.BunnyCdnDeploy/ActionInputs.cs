using CommandLine;

namespace Dustuu.Actions.BunnyCdnDeploy;

public class ActionInputs
{
    [Option('w', "workspace", Required = true)]
    public string Workspace { get; set; } = null!;

    [Option('d', "directory", Required = true)]
    public string Directory { get; set; } = null!;

    [Option('m', "branch-main-name")]
    public string BranchMainName { get; set; } = "main";

    [Option('a', "api-key", Required = true)]
    public string ApiKey { get; set; } = null!;

    [Option('i', "dns-zone-id", Required = true)]
    public string DnsZoneId { get; set; } = null!;

    [Option('s', "dns-subdomain")]
    public string DnsSubdomain { get; set; } = string.Empty;

    [Option('l', "debug-limit")]
    public int DebugLimit { get; set; }
}
