namespace Dustuu.Actions.BunnyCdnDeploy.Extensions;

public static class IEnumerableExtensions
{
    public static T? SingleOrDefaultEqualsIgnoreCase<T>
    (this IEnumerable<T> source, Func<T, string> func, string match) =>
        source.SingleOrDefault(t => func(t).Equals(match, StringComparison.InvariantCultureIgnoreCase));
}
