namespace Dustuu.Actions.BunnyCdnDeploy.Models.RequestBodies;

public record AddHostnameRequestBody
{
    public required string Hostname { get; init; }
}
