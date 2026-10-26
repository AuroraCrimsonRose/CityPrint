using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CityPrint.Core.Osm;

namespace CityPrint.Core.Carto;

/// <summary>
/// Reads an export from the Carto mod (GeoJSON layers plus an optional Depth.tif) and turns it into
/// an <see cref="OsmDocument"/> with OSM-style tags, so the rest of the app treats it like an OSM
/// Export file. Carto's own values are kept as <c>carto:*</c> tags for the inspector.
/// </summary>
public static class CartoImporter
{
    /// <summary>The layers Carto writes, as {Feature} or {City}_{Feature}.</summary>
    private static readonly string[] Layers =
        ["Area_Boundary", "Building_Boundary", "Network_Boundary", "Network_Centerline", "POI_Location", "Route_Centerline", "Zoning_Boundary"];

    /// <summary>Names Carto gives unnamed paths, tracks and seaways; they would only clutter the labels.</summary>
    private static readonly HashSet<string> GenericNames =
        ["Pathways", "Train Tracks", "Tram Tracks", "Subway Tracks", "Seaways", "Waterways", "Runways", "Taxiways", "Fences"];

    /// <summary>True for a Carto export folder (or its GeoJSON folder) or one of its layer files.</summary>
    public static bool IsCartoExport(string path) =>
        (Directory.Exists(path) || Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)) && FindLayers(path).Count > 0;

    /// <summary>Carto's default export folder, if it exists.</summary>
    public static string? DefaultFolder(string gameDataFolder)
    {
        var dir = Path.Combine(gameDataFolder, "ModsData", "Carto");
        return Directory.Exists(dir) ? dir : null;
    }

    public static OsmDocument Load(string path)
    {
        var sw = Stopwatch.StartNew();
        var layers = FindLayers(path);
        if (layers.Count == 0) throw new InvalidDataException("No Carto GeoJSON layers were found.");

        var b = new Builder();
        long size = 0;
        var json = new Dictionary<string, JsonDocument>();
        try
        {
            foreach (var (layer, file) in layers)
            {
                var bytes = File.ReadAllBytes(file);
                size += bytes.Length;
                int bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                json[layer] = JsonDocument.Parse(bytes.AsMemory(bom));
            }

            var depthFile = FindRaster(layers.Values.First(), "Depth");
            GeoTiff? depth = null;
            if (depthFile != null)
            {
                try { depth = GeoTiff.Load(depthFile); size += new FileInfo(depthFile).Length; }
                catch (Exception ex) { b.Diagnostics.Add(new(DiagnosticLevel.Warning, $"Couldn't read {Path.GetFileName(depthFile)}: {ex.Message} Water won't be shown.")); }
            }

            b.Frame = Frame.Find(json, depth, b.Diagnostics);

            foreach (var (layer, doc) in json)
            {
                foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
                {
                    var props = f.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
                    if (!f.TryGetProperty("geometry", out var g) || g.ValueKind != JsonValueKind.Object) continue;
                    switch (layer)
                    {
                        case "Network_Centerline": b.AddLines(g, NetworkTags(props, surface: false)); break;
                        case "Network_Boundary": b.AddAreas(g, NetworkTags(props, surface: true)); break;
                        case "Building_Boundary": b.AddAreas(g, BuildingTags(props)); break;
                        case "POI_Location": b.AddPoints(g, PoiTags(props)); break;
                        case "Route_Centerline": b.AddLines(g, RouteTags(props)); break;
                        case "Zoning_Boundary": b.AddAreas(g, ZoningTags(props)); break;
                        case "Area_Boundary": b.AddDistrict(g, props); break;
                    }
                }
            }

            if (depth != null) b.AddWater(depth);
            if (!json.ContainsKey("Route_Centerline"))
                b.Diagnostics.Add(new(DiagnosticLevel.Info, "No Route_Centerline layer; transport lines won't be shown."));
        }
        finally
        {
            foreach (var d in json.Values) d.Dispose();
        }

        return OsmDocument.FromElements(path, "Carto", size, sw.Elapsed, b.Nodes, b.Ways, b.Relations, b.Diagnostics);
    }

    // ---- finding files ---------------------------------------------------

    private static Dictionary<string, string> FindLayers(string path)
    {
        var result = new Dictionary<string, string>();
        string? dir = File.Exists(path) ? Path.GetDirectoryName(path) : Directory.Exists(path) ? path : null;
        if (dir is null) return result;
        if (Directory.Exists(Path.Combine(dir, "GeoJSON"))) dir = Path.Combine(dir, "GeoJSON");

        // A specific file picks the city prefix ("Middle Shore_Network_Centerline.json" → "Middle Shore_").
        string? prefix = null;
        if (File.Exists(path) && Layers.FirstOrDefault(l => Path.GetFileNameWithoutExtension(path).EndsWith(l, StringComparison.OrdinalIgnoreCase)) is { } picked)
            prefix = Path.GetFileNameWithoutExtension(path)[..^picked.Length];

        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            foreach (var layer in Layers)
            {
                if (!name.EndsWith(layer, StringComparison.OrdinalIgnoreCase)) continue;
                string p = name[..^layer.Length];
                if (p.Length > 0 && !p.EndsWith('_')) continue;
                prefix ??= p;
                if (p == prefix) result.TryAdd(layer, file);
            }
        }
        return result;
    }

    /// <summary>Finds a raster from the same export: next to the GeoJSON or in a sibling GeoTIFF folder.</summary>
    private static string? FindRaster(string anyLayerFile, string kind)
    {
        string dir = Path.GetDirectoryName(anyLayerFile)!;
        string name = Path.GetFileNameWithoutExtension(anyLayerFile);
        string prefix = Layers.FirstOrDefault(l => name.EndsWith(l, StringComparison.OrdinalIgnoreCase)) is { } l ? name[..^l.Length] : "";
        string?[] dirs = [dir, Path.Combine(Path.GetDirectoryName(dir) ?? dir, "GeoTIFF")];
        foreach (var d in dirs)
        {
            if (d is null) continue;
            var f = Path.Combine(d, prefix + kind + ".tif");
            if (File.Exists(f)) return f;
        }
        return null;
    }

    // ---- tag mapping -----------------------------------------------------

    private static string? Str(JsonElement props, string key) =>
        props.ValueKind == JsonValueKind.Object && props.TryGetProperty(key, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() is { Length: > 0 } s ? s : null,
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.True => "yes",
                JsonValueKind.False => "no",
                _ => null,
            }
            : null;

    private static double Num(JsonElement props, string key) =>
        props.ValueKind == JsonValueKind.Object && props.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static HashSet<string> Flags(string? s) =>
        s is null ? [] : s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static void Address(Dictionary<string, string> t, JsonElement props)
    {
        if (Str(props, "Address_Street") is { } street) t["addr:street"] = street;
        if (Num(props, "Address_Number") is > 0 and var number) t["addr:housenumber"] = N(number);
        if (Str(props, "Address_District") is { } district && !district.StartsWith('(')) t["addr:district"] = district;
    }

    /// <summary>Road, path, track and seaway tags; <paramref name="surface"/> gives the area:* form for road shapes.</summary>
    private static Dictionary<string, string>? NetworkTags(JsonElement props, bool surface)
    {
        var t = new Dictionary<string, string>();
        string obj = Str(props, "Object") ?? "";
        var cats = Flags(Str(props, "Category"));
        string asset = Str(props, "Asset") ?? "";
        string? direction = Str(props, "Direction");
        bool oneWay = direction is "Forward" or "Backward";

        string? key = null, value = null;
        switch (obj)
        {
            case "Road":
                key = "highway";
                if (cats.Contains("Highway"))
                    value = !oneWay ? "trunk" : asset.Contains("One-Lane") ? "motorway_link" : "motorway";
                else if (asset.Contains("Pedestrian")) value = "pedestrian";
                else if (asset.Contains("Alley")) value = "service";
                else if (cats.Contains("Large")) value = "primary";
                else if (cats.Contains("Medium")) value = "secondary";
                else if (cats.Contains("Small")) value = "residential";
                else if (cats.Contains("Tram")) (key, value) = ("railway", "tram");
                else value = "road";
                break;
            case "Pathway":
                key = "highway";
                value = cats.Contains("Bicycle") && !cats.Contains("Pathway") ? "cycleway" : "footway";
                break;
            case "Track":
                key = "railway";
                value = cats.Contains("Tram") ? "tram" : cats.Contains("Subway") ? "subway" : "rail";
                break;
            case "Waterway":
                if (surface) return null; // shipping lanes have no visible surface
                (key, value) = ("route", "ferry");
                break;
            case "Runway" or "Taxiway":
                (key, value) = ("aeroway", obj.ToLowerInvariant());
                break;
            case "Fence":
                (key, value) = ("barrier", "fence");
                break;
            case "Cable":
                if (surface) return null;
                (key, value) = ("power", cats.Contains("HighCable") ? "line" : "minor_line");
                break;
            default:
                return null; // pipes and anything else underground
        }
        // Only roads get drawn surfaces; paths and tracks read better as lines.
        if (surface && (key != "highway" || value is "pedestrian" or "footway" or "cycleway")) return null;
        t[surface ? "area:" + key : key] = value;

        if (Str(props, "Name") is { } name) t[obj == "Road" || !GenericNames.Contains(name) ? "name" : "carto:name"] = name;
        if (asset.Length > 0) t["carto:asset"] = asset;
        if (cats.Count > 0) t["carto:category"] = string.Join(", ", cats);
        if (Num(props, "Lane") is > 0 and var lanes && obj == "Road") t["lanes"] = N(lanes);
        if (Num(props, "Limit") is > 0 and var limit && obj is "Road" or "Track") t["maxspeed"] = N(limit);
        if (oneWay && obj == "Road") t["oneway"] = direction == "Forward" ? "yes" : "-1";
        switch (Str(props, "Form"))
        {
            case "Elevated": t["bridge"] = "yes"; t["layer"] = "1"; break;
            case "Tunnel": t["tunnel"] = "yes"; t["layer"] = "-1"; break;
        }
        return t;
    }

    private static Dictionary<string, string> BuildingTags(JsonElement props)
    {
        var t = new Dictionary<string, string>();
        var cats = Flags(Str(props, "Category"));
        string? zoning = Str(props, "Zoning") is { } z && z != "None" ? z : null;
        t["building"] = zoning switch
        {
            "Residential" => "residential",
            "Commercial" => "commercial",
            "Office" => "office",
            "Industrial" => "industrial",
            null when cats.Contains("Public") => "public",
            _ => "yes",
        };
        // Zoned buildings are named after their style ("MW Residential 1900s"); only unique buildings get a label.
        if (Str(props, "Name") is { } name) t[cats.Contains("Property") ? "carto:type" : "name"] = name;
        if (zoning != null) t["carto:zoning"] = zoning;
        if (cats.Count > 0) t["carto:category"] = string.Join(", ", cats);
        if (Str(props, "Asset") is { } asset) t["carto:asset"] = asset;
        double residents = Num(props, "Resident_Female") + Num(props, "Resident_Male");
        if (residents > 0) t["residents"] = N(residents);
        if (Num(props, "Employee") is > 0 and var employees) t["employees"] = N(employees);
        if (Num(props, "Household") is > 0 and var households) t["households"] = N(households);
        if (Num(props, "Level") is > 0 and var level) t["building:level"] = N(level);
        Address(t, props);
        return t;
    }

    private static Dictionary<string, string> PoiTags(JsonElement props)
    {
        var t = new Dictionary<string, string>();
        string category = Str(props, "Category") ?? "";
        string name = Str(props, "Name") ?? "";
        foreach (var (k, v) in PoiCategory(category, name)) t[k] = v;
        // Traffic lights, pylons and poles are named "<kind> <entity id>", which isn't worth showing.
        if (name.Length > 0) t[t.ContainsKey("power") && t["power"] is "tower" or "pole" || category == "TrafficLight" ? "carto:name" : "name"] = name;
        if (category.Length > 0) t["carto:category"] = category;
        if (Str(props, "Object") is { } obj) t["carto:object"] = obj;
        Address(t, props);
        return t;
    }

    private static IEnumerable<(string, string)> PoiCategory(string c, string name)
    {
        if (c.StartsWith("Stop"))
        {
            yield return ("public_transport", "platform");
            yield return ("osm_export_stop", c[4..].Replace("Passenger", "").ToLowerInvariant());
            switch (c)
            {
                case "StopBus": yield return ("highway", "bus_stop"); break;
                case "StopTram": yield return ("railway", "tram_stop"); break;
                case "StopPassengerTrain" or "StopSubway": yield return ("railway", "platform"); break;
            }
            yield break;
        }
        if (c.StartsWith("Building") && c != "BuildingBicycle")
        {
            yield return c switch
            {
                "BuildingBus" => ("amenity", "bus_station"),
                "BuildingPassengerAirplane" or "BuildingCargoAirplane" => ("aeroway", "aerodrome"),
                "BuildingFerry" or "BuildingPassengerShip" => ("amenity", "ferry_terminal"),
                "BuildingCargoShip" or "BuildingCargoTrain" => ("landuse", "port"),
                "BuildingTaxi" => ("amenity", "taxi"),
                "BuildingHelicopter" => ("aeroway", "heliport"),
                _ => ("railway", "station"),
            };
            if (c is "BuildingPassengerTrain" or "BuildingSubway" or "BuildingTram") yield return ("public_transport", "station");
            yield break;
        }
        if (c.StartsWith("Store"))
        {
            yield return c switch
            {
                "StoreBank" => ("amenity", "bank"),
                "StoreBar" => ("amenity", "bar"),
                "StoreRestaurant" => ("amenity", "restaurant"),
                "StoreGasStation" => ("amenity", "fuel"),
                "StoreHotel" => ("tourism", "hotel"),
                "StoreOffice" or "StoreSoftware" or "StoreTelecom" or "StoreMedia" => ("office", c[5..].ToLowerInvariant()),
                "StoreFood" => ("shop", "supermarket"),
                "StoreConvenienceStore" => ("shop", "convenience"),
                "StoreFashionStore" => ("shop", "clothes"),
                "StoreDrugStore" => ("shop", "chemist"),
                "StoreBookStore" => ("shop", "books"),
                "StoreCarStore" => ("shop", "car"),
                _ => ("shop", c == "StoreGeneric" ? "yes" : c[5..].ToLowerInvariant()),
            };
            yield break;
        }
        if (c.StartsWith("Education"))
        {
            yield return ("amenity", c switch { "EducationUniversity" => "university", "EducationCollege" => "college", _ => "school" });
            yield break;
        }
        if (c.StartsWith("Industrial"))
        {
            yield return ("man_made", c == "IndustrialWarehouse" ? "warehouse" : "works");
            yield break;
        }
        if (c.StartsWith("Depot"))
        {
            yield return ("amenity", "vehicle_depot");
            yield break;
        }
        (string, string)? tag = c switch
        {
            "Health" => ("amenity", name.Contains("Hospital", StringComparison.OrdinalIgnoreCase) ? "hospital" : "clinic"),
            "Police" => ("amenity", "police"),
            "Fire" or "FireWatchTower" => ("amenity", "fire_station"),
            "Admin" => ("amenity", "townhall"),
            "Post" => ("amenity", "post_office"),
            "PostBox" => ("amenity", "post_box"),
            "Prison" => ("amenity", "prison"),
            "Research" => ("amenity", "research_institute"),
            "Communication" => ("man_made", "communications_tower"),
            "Disaster" => ("emergency", "shelter"),
            "Maintenance" => ("amenity", "vehicle_depot"),
            "MortuaryCemetery" => ("amenity", "grave_yard"),
            "MortuaryCrematorium" or "MortuaryGeneric" => ("amenity", "crematorium"),
            "Waste" => ("amenity", "waste_transfer_station"),
            "Water" => ("man_made", "water_works"),
            "Sewage" => ("man_made", "wastewater_plant"),
            "PowerPlant" or "PowerGeneric" or "PowerDam" or "PowerBattery" => ("power", "plant"),
            "PowerTurbine" => ("power", "generator"),
            "PowerSubstation" => ("power", "substation"),
            "UtilityPylon" => ("power", "tower"),
            "UtilityPole" => ("power", "pole"),
            "TrafficLight" => ("highway", "traffic_signals"),
            "LevelCrossing" => ("railway", "level_crossing"),
            "Parking" => ("amenity", "parking"),
            "BicycleStand" or "BuildingBicycle" => ("amenity", "bicycle_parking"),
            "Helipad" => ("aeroway", "helipad"),
            "SpaceCenter" => ("tourism", "attraction"),
            "Attraction" => ("tourism", "attraction"),
            "Park" => ("leisure", "park"),
            _ => null,
        };
        if (tag is { } x) yield return x;
    }

    private static Dictionary<string, string> RouteTags(JsonElement props)
    {
        var t = new Dictionary<string, string>();
        string transport = (Str(props, "Transport") ?? "bus").ToLowerInvariant();
        t["osm_export_route"] = transport;
        if (Str(props, "Color") is { } colour) t["osm_export_route_color"] = colour;
        if (Str(props, "Name") is { } name) t["osm_export_route_name"] = name;
        if (Str(props, "Route") is { } number) t["ref"] = t["osm_export_route_ref"] = number;
        if (Str(props, "Object") is { } obj) t["carto:object"] = obj;
        foreach (var k in new[] { "Passenger", "Vehicle", "Stop", "Usage" })
            if (Num(props, k) is > 0 and var v) t["carto:" + k.ToLowerInvariant()] = N(v);
        return t;
    }

    private static Dictionary<string, string>? ZoningTags(JsonElement props)
    {
        string? zoning = Str(props, "Zoning");
        if (zoning is null or "None") return null;
        var t = new Dictionary<string, string> { ["carto:zoning"] = zoning, ["zoning"] = zoning };
        if (Str(props, "Color") is { } colour) t["colour"] = colour;
        if (Str(props, "Name") is { } name) t["carto:type"] = name;
        return t;
    }

    // ---- geometry --------------------------------------------------------

    /// <summary>Maps Carto's longitude/latitude back to game metres around the map centre.</summary>
    private sealed record Frame(int Zone, bool North, double CentreE, double CentreN)
    {
        public (double X, double Y) ToGame(double lon, double lat)
        {
            var (e, n) = Utm.Forward(lon, lat, Zone, North);
            return (e - CentreE, n - CentreN);
        }

        /// <summary>
        /// The map's centre is game (0,0). Carto's origin longitude/latitude is user-set, so the centre is
        /// found from the data: the 529 map tiles span the whole map, and Depth.tif covers it too.
        /// </summary>
        public static Frame Find(Dictionary<string, JsonDocument> json, GeoTiff? depth, List<Diagnostic> diagnostics)
        {
            var tiles = new List<(double Lon, double Lat)>();
            if (json.TryGetValue("Area_Boundary", out var areas))
                foreach (var f in areas.RootElement.GetProperty("features").EnumerateArray())
                    if (Str(f.GetProperty("properties"), "Object") == "MapTile")
                        tiles.AddRange(Positions(f.GetProperty("geometry")));

            var all = tiles.Count > 0 ? tiles : json.Values.SelectMany(d => d.RootElement.GetProperty("features").EnumerateArray())
                .SelectMany(f => Positions(f.GetProperty("geometry"))).ToList();
            if (all.Count == 0) return new Frame(31, true, 0, 0);

            // Carto picks the zone from its origin, which sits at the map centre; rounding guards
            // against an origin of exactly 0° landing on the wrong side of a zone boundary.
            double lon = Math.Round((all.Min(p => p.Lon) + all.Max(p => p.Lon)) / 2, 5);
            double lat = Math.Round((all.Min(p => p.Lat) + all.Max(p => p.Lat)) / 2, 5);
            int zone = Utm.ZoneOf(lon);
            bool north = lat >= 0;
            if (depth?.Epsg is { } epsg && Utm.FromEpsg(epsg) is { } z) (zone, north) = z;

            if (tiles.Count > 0)
            {
                var pts = tiles.Select(p => Utm.Forward(p.Lon, p.Lat, zone, north)).ToList();
                double minE = pts.Min(p => p.Easting), maxE = pts.Max(p => p.Easting);
                double minN = pts.Min(p => p.Northing), maxN = pts.Max(p => p.Northing);
                if (Math.Abs(maxE - minE - Projection.MapSize) > 50)
                    diagnostics.Add(new(DiagnosticLevel.Warning, $"The map tiles span {maxE - minE:N0} m instead of {Projection.MapSize:N0} m; the projection may not match the game."));
                return new Frame(zone, north, (minE + maxE) / 2, (minN + maxN) / 2);
            }
            if (depth?.Epsg != null)
                return new Frame(zone, north, (depth.Left + depth.Right) / 2, (depth.Top + depth.Bottom) / 2);

            diagnostics.Add(new(DiagnosticLevel.Warning, "No map tiles or Depth.tif in this export, so the map centre was estimated from the data. The tile grid may be offset."));
            var all2 = all.Select(p => Utm.Forward(p.Lon, p.Lat, zone, north)).ToList();
            return new Frame(zone, north, (all2.Min(p => p.Easting) + all2.Max(p => p.Easting)) / 2, (all2.Min(p => p.Northing) + all2.Max(p => p.Northing)) / 2);
        }
    }

    private static IEnumerable<(double Lon, double Lat)> Positions(JsonElement geometry)
    {
        if (!geometry.TryGetProperty("coordinates", out var c)) yield break;
        var stack = new Stack<JsonElement>([c]);
        while (stack.Count > 0)
        {
            var e = stack.Pop();
            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() == 0) continue;
            if (e[0].ValueKind == JsonValueKind.Number) yield return (e[0].GetDouble(), e[1].GetDouble());
            else foreach (var child in e.EnumerateArray()) stack.Push(child);
        }
    }

    private sealed class Builder
    {
        public List<OsmNode> Nodes { get; } = new();
        public List<OsmWay> Ways { get; } = new();
        public List<OsmRelation> Relations { get; } = new();
        public List<Diagnostic> Diagnostics { get; } = new();
        public Frame Frame { get; set; } = new(31, true, 0, 0);
        private long _id;

        private long Node(double lon, double lat, IReadOnlyDictionary<string, string>? tags = null)
        {
            var (x, y) = Frame.ToGame(lon, lat);
            return Node(x, y, lon, lat, tags);
        }

        private long Node(double x, double y, double lon, double lat, IReadOnlyDictionary<string, string>? tags = null)
        {
            var n = new OsmNode { Id = ++_id, X = x, Y = y, Lon = lon, Lat = lat, Tags = tags ?? OsmElement.NoTags };
            Nodes.Add(n);
            return n.Id;
        }

        private long[] Line(JsonElement coords)
        {
            var refs = new List<long>(coords.GetArrayLength());
            foreach (var p in coords.EnumerateArray()) refs.Add(Node(p[0].GetDouble(), p[1].GetDouble()));
            return refs.ToArray();
        }

        private long[] Ring(JsonElement coords)
        {
            var refs = Line(coords);
            // GeoJSON repeats the first position; OSM closes a ring by repeating the first node.
            if (refs.Length > 1) refs[^1] = refs[0];
            return refs;
        }

        private long Way(long[] refs, IReadOnlyDictionary<string, string> tags)
        {
            Ways.Add(new OsmWay { Id = ++_id, NodeRefs = refs, Tags = tags });
            return _id;
        }

        public void AddPoints(JsonElement g, Dictionary<string, string> tags)
        {
            if (g.GetProperty("type").GetString() == "Point")
            {
                var c = g.GetProperty("coordinates");
                Node(c[0].GetDouble(), c[1].GetDouble(), tags);
            }
        }

        public void AddLines(JsonElement g, Dictionary<string, string>? tags)
        {
            if (tags is null) return;
            var c = g.GetProperty("coordinates");
            switch (g.GetProperty("type").GetString())
            {
                case "LineString": Way(Line(c), tags); break;
                case "MultiLineString": foreach (var l in c.EnumerateArray()) Way(Line(l), tags); break;
            }
        }

        public void AddAreas(JsonElement g, Dictionary<string, string>? tags)
        {
            if (tags is null) return;
            var c = g.GetProperty("coordinates");
            var polygons = g.GetProperty("type").GetString() switch
            {
                "Polygon" => [c],
                "MultiPolygon" => c.EnumerateArray().ToList(),
                _ => new List<JsonElement>(),
            };
            foreach (var poly in polygons)
            {
                var rings = poly.EnumerateArray().Select(Ring).Where(r => r.Length >= 4).ToList();
                if (rings.Count == 0) continue;
                if (rings.Count == 1)
                {
                    Way(rings[0], tags);
                    continue;
                }
                // Holes need a multipolygon; its member ways carry no tags of their own.
                var members = rings.Select((r, i) => new OsmMember(OsmType.Way, Way(r, OsmElement.NoTags), i == 0 ? "outer" : "inner")).ToArray();
                Relations.Add(new OsmRelation { Id = ++_id, Members = members, Tags = new Dictionary<string, string>(tags) { ["type"] = "multipolygon" } });
            }
        }

        /// <summary>Districts become an outline plus a place name at their centre; map tiles are skipped (the app draws its own grid).</summary>
        public void AddDistrict(JsonElement g, JsonElement props)
        {
            if (Str(props, "Object") != "District" || Str(props, "Name") is not { } name) return;
            var tags = new Dictionary<string, string> { ["boundary"] = "administrative", ["carto:object"] = "District", ["name"] = name };
            double residents = Num(props, "Resident_Female") + Num(props, "Resident_Male");
            if (residents > 0) tags["population"] = N(residents);
            foreach (var k in new[] { "Household", "Employee", "Company" })
                if (Num(props, k) is > 0 and var v) tags["carto:" + k.ToLowerInvariant()] = N(v);
            AddAreas(g, tags);

            var pts = Positions(g).Select(p => Frame.ToGame(p.Lon, p.Lat)).ToList();
            if (pts.Count == 0) return;
            double x = pts.Average(p => p.X), y = pts.Average(p => p.Y);
            var place = new Dictionary<string, string> { ["place"] = "suburb", ["name"] = name };
            if (tags.TryGetValue("population", out var pop)) place["population"] = pop;
            Node(x, y, Positions(g).Average(p => p.Lon), Positions(g).Average(p => p.Lat), place);
        }

        /// <summary>Water is wherever Depth.tif has a positive depth; its outline becomes one multipolygon.</summary>
        public void AddWater(GeoTiff depth)
        {
            const double MinDepth = 0.05;
            var v = depth.Values;
            int w = depth.Width;
            var rings = Contours.Trace(depth.Width, depth.Height, (x, y) =>
            {
                float d = v[y * w + x];
                return depth.IsNoData(d) ? 0 : d;
            }, MinDepth);

            double pw = depth.PixelWidth, ph = depth.PixelHeight;
            var members = new List<OsmMember>();
            foreach (var ring in rings)
            {
                // Pixel space (y down) to projected metres, then to game metres around the map centre.
                var simplified = Contours.Simplify(ring, 0.35);
                var pts = new double[simplified.Length];
                for (int i = 0; i < simplified.Length; i += 2)
                {
                    pts[i] = depth.Left + (simplified[i] + 0.5) * pw - Frame.CentreE;
                    pts[i + 1] = depth.Top - (simplified[i + 1] + 0.5) * ph - Frame.CentreN;
                }
                double area = Contours.SignedArea(pts);
                if (Math.Abs(area) < 4 * pw * ph) continue; // specks
                var refs = new long[pts.Length / 2 + 1];
                for (int i = 0; i < pts.Length; i += 2)
                    refs[i / 2] = Node(pts[i], pts[i + 1], Projection.ToLon(pts[i]), Projection.ToLat(pts[i + 1]));
                refs[^1] = refs[0];
                members.Add(new OsmMember(OsmType.Way, Way(refs, OsmElement.NoTags), area < 0 ? "outer" : "inner"));
            }
            if (members.Count == 0) return;
            Relations.Add(new OsmRelation
            {
                Id = ++_id,
                Members = members.ToArray(),
                Tags = new Dictionary<string, string> { ["type"] = "multipolygon", ["natural"] = "water", ["carto:source"] = "Depth.tif" },
            });
        }
    }
}
