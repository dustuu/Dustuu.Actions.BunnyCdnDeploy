using CommandLine;

namespace Dustuu.Actions.BunnyCdnDeploy;

public class ActionInputs
{
    string _branchCurrentName = null!;
    string _branchMainName = "main";

    private static string CleanBranchName(string branchName) => !string.IsNullOrEmpty(branchName) ?
        branchName.Split('/')[^1].ToLowerInvariant() : string.Empty;

    [Option('w', "workspace", Required = true)]
    public string Workspace { get; set; } = null!;

    [Option('d', "directory", Required = true)]
    public string Directory { get; set; } = null!;

    [Option('c', "branch-current-name", Required = true)]
    public string BranchCurrentName
    {
        get => _branchCurrentName;
        set => _branchCurrentName = CleanBranchName(value);
    }

    [Option('m', "branch-main-name")]
    public string BranchMainName
    {
        get => _branchMainName;
        set => _branchMainName = CleanBranchName(value);
    }

    [Option('a', "api-key", Required = true)]
    public string ApiKey { get; set; } = null!;

    [Option('i', "dns-zone-id", Required = true)]
    public string DnsZoneId { get; set; } = null!;

    [Option('s', "dns-subdomain")]
    public string DnsSubdomain { get; set; } = string.Empty;

    [Option('l', "debug-limit")]
    public int DebugLimit { get; set; }
}
