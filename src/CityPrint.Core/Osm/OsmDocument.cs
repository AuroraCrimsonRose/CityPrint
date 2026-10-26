using System.Diagnostics;
using System.Globalization;
using System.Xml;

namespace CityPrint.Core.Osm;

public enum DiagnosticLevel { Info, Warning, Error }

public sealed record Diagnostic(DiagnosticLevel Level, string Message, string? ElementKey = null);

public sealed class OsmDocument
{
    public string? FilePath { get; private set; }
    public string? Generator { get; private set; }
    public string? Version { get; private set; }
    public long FileSize { get; private set; }
    public TimeSpan ParseTime { get; private set; }

    public Dictionary<long, OsmNode> Nodes { get; } = new();
    public Dictionary<long, OsmWay> Ways { get; } = new();
    public Dictionary<long, OsmRelation> Relations { get; } = new();

    /// <summary>Identical elements written more than once (a known OSM Export quirk).</summary>
    public int DuplicateNodes { get; private set; }
    public int DuplicateWays { get; private set; }
    public int DuplicateRelations { get; private set; }

    /// <summary>Same id written twice with different content; first copy wins.</summary>
    public int ConflictingDuplicates { get; private set; }

    public List<Diagnostic> Diagnostics { get; } = new();

    public static OsmDocument Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        var doc = Load(stream);
        doc.FilePath = path;
        doc.FileSize = stream.Length;
        return doc;
    }

    public static OsmDocument Load(Stream stream)
    {
        var sw = Stopwatch.StartNew();
        var doc = new OsmDocument();
        var settings = new XmlReaderSettings
        {
            IgnoreWhitespace = true,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            DtdProcessing = DtdProcessing.Prohibit,
        };
        using (var reader = XmlReader.Create(stream, settings))
        {
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                switch (reader.Name)
                {
                    case "osm":
                        doc.Generator = reader.GetAttribute("generator");
                        doc.Version = reader.GetAttribute("version");
                        break;
                    case "node":
                        doc.Add(ReadNode(reader));
                        break;
                    case "way":
                        doc.Add(ReadWay(reader));
                        break;
                    case "relation":
                        doc.Add(ReadRelation(reader));
                        break;
                }
            }
        }
        doc.Validate();
        doc.ParseTime = sw.Elapsed;
        return doc;
    }

    private void Add(OsmNode n)
    {
        if (Nodes.TryAdd(n.Id, n)) return;
        var first = Nodes[n.Id];
        if (first.Lat == n.Lat && first.Lon == n.Lon && SameTags(first, n)) DuplicateNodes++;
        else Conflict(n);
    }

    private void Add(OsmWay w)
    {
        if (Ways.TryAdd(w.Id, w)) return;
        var first = Ways[w.Id];
        if (first.NodeRefs.AsSpan().SequenceEqual(w.NodeRefs) && SameTags(first, w)) DuplicateWays++;
        else Conflict(w);
    }

    private void Add(OsmRelation r)
    {
        if (Relations.TryAdd(r.Id, r)) return;
        var first = Relations[r.Id];
        if (first.Members.AsSpan().SequenceEqual(r.Members) && SameTags(first, r)) DuplicateRelations++;
        else Conflict(r);
    }

    private void Conflict(OsmElement e)
    {
        ConflictingDuplicates++;
        Diagnostics.Add(new(DiagnosticLevel.Error, $"{e.Key} appears more than once with different content; using the first copy.", e.Key));
    }

    private static bool SameTags(OsmElement a, OsmElement b) =>
        a.Tags.Count == b.Tags.Count && a.Tags.All(kv => b.Tags.TryGetValue(kv.Key, out var v) && v == kv.Value);

    private void Validate()
    {
        int dupTotal = DuplicateNodes + DuplicateWays + DuplicateRelations;
        if (dupTotal > 0)
            Diagnostics.Insert(0, new(DiagnosticLevel.Warning,
                $"{dupTotal:N0} elements were written more than once with identical content " +
                $"({DuplicateNodes:N0} nodes, {DuplicateWays:N0} ways, {DuplicateRelations:N0} relations). Duplicates were ignored."));

        foreach (var w in Ways.Values)
        {
            int missing = w.NodeRefs.Count(r => !Nodes.ContainsKey(r));
            if (missing > 0)
                Diagnostics.Add(new(DiagnosticLevel.Error, $"{w.Key} references {missing} missing node(s).", w.Key));
            if (w.NodeRefs.Length < 2)
                Diagnostics.Add(new(DiagnosticLevel.Warning, $"{w.Key} has fewer than 2 nodes.", w.Key));
        }

        foreach (var r in Relations.Values)
        {
            foreach (var m in r.Members)
            {
                bool exists = m.Type switch
                {
                    OsmType.Node => Nodes.ContainsKey(m.Ref),
                    OsmType.Way => Ways.ContainsKey(m.Ref),
                    _ => Relations.ContainsKey(m.Ref),
                };
                if (!exists)
                    Diagnostics.Add(new(DiagnosticLevel.Error, $"{r.Key} references missing {m.Type.ToString().ToLowerInvariant()}/{m.Ref}.", r.Key));
            }
        }

        int untaggedOrphans = 0;
        var used = new HashSet<long>(Ways.Values.SelectMany(w => w.NodeRefs));
        foreach (var n in Nodes.Values)
            if (n.Tags.Count == 0 && !used.Contains(n.Id)) untaggedOrphans++;
        if (untaggedOrphans > 0)
            Diagnostics.Add(new(DiagnosticLevel.Info, $"{untaggedOrphans:N0} untagged nodes are not part of any way."));
    }

    // ---- element readers -------------------------------------------------

    private static OsmNode ReadNode(XmlReader r)
    {
        long id = Long(r, "id");
        double lat = Double(r, "lat"), lon = Double(r, "lon");
        var tags = ReadChildren(r, null, null);
        return new OsmNode
        {
            Id = id, Lat = lat, Lon = lon,
            X = Projection.ToX(lon), Y = Projection.ToY(lat),
            Tags = tags,
        };
    }

    private static OsmWay ReadWay(XmlReader r)
    {
        long id = Long(r, "id");
        var refs = new List<long>();
        var tags = ReadChildren(r, refs, null);
        return new OsmWay { Id = id, NodeRefs = refs.ToArray(), Tags = tags };
    }

    private static OsmRelation ReadRelation(XmlReader r)
    {
        long id = Long(r, "id");
        var members = new List<OsmMember>();
        var tags = ReadChildren(r, null, members);
        return new OsmRelation { Id = id, Members = members.ToArray(), Tags = tags };
    }

    private static IReadOnlyDictionary<string, string> ReadChildren(XmlReader r, List<long>? refs, List<OsmMember>? members)
    {
        if (r.IsEmptyElement) return OsmElement.NoTags;
        Dictionary<string, string>? tags = null;
        int depth = r.Depth;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth) break;
            if (r.NodeType != XmlNodeType.Element) continue;
            switch (r.Name)
            {
                case "tag":
                    var k = r.GetAttribute("k");
                    if (k != null) (tags ??= new())[k] = r.GetAttribute("v") ?? "";
                    break;
                case "nd":
                    refs?.Add(Long(r, "ref"));
                    break;
                case "member":
                    members?.Add(new OsmMember(
                        r.GetAttribute("type") switch { "node" => OsmType.Node, "relation" => OsmType.Relation, _ => OsmType.Way },
                        Long(r, "ref"),
                        r.GetAttribute("role") ?? ""));
                    break;
            }
        }
        return tags ?? OsmElement.NoTags;
    }

    private static long Long(XmlReader r, string name) =>
        long.Parse(r.GetAttribute(name) ?? "0", CultureInfo.InvariantCulture);

    private static double Double(XmlReader r, string name) =>
        double.Parse(r.GetAttribute(name) ?? "0", CultureInfo.InvariantCulture);
}
