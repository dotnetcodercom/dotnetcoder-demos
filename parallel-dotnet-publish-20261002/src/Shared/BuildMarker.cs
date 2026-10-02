using System.Reflection;

namespace Dnc.Shared;

public static class BuildMarker
{
    public static string Flavor => typeof(BuildMarker).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(x => x.Key == "DncFlavor").Value ?? "MISSING";
}
