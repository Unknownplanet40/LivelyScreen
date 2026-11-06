using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Windows.UI;
using LivelyScreen.Models;

namespace LivelyScreen.Services;

public static class KnowledgeMapService
{
    private const int SQLITE_OK = 0;
    private const int SQLITE_ROW = 100;
    private const int SQLITE_OPEN_READONLY = 0x00000001;
    private const int SQLITE_OPEN_NOMUTEX = 0x00008000;

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr zVfs);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close_v2(IntPtr db);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr db, byte[] zSql, int nByte, out IntPtr ppStmt, IntPtr pzTail);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr pStmt);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr pStmt);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_int(IntPtr pStmt, int iCol);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr pStmt, int iCol);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_bytes(IntPtr pStmt, int iCol);

    private static readonly object _syncLock = new();
    private static KnowledgeMapData? _cachedData;
    private static string? _cachedDbPath;
    private static DateTime _cachedDbTimestamp;

    public static Color GetColorForType(string? type) => (type ?? "").Trim().ToLowerInvariant() switch
    {
        "troubleshooting" or "trouble" => KnowledgeMapPalette.TypeTroubleshooting,
        "note" => KnowledgeMapPalette.TypeNote,
        "quote" => KnowledgeMapPalette.TypeQuote,
        "saying" => KnowledgeMapPalette.TypeSaying,
        "joke" => KnowledgeMapPalette.TypeJoke,
        "book" => KnowledgeMapPalette.TypeBook,
        "diary" => KnowledgeMapPalette.TypeDiary,
        "recipe" => KnowledgeMapPalette.TypeRecipe,
        _ => KnowledgeMapPalette.TypeFallback
    };

    public static KnowledgeMapData LoadGraph(string? configuredPath, bool forceReload = false)
    {
        lock (_syncLock)
        {
            string dbPath = ResolveDbPath(configuredPath);
            DateTime currentFileTime = File.Exists(dbPath) ? File.GetLastWriteTimeUtc(dbPath) : DateTime.MinValue;

            if (!forceReload && _cachedData != null && string.Equals(_cachedDbPath, dbPath, StringComparison.OrdinalIgnoreCase) && _cachedDbTimestamp == currentFileTime)
            {
                return _cachedData;
            }

            KnowledgeMapData data;
            if (File.Exists(dbPath))
            {
                data = QuerySqliteGraph(dbPath);
                if (data.Nodes.Count > 0)
                {
                    data.IsConnectedToDb = true;
                    data.StatusSummary = $"Connected ({data.Nodes.Count} notes, {data.Edges.Count} links)";
                }
                else
                {
                    data = GenerateDemoGraph();
                    data.IsConnectedToDb = false;
                    data.StatusSummary = "Demo Constellation (Empty Database)";
                }
            }
            else
            {
                data = GenerateDemoGraph();
                data.IsConnectedToDb = false;
                data.StatusSummary = "Demo Constellation (Database Not Found)";
            }

            ComputePhysicalLayout(data);

            _cachedData = data;
            _cachedDbPath = dbPath;
            _cachedDbTimestamp = currentFileTime;

            return data;
        }
    }

    public static string ResolveDatabasePath(string? configuredPath) => ResolveDbPath(configuredPath);

    public static string GetGraphJson(AppSettings settings)
    {
        var data = LoadGraph(settings.KnowledgeMapDatabasePath);
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"style\":\"{(settings.KnowledgeMapStyle == KnowledgeMapStyle.Celestial3D ? "3d" : "2d")}\",");
        sb.Append($"\"speed\":{settings.KnowledgeMapOrbitSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        sb.Append($"\"pitch\":{settings.KnowledgeMapCameraPitch.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
        sb.Append($"\"suppressWidgets\":{(settings.KnowledgeMapSuppressWidgets ? "true" : "false")},");

        sb.Append("\"nodes\":[");
        for (int i = 0; i < data.Nodes.Count; i++)
        {
            var n = data.Nodes[i];
            if (i > 0) sb.Append(",");
            sb.Append("{");
            sb.Append($"\"id\":{n.Id},");
            sb.Append($"\"title\":{System.Text.Json.JsonSerializer.Serialize(n.Title)},");
            sb.Append($"\"label\":{System.Text.Json.JsonSerializer.Serialize(n.Label)},");
            sb.Append($"\"type\":{System.Text.Json.JsonSerializer.Serialize(n.Type)},");
            sb.Append($"\"favorite\":{(n.Favorite ? "true" : "false")},");
            sb.Append($"\"degree\":{n.Degree},");
            sb.Append($"\"x\":{n.X.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"y\":{n.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"z\":{n.Z.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            sb.Append("}");
        }
        sb.Append("],");

        sb.Append("\"edges\":[");
        for (int i = 0; i < data.Edges.Count; i++)
        {
            var e = data.Edges[i];
            if (i > 0) sb.Append(",");
            sb.Append("{");
            sb.Append($"\"source\":{e.SourceId},");
            sb.Append($"\"target\":{e.TargetId},");
            sb.Append($"\"hop\":{e.HopDistance}");
            sb.Append("}");
        }
        sb.Append("]");

        sb.Append("}");
        return sb.ToString();
    }

    public static bool IsAvailable(string? configuredPath = null)
    {
        string path = ResolveDbPath(configuredPath);
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    public static string ResolveDbPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        string defaultPath = @"C:\Users\ryanj\OneDrive\Desktop\Applications\KnowledgeBase-Quotes\data\brain.db";
        if (File.Exists(defaultPath))
            return defaultPath;

        string appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "OneDrive", "Desktop", "Applications", "KnowledgeBase-Quotes", "data", "brain.db");
        if (File.Exists(appDataPath))
            return appDataPath;

        return configuredPath ?? defaultPath;
    }

    private static KnowledgeMapData QuerySqliteGraph(string dbPath)
    {
        var data = new KnowledgeMapData();
        IntPtr db = IntPtr.Zero;

        try
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(dbPath + "\0");
            int rc = sqlite3_open_v2(pathBytes, out db, SQLITE_OPEN_READONLY | SQLITE_OPEN_NOMUTEX, IntPtr.Zero);
            if (rc != SQLITE_OK || db == IntPtr.Zero)
                return data;

            var nodeMap = new Dictionary<int, KnowledgeMapNode>();
            var seenEdges = new HashSet<string>();

            // 1. Query ONLY real notes from entries table (NO tag hubs, NO folder hubs, NO author hubs)
            string sqlEntries = "SELECT id, title, type, author, tags, favorite FROM entries WHERE title IS NOT NULL AND length(trim(title)) > 0;";
            byte[] sqlBytes = Encoding.UTF8.GetBytes(sqlEntries + "\0");
            IntPtr stmt = IntPtr.Zero;

            rc = sqlite3_prepare_v2(db, sqlBytes, sqlBytes.Length, out stmt, IntPtr.Zero);
            if (rc == SQLITE_OK && stmt != IntPtr.Zero)
            {
                while (sqlite3_step(stmt) == SQLITE_ROW)
                {
                    int id = sqlite3_column_int(stmt, 0);
                    string title = ReadColumnString(stmt, 1) ?? $"Note {id}";
                    string type = ReadColumnString(stmt, 2) ?? "note";
                    string? author = ReadColumnString(stmt, 3);
                    string? tags = ReadColumnString(stmt, 4);
                    int favInt = sqlite3_column_int(stmt, 5);

                    var node = new KnowledgeMapNode
                    {
                        Id = id,
                        Title = title,
                        Label = TruncateLabel(title),
                        Type = type.ToLowerInvariant().Trim(),
                        Author = author,
                        Tags = tags,
                        Favorite = favInt != 0,
                        Color = GetColorForType(type)
                    };

                    data.Nodes.Add(node);
                    nodeMap[id] = node;
                }
                sqlite3_finalize(stmt);
            }

            // 2. Query direct links from links table
            string sqlLinks = "SELECT source_id, target_id FROM links WHERE target_id IS NOT NULL;";
            byte[] linkBytes = Encoding.UTF8.GetBytes(sqlLinks + "\0");
            IntPtr linkStmt = IntPtr.Zero;

            rc = sqlite3_prepare_v2(db, linkBytes, linkBytes.Length, out linkStmt, IntPtr.Zero);
            if (rc == SQLITE_OK && linkStmt != IntPtr.Zero)
            {
                while (sqlite3_step(linkStmt) == SQLITE_ROW)
                {
                    int srcId = sqlite3_column_int(linkStmt, 0);
                    int tgtId = sqlite3_column_int(linkStmt, 1);

                    if (srcId == tgtId) continue;

                    if (nodeMap.TryGetValue(srcId, out var srcNode) && nodeMap.TryGetValue(tgtId, out var tgtNode))
                    {
                        int minId = Math.Min(srcId, tgtId);
                        int maxId = Math.Max(srcId, tgtId);
                        string key = $"{minId}:{maxId}";
                        if (!seenEdges.Add(key)) continue;

                        srcNode.Degree++;
                        tgtNode.Degree++;

                        data.Edges.Add(new KnowledgeMapEdge
                        {
                            SourceId = srcId,
                            TargetId = tgtId,
                            SourceNode = srcNode,
                            TargetNode = tgtNode
                        });
                    }
                }
                sqlite3_finalize(linkStmt);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[KnowledgeMapService] SQLite error: {ex.Message}");
        }
        finally
        {
            if (db != IntPtr.Zero) sqlite3_close_v2(db);
        }

        return data;
    }

    /// <summary>
    /// Computes physical force layout matching Stash GraphEngine.js.
    /// Connected nodes relax via Hooke link attraction and Coulomb repulsion.
    /// Unconnected / orphan nodes are distributed smoothly along outer brain silhouette.
    /// </summary>
    public static void ComputePhysicalLayout(KnowledgeMapData data)
    {
        if (data.Nodes.Count == 0) return;

        // 1. Calculate node radii based on degree: radiusFor(degree) = 3.2 + sqrt(degree) * 2.1
        foreach (var node in data.Nodes)
        {
            node.Radius = 3.2f + MathF.Sqrt(Math.Max(0, node.Degree)) * 2.1f;
        }

        // 2. Category Radial Anchors for connected nodes
        var distinctTypes = data.Nodes
            .Where(n => n.Degree > 0)
            .Select(n => n.Type)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctTypes.Count == 0)
        {
            distinctTypes = data.Nodes.Select(n => n.Type).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        int numTypes = Math.Max(1, distinctTypes.Count);
        var typeAnchors = new Dictionary<string, (float X, float Y)>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < numTypes; i++)
        {
            float angle = (i / (float)numTypes) * (MathF.PI * 2.0f) - (MathF.PI * 0.5f);
            float anchorX = 220.0f * MathF.Cos(angle);
            float anchorY = 220.0f * MathF.Sin(angle);
            typeAnchors[distinctTypes[i]] = (anchorX, anchorY);
        }

        var rng = new Random(1337);
        var connectedNodes = data.Nodes.Where(n => n.Degree > 0).ToList();
        int connCount = connectedNodes.Count;

        if (connCount > 0)
        {
            float[] x = new float[connCount];
            float[] y = new float[connCount];
            float[] vx = new float[connCount];
            float[] vy = new float[connCount];

            var nodeIndexMap = new Dictionary<int, int>();
            for (int i = 0; i < connCount; i++)
            {
                var node = connectedNodes[i];
                nodeIndexMap[node.Id] = i;

                if (!typeAnchors.TryGetValue(node.Type, out var anchor))
                {
                    anchor = (0f, 0f);
                }

                x[i] = anchor.X + ((float)rng.NextDouble() - 0.5f) * 50.0f;
                y[i] = anchor.Y + ((float)rng.NextDouble() - 0.5f) * 50.0f;
            }

            // Run 80 relaxation steps
            const int steps = 80;
            int edgeCount = data.Edges.Count;

            for (int step = 0; step < steps; step++)
            {
                // Hooke Link Attraction
                for (int e = 0; e < edgeCount; e++)
                {
                    var edge = data.Edges[e];
                    if (!nodeIndexMap.TryGetValue(edge.SourceId, out int iA) ||
                        !nodeIndexMap.TryGetValue(edge.TargetId, out int iB))
                        continue;

                    float dx = x[iB] - x[iA];
                    float dy = y[iB] - y[iA];
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist < 0.001f) dist = 0.001f;

                    int maxDeg = Math.Max(connectedNodes[iA].Degree, connectedNodes[iB].Degree);
                    float distTarget = Math.Clamp(46.0f + MathF.Sqrt(maxDeg) * 15.0f, 50.0f, 160.0f);
                    float kLink = maxDeg > 15 ? 0.45f : 0.75f;

                    float force = (dist - distTarget) * kLink / dist;
                    float fx = dx * force * 0.1f;
                    float fy = dy * force * 0.1f;

                    vx[iA] += fx;
                    vy[iA] += fy;
                    vx[iB] -= fx;
                    vy[iB] -= fy;
                }

                // Coulomb Repulsion
                for (int i = 0; i < connCount; i++)
                {
                    int degI = connectedNodes[i].Degree;
                    float repI = degI > 15 ? 260.0f : (degI > 5 ? 150.0f : 85.0f);

                    for (int j = i + 1; j < connCount; j++)
                    {
                        float dx = x[i] - x[j];
                        float dy = y[i] - y[j];
                        float distSq = dx * dx + dy * dy;
                        float minDist = connectedNodes[i].Radius + connectedNodes[j].Radius + 8.0f;
                        float effectiveDistSq = Math.Max(distSq, minDist * minDist);
                        float dist = MathF.Sqrt(effectiveDistSq);

                        float fRepulse = (repI * 0.14f) / effectiveDistSq;
                        float rx = (dx / dist) * fRepulse;
                        float ry = (dy / dist) * fRepulse;

                        vx[i] += rx;
                        vy[i] += ry;
                        vx[j] -= rx;
                        vy[j] -= ry;
                    }

                    // Gentle anchor gravity
                    if (typeAnchors.TryGetValue(connectedNodes[i].Type, out var anc))
                    {
                        vx[i] += (anc.X - x[i]) * 0.02f;
                        vy[i] += (anc.Y - y[i]) * 0.02f;
                    }
                }

                // Damping & Position Update
                for (int i = 0; i < connCount; i++)
                {
                    vx[i] *= 0.62f;
                    vy[i] *= 0.62f;
                    x[i] += vx[i];
                    y[i] += vy[i];
                }
            }

            for (int i = 0; i < connCount; i++)
            {
                connectedNodes[i].X = x[i];
                connectedNodes[i].Y = y[i];
            }
        }

        // 3. Centroid and Cluster Radius of Connected Network
        float cx = 0, cy = 0;
        if (connCount > 0)
        {
            for (int i = 0; i < connCount; i++)
            {
                cx += connectedNodes[i].X;
                cy += connectedNodes[i].Y;
            }
            cx /= connCount;
            cy /= connCount;
        }

        float maxDistSq = 0;
        for (int i = 0; i < connCount; i++)
        {
            float dSq = (connectedNodes[i].X - cx) * (connectedNodes[i].X - cx) +
                        (connectedNodes[i].Y - cy) * (connectedNodes[i].Y - cy);
            if (dSq > maxDistSq) maxDistSq = dSq;
        }

        float clusterRadius = Math.Max(260.0f, MathF.Sqrt(maxDistSq) * 1.15f);

        // 4. Distribute Unconnected (Orphan) Notes around outer brain silhouette
        var orphanNodes = data.Nodes.Where(n => n.Degree == 0).ToList();
        int totalOrphans = orphanNodes.Count;

        for (int i = 0; i < totalOrphans; i++)
        {
            var node = orphanNodes[i];
            var (ox, oy, oz) = ComputeBrainOrphanPosition(node.Id, i, totalOrphans, cx, cy, clusterRadius);
            node.X = ox;
            node.Y = oy;
            node.Z = oz;
        }

        // 5. 3D Spherical Harmonic Z Depth for Connected Nodes
        for (int i = 0; i < connCount; i++)
        {
            var node = connectedNodes[i];
            float dist = MathF.Sqrt((node.X - cx) * (node.X - cx) + (node.Y - cy) * (node.Y - cy));
            float zBase = MathF.Pow(dist / 320.0f, 1.4f) * 45.0f - 20.0f;
            float zHarmonic = MathF.Sin(node.Id * 1.618f) * 85.0f;
            node.Z = zBase + zHarmonic;
        }

        // 6. BFS Hop Tagging for Cascading Synaptic Waves
        TagHopDistances(data);
    }

    /// <summary>
    /// Dual-hemisphere brain silhouette from Stash graphMath.js computeBrainNodePosition.
    /// Distributes isolated nodes smoothly around perimeter without overlapping the core network.
    /// </summary>
    private static (float X, float Y, float Z) ComputeBrainOrphanPosition(int id, int index, int totalOrphans, float cx, float cy, float clusterRadius)
    {
        if (totalOrphans <= 0) return (cx, cy, 0f);

        float safeRadius = Math.Max(140.0f, clusterRadius);
        float side = (index % 2 == 0) ? -1.0f : 1.0f;
        int hIndex = index / 2;
        int hTotal = Math.Max(1, (totalOrphans + 1) / 2);

        float fissureGap = Math.Max(20.0f, safeRadius * 0.12f);
        float ax = safeRadius * 0.65f;
        float by = safeRadius * 0.88f;
        float cz = safeRadius * 0.45f;

        float cosPhi = 1.0f - 2.0f * ((hIndex + 0.5f) / hTotal);
        float sinPhi = MathF.Sqrt(Math.Max(0.04f, 1.0f - cosPhi * cosPhi));
        const float goldenAngle = 2.3999632f;
        float azimuth = hIndex * goldenAngle;

        float localX = (MathF.Abs(MathF.Sin(azimuth)) * sinPhi) * ax + fissureGap;
        float localY = cosPhi * by;
        float localZ = MathF.Cos(azimuth) * sinPhi * cz;

        float sulcus = MathF.Sin(cosPhi * 9.0f + azimuth * 2.5f) * (safeRadius * 0.04f);

        float x = cx + side * Math.Max(fissureGap * 0.5f, localX + sulcus);
        float y = cy + localY + sulcus * 0.4f;
        float z = localZ + sulcus * 0.8f;

        return (x, y, z);
    }

    private static void TagHopDistances(KnowledgeMapData data)
    {
        if (data.Nodes.Count == 0) return;

        var primaryHub = data.Nodes.OrderByDescending(n => n.Degree).FirstOrDefault();
        if (primaryHub == null || primaryHub.Degree == 0)
        {
            data.MaxHop = 1;
            return;
        }

        var adj = new Dictionary<int, List<(int NeighborId, KnowledgeMapEdge Edge)>>();
        foreach (var edge in data.Edges)
        {
            if (!adj.TryGetValue(edge.SourceId, out var l1))
            {
                l1 = new List<(int, KnowledgeMapEdge)>();
                adj[edge.SourceId] = l1;
            }
            l1.Add((edge.TargetId, edge));

            if (!adj.TryGetValue(edge.TargetId, out var l2))
            {
                l2 = new List<(int, KnowledgeMapEdge)>();
                adj[edge.TargetId] = l2;
            }
            l2.Add((edge.SourceId, edge));
        }

        var visited = new HashSet<int> { primaryHub.Id };
        var queue = new Queue<(int NodeId, int Depth)>();
        queue.Enqueue((primaryHub.Id, 0));

        int maxHop = 1;

        while (queue.Count > 0)
        {
            var (currId, currDepth) = queue.Dequeue();

            if (adj.TryGetValue(currId, out var neighbors))
            {
                foreach (var (nextId, edge) in neighbors)
                {
                    int hop = currDepth + 1;
                    if (hop > maxHop) maxHop = hop;

                    if (edge.HopDistance == 1 || hop < edge.HopDistance)
                    {
                        edge.HopDistance = hop;
                    }

                    if (visited.Add(nextId))
                    {
                        queue.Enqueue((nextId, hop));
                    }
                }
            }
        }

        data.MaxHop = Math.Max(1, maxHop);
    }

    public static KnowledgeMapData GenerateDemoGraph()
    {
        var data = new KnowledgeMapData();
        var nodeMap = new Dictionary<int, KnowledgeMapNode>();
        var seenEdges = new HashSet<string>();

        var demoNotes = new (int id, string title, string type, bool fav)[]
        {
            (1, "Active Directory: Check & Fix Health", "troubleshooting", true),
            (2, "Active Directory: Add or Remove Domain Controller", "troubleshooting", false),
            (3, "Active Directory: Find and Export Inactive Users", "troubleshooting", false),
            (4, "Active Directory: Set or Extend Account Expiration", "troubleshooting", false),
            (5, "Active Directory: Offboard User Checklist", "troubleshooting", false),
            (6, "Windows 11 Installation & Clean Setup", "troubleshooting", true),
            (7, "Format a Disk Using DiskPart", "troubleshooting", false),
            (8, "Disable Hibernation & Reclaim Space", "troubleshooting", false),
            (9, "SSD and HDD Troubleshooting", "troubleshooting", true),
            (10, "NTFS vs FAT32 vs exFAT", "note", false),
            (11, "Windows CMD Reference", "note", false),
            (12, "Windows CMD Commands", "note", false),
            (13, "Windows Hardware Diagnostics", "troubleshooting", false),
            (14, "Windows 11 Offline Setup", "troubleshooting", false),
            (15, "Windows OOBE Setup", "troubleshooting", false),
            (16, "Network Troubleshooting", "troubleshooting", false),
            (17, "Straight-through Cable vs Crossover", "note", false),
            (18, "Patch Panel", "note", false),
            (19, "Router and Switch", "note", false),
            (20, "OSI Model", "note", true),
            (21, "DHCP, DNS, and WAP", "note", false),
            (22, "IP Address and MAC Address", "note", false),
            (23, "IPv4 and IPv6", "note", false),
            (24, "Windows Server 2022: Flush DNS & Cache", "troubleshooting", false),
            (25, "Windows Server 2022: Backup Setup", "troubleshooting", false),
            (26, "Retrieve Saved Wi-Fi Passwords", "troubleshooting", false),
            (27, "Kill Process by Port Number", "troubleshooting", false),
            (28, "Every wait has a worth", "saying", false),
            (29, "Trust the process", "saying", true),
            (30, "Rule number five, show no fear", "saying", false),
            (31, "A fish with its mouth shut never gets caught", "saying", false),
            (32, "You Won't Find the Same Person Twice", "saying", false),
            (33, "Ano tagalog ng ticket", "joke", false),
            (34, "Ano tawag sa kambal na stars", "joke", false),
            (35, "Ano english ng ABS-CBN", "joke", false),
            (36, "Tawag sa palakang may ngipin", "joke", false)
        };

        foreach (var (id, title, type, fav) in demoNotes)
        {
            var node = new KnowledgeMapNode
            {
                Id = id,
                Title = title,
                Label = TruncateLabel(title),
                Type = type,
                Favorite = fav,
                Color = GetColorForType(type)
            };
            data.Nodes.Add(node);
            nodeMap[id] = node;
        }

        var links = new[]
        {
            (1, 2), (2, 3), (3, 4), (4, 5), (1, 5),
            (6, 7), (7, 9), (9, 10), (10, 11), (11, 12),
            (13, 9), (14, 6), (15, 6),
            (16, 17), (17, 18), (18, 19), (19, 20), (20, 21), (21, 22), (22, 23),
            (24, 25), (25, 1), (26, 16), (27, 11), (20, 16)
        };

        foreach (var (src, tgt) in links)
        {
            if (nodeMap.TryGetValue(src, out var nSrc) && nodeMap.TryGetValue(tgt, out var nTgt))
            {
                nSrc.Degree++;
                nTgt.Degree++;
                data.Edges.Add(new KnowledgeMapEdge
                {
                    SourceId = src,
                    TargetId = tgt,
                    SourceNode = nSrc,
                    TargetNode = nTgt
                });
            }
        }

        return data;
    }

    private static string TruncateLabel(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string clean = text.Trim();
        return clean.Length > 28 ? clean.Substring(0, 25) + "…" : clean;
    }

    private static string? ReadColumnString(IntPtr stmt, int col)
    {
        IntPtr ptr = sqlite3_column_text(stmt, col);
        if (ptr == IntPtr.Zero) return null;
        int len = sqlite3_column_bytes(stmt, col);
        if (len <= 0) return string.Empty;
        byte[] bytes = new byte[len];
        Marshal.Copy(ptr, bytes, 0, len);
        return Encoding.UTF8.GetString(bytes);
    }
}
