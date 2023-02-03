namespace Dustuu.Actions.BunnyCdnDeploy.Models.RequestBodies;

public record SetForceSslRequestBody
{
    public required string Hostname { get; init; }
    public required bool ForceSSL { get; init; }
}
