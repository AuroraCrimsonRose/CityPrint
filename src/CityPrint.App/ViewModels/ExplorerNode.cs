using Avalonia.Media;
using CityPrint.Core.Map;

namespace CityPrint.App.ViewModels;

/// <summary>A row in the Explorer tree: a section, a consolidated group (e.g. a street), or one feature.</summary>
public sealed class ExplorerNode
{
    private IReadOnlyList<ExplorerNode>? _children;
    private readonly Func<IReadOnlyList<ExplorerNode>>? _makeChildren;

    public required string Title { get; init; }
    public string Detail { get; init; } = "";
    public string? Tip { get; init; }
    public IBrush? Swatch { get; init; }
    public bool IsSection { get; init; }
    public bool IsExpanded { get; set; }
    public FeatureGroup? Group { get; init; }
    public MapFeature? Feature { get; init; }

    public ExplorerNode() { }

    public ExplorerNode(Func<IReadOnlyList<ExplorerNode>> makeChildren) => _makeChildren = makeChildren;

    /// <summary>Built on first expand so huge cities don't create thousands of rows up front.</summary>
    public IReadOnlyList<ExplorerNode> Children => _children ??= _makeChildren?.Invoke() ?? [];

    public static IReadOnlyList<ExplorerNode> BuildTree(IEnumerable<MapFeature> features, bool expandAll)
    {
        var sections = new List<ExplorerNode>();
        foreach (var section in FeatureGroups.Build(features).GroupBy(g => g.Section))
        {
            var groups = section.ToList();
            int items = groups.Sum(g => g.Features.Count);
            sections.Add(new ExplorerNode(() => groups.Select(GroupNode).ToList())
            {
                Title = section.Key,
                Detail = $"{groups.Count:N0}",
                Tip = $"{groups.Count:N0} groups, {items:N0} features",
                IsSection = true,
                IsExpanded = expandAll || section.Key is "Roads" or "Rail & transit",
            });
        }
        return sections;
    }

    private static ExplorerNode GroupNode(FeatureGroup g)
    {
        var swatch = Swatch_(g.Features.First(f => f.Category == g.Category));
        if (g.Features.Count == 1)
            return new ExplorerNode { Title = g.Title, Detail = g.ShortSummary, Tip = g.Summary, Swatch = swatch, Group = g, Feature = g.Features[0] };

        return new ExplorerNode(() => g.Features.Select(f => new ExplorerNode
        {
            Title = f.Element.Key + (f.IsBridge ? " · bridge" : f.IsTunnel ? " · tunnel" : ""),
            Tip = $"{f.Style.Name} · {f.Element.Key}",
            Detail = f.Kind switch
            {
                GeometryKind.Line => MapStats.FormatLength(f.LengthMetres),
                GeometryKind.Area => MapStats.FormatArea(f.AreaSqMetres),
                _ => "",
            },
            Swatch = Swatch_(f),
            Feature = f,
        }).ToList())
        {
            Title = g.Title,
            Detail = g.ShortSummary,
            Tip = g.Summary,
            Swatch = swatch,
            Group = g,
        };
    }

    private static readonly Dictionary<uint, IBrush> Brushes = new();

    private static IBrush Swatch_(MapFeature f)
    {
        var s = f.Style;
        uint c = f.Colour ?? (s.Kind == GeometryKind.Area ? s.Fill : s.Stroke != 0 && s.Stroke != 0xFFFFFFFF ? s.Stroke : s.Casing != 0 ? s.Casing : s.Fill);
        if (!Brushes.TryGetValue(c, out var b)) Brushes[c] = b = new SolidColorBrush(Color.FromUInt32(c));
        return b;
    }
}
