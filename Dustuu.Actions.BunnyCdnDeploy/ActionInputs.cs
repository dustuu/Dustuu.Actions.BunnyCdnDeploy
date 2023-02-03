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

    [Option('z', "dns-zone-id", Required = true)]
    public string DnsZoneId { get; set; } = null!;

    [Option('s', "dns-root-subdomain", Required = true)]
    public string DnsRootSubdomain { get; set; } = null!;

    string _branchName = null!;

    [Option('b', "branch", Required = true)]
    public string Branch
    {
        get => _branchName;
        set
        {
            if (value is { Length: > 0 } )
            { _branchName = value.Split("/")[^1]; }
        }
    }
}
