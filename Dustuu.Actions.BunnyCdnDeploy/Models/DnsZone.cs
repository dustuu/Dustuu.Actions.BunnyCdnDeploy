namespace Dustuu.Actions.BunnyCdnDeploy.Models;

public record DnsZone
{
    public required string Domain { get; init; }
    public required Record[] Records { get; init; }

    public record Record
    {
        public const int TYPE_CNAME = 2;

        public int Id { get; init; }
        public required int Type { get; init; }
        public required string Value { get; init; }
        public required string Name { get; init; }
        public required int Ttl { get; init; } // In Seconds
    }
}
