namespace CityPrint.Core.Osm;

public enum OsmType { Node, Way, Relation }

public abstract class OsmElement
{
    public static readonly IReadOnlyDictionary<string, string> NoTags = new Dictionary<string, string>();

    public long Id { get; init; }
    public IReadOnlyDictionary<string, string> Tags { get; init; } = NoTags;
    public abstract OsmType Type { get; }

    public string? Tag(string key) => Tags.TryGetValue(key, out var v) ? v : null;
    public bool Has(string key) => Tags.ContainsKey(key);
    public bool Is(string key, string value) => Tags.TryGetValue(key, out var v) && v == value;

    public string Key => $"{Type.ToString().ToLowerInvariant()}/{Id}";
}

public sealed class OsmNode : OsmElement
{
    public override OsmType Type => OsmType.Node;
    public double Lat { get; init; }
    public double Lon { get; init; }

    /// <summary>Game-space metres east of the map centre.</summary>
    public double X { get; init; }

    /// <summary>Game-space metres north of the map centre.</summary>
    public double Y { get; init; }
}

public sealed class OsmWay : OsmElement
{
    public override OsmType Type => OsmType.Way;
    public long[] NodeRefs { get; init; } = [];
    public bool IsClosed => NodeRefs.Length > 3 && NodeRefs[0] == NodeRefs[^1];
}

public readonly record struct OsmMember(OsmType Type, long Ref, string Role);

public sealed class OsmRelation : OsmElement
{
    public override OsmType Type => OsmType.Relation;
    public OsmMember[] Members { get; init; } = [];
}
