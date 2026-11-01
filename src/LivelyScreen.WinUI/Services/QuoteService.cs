using LivelyScreen.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LivelyScreen_WinUI;

namespace LivelyScreen.Services;

public sealed class QuoteItem
{
    public int Id { get; set; }
    public string Type { get; set; } = "Quote";
    public string QuoteText { get; set; } = "";
    public string AuthorLine { get; set; } = "";
    public string MeaningHeading { get; set; } = "";
    public string MeaningText { get; set; } = "";
    public string Subtext { get; set; } = "";
    public string Author { get; set; } = "";
    public string Source { get; set; } = "";
}

public static class QuoteService
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

    private static readonly string CacheFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LivelyScreen",
        "quotes_cache.json"
    );

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(6) };
    private static readonly object _syncLock = new();
    private static List<QuoteItem> _cachedQuotes = new();
    private static string _lastLoadedPath = "";
    private static QuoteSourceType _lastSourceType = QuoteSourceType.SqliteDatabase;
    private static DateTime _lastFileWriteTime = DateTime.MinValue;
    private static int _currentIndex = -1;
    private static readonly Random _rng = new();

    public static int LoadedCount
    {
        get { lock (_syncLock) return _cachedQuotes.Count; }
    }

    public static bool UsingFallbackCache { get; private set; }
    public static string LastStatusMessage { get; private set; } = "Not loaded";
    public static string ResolvedDatabasePath { get; private set; } = "";

    public static string ResolveDatabasePath(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return string.Empty;

        string raw = configuredPath.Trim().Trim('"');

        try
        {
            if (File.Exists(raw))
                return raw;

            if (Directory.Exists(raw))
            {
                string candidate1 = Path.Combine(raw, "data", "brain.db");
                if (File.Exists(candidate1)) return candidate1;

                string candidate2 = Path.Combine(raw, "brain.db");
                if (File.Exists(candidate2)) return candidate2;

                var anyDb = Directory.GetFiles(raw, "*.db", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrEmpty(anyDb)) return anyDb;
            }
        }
        catch { }

        return raw;
    }

    public static List<QuoteItem> EnsureQuotesLoaded(string? configuredPath, bool forceReload = false)
    {
        return EnsureQuotesLoaded(configuredPath, QuoteSourceType.SqliteDatabase, forceReload);
    }

    public static List<QuoteItem> EnsureQuotesLoaded(string? configuredPath, QuoteSourceType sourceType, bool forceReload = false)
    {
        // Auto-detect source if passed a URL or json path
        if (sourceType == QuoteSourceType.SqliteDatabase && !string.IsNullOrWhiteSpace(configuredPath))
        {
            if (configuredPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                configuredPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                sourceType = QuoteSourceType.OnlineUrl;
            }
            else if (configuredPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                sourceType = QuoteSourceType.LocalJson;
            }
        }

        lock (_syncLock)
        {
            if (sourceType == QuoteSourceType.OnlineUrl)
            {
                return LoadFromOnlineUrl(configuredPath, forceReload);
            }
            else if (sourceType == QuoteSourceType.LocalJson)
            {
                return LoadFromLocalJson(configuredPath, forceReload);
            }
            else
            {
                return LoadFromSqlite(configuredPath, forceReload);
            }
        }
    }

    private static List<QuoteItem> LoadFromOnlineUrl(string? url, bool forceReload)
    {
        string rawUrl = url?.Trim() ?? "";
        ResolvedDatabasePath = rawUrl;

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            var fallback = LoadBackupCache();
            if (fallback.Count > 0)
            {
                _cachedQuotes = fallback;
                UsingFallbackCache = true;
                LastStatusMessage = $"No URL set • Using {fallback.Count} cached quotes";
                return _cachedQuotes;
            }
            _cachedQuotes = GetDefaultFallbackQuotes();
            UsingFallbackCache = true;
            LastStatusMessage = "No URL configured • Using default quotes";
            return _cachedQuotes;
        }

        if (!forceReload && _cachedQuotes.Count > 0 && !UsingFallbackCache &&
            _lastSourceType == QuoteSourceType.OnlineUrl &&
            string.Equals(_lastLoadedPath, rawUrl, StringComparison.OrdinalIgnoreCase))
        {
            return _cachedQuotes;
        }

        try
        {
            App.Log($"Fetching quotes from online URL: {rawUrl}");
            using var req = new HttpRequestMessage(HttpMethod.Get, rawUrl);
            req.Headers.Add("User-Agent", "LivelyScreen/1.0");
            using var resp = _httpClient.Send(req);
            resp.EnsureSuccessStatusCode();

            using var stream = resp.Content.ReadAsStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string json = reader.ReadToEnd();

            var quotes = ParseFlexibleJson(json);
            if (quotes.Count > 0)
            {
                _cachedQuotes = quotes;
                _lastLoadedPath = rawUrl;
                _lastSourceType = QuoteSourceType.OnlineUrl;
                UsingFallbackCache = false;
                LastStatusMessage = $"Online Synced • {quotes.Count} quotes loaded";
                SaveBackupCache(quotes);
                return _cachedQuotes;
            }
        }
        catch (Exception ex)
        {
            App.Log($"Error fetching online quotes: {ex.Message}");
        }

        var backup = LoadBackupCache();
        if (backup.Count > 0)
        {
            _cachedQuotes = backup;
            UsingFallbackCache = true;
            LastStatusMessage = $"Source offline • Using {backup.Count} cached quotes";
            return _cachedQuotes;
        }

        _cachedQuotes = GetDefaultFallbackQuotes();
        UsingFallbackCache = true;
        LastStatusMessage = "Online source unreachable • Using default quotes";
        return _cachedQuotes;
    }

    private static List<QuoteItem> LoadFromLocalJson(string? filePath, bool forceReload)
    {
        string raw = filePath?.Trim().Trim('"') ?? "";
        ResolvedDatabasePath = raw;

        if (string.IsNullOrWhiteSpace(raw) || !File.Exists(raw))
        {
            var backup = LoadBackupCache();
            if (backup.Count > 0)
            {
                _cachedQuotes = backup;
                UsingFallbackCache = true;
                LastStatusMessage = $"File not found • Using {backup.Count} cached quotes";
                return _cachedQuotes;
            }
            _cachedQuotes = GetDefaultFallbackQuotes();
            UsingFallbackCache = true;
            LastStatusMessage = "JSON file not found • Using default quotes";
            return _cachedQuotes;
        }

        try
        {
            DateTime writeTime = File.GetLastWriteTimeUtc(raw);
            if (!forceReload && _cachedQuotes.Count > 0 && !UsingFallbackCache &&
                _lastSourceType == QuoteSourceType.LocalJson &&
                string.Equals(_lastLoadedPath, raw, StringComparison.OrdinalIgnoreCase) &&
                writeTime == _lastFileWriteTime)
            {
                return _cachedQuotes;
            }

            string json = File.ReadAllText(raw, Encoding.UTF8);
            var quotes = ParseFlexibleJson(json);
            if (quotes.Count > 0)
            {
                _cachedQuotes = quotes;
                _lastLoadedPath = raw;
                _lastSourceType = QuoteSourceType.LocalJson;
                _lastFileWriteTime = writeTime;
                UsingFallbackCache = false;
                LastStatusMessage = $"Local JSON • {quotes.Count} quotes loaded";
                SaveBackupCache(quotes);
                return _cachedQuotes;
            }
        }
        catch (Exception ex)
        {
            App.Log($"Error loading local quote JSON: {ex.Message}");
        }

        var backupCache = LoadBackupCache();
        if (backupCache.Count > 0)
        {
            _cachedQuotes = backupCache;
            UsingFallbackCache = true;
            LastStatusMessage = $"File invalid • Using {backupCache.Count} cached quotes";
            return _cachedQuotes;
        }

        _cachedQuotes = GetDefaultFallbackQuotes();
        UsingFallbackCache = true;
        LastStatusMessage = "Invalid JSON format • Using default quotes";
        return _cachedQuotes;
    }

    private static List<QuoteItem> LoadFromSqlite(string? configuredPath, bool forceReload)
    {
        string resolved = ResolveDatabasePath(configuredPath);
        ResolvedDatabasePath = resolved;

        try
        {
            if (File.Exists(resolved))
            {
                DateTime writeTime = File.GetLastWriteTimeUtc(resolved);
                if (!forceReload && _cachedQuotes.Count > 0 && !UsingFallbackCache &&
                    _lastSourceType == QuoteSourceType.SqliteDatabase &&
                    string.Equals(_lastLoadedPath, resolved, StringComparison.OrdinalIgnoreCase) &&
                    writeTime == _lastFileWriteTime)
                {
                    return _cachedQuotes;
                }

                var fromDb = QuerySqliteDatabase(resolved);
                if (fromDb.Count > 0)
                {
                    _cachedQuotes = fromDb;
                    _lastLoadedPath = resolved;
                    _lastSourceType = QuoteSourceType.SqliteDatabase;
                    _lastFileWriteTime = writeTime;
                    UsingFallbackCache = false;
                    int quoteCount = fromDb.Count(q => q.Type.Equals("quote", StringComparison.OrdinalIgnoreCase));
                    int sayingCount = fromDb.Count - quoteCount;
                    LastStatusMessage = $"Connected • {fromDb.Count} items ({quoteCount} quotes, {sayingCount} sayings)";
                    SaveBackupCache(fromDb);
                    return _cachedQuotes;
                }
            }
        }
        catch { }

        var backup = LoadBackupCache();
        if (backup.Count > 0)
        {
            _cachedQuotes = backup;
            UsingFallbackCache = true;
            LastStatusMessage = $"Source offline • Using {backup.Count} cached quotes";
            return _cachedQuotes;
        }

        _cachedQuotes = GetDefaultFallbackQuotes();
        UsingFallbackCache = true;
        LastStatusMessage = "Database not found • Using default quotes";
        return _cachedQuotes;
    }

    public static QuoteItem GetNextQuote(string? configuredPath, bool randomOrder = true)
    {
        return GetNextQuote(configuredPath, QuoteSourceType.SqliteDatabase, randomOrder);
    }

    public static QuoteItem GetNextQuote(string? configuredPath, QuoteSourceType sourceType, bool randomOrder = true)
    {
        var list = EnsureQuotesLoaded(configuredPath, sourceType);
        lock (_syncLock)
        {
            if (list.Count == 0)
            {
                return new QuoteItem
                {
                    Type = "Saying",
                    QuoteText = "No matter how good you are, the wrong room will never see your value",
                    MeaningHeading = "Origin & Cultural Meaning",
                    MeaningText = "If you're in a place that doesn't appreciate you, your effort won't be recognized, no matter how skilled you are."
                };
            }

            if (list.Count == 1)
            {
                _currentIndex = 0;
                return list[0];
            }

            if (randomOrder)
            {
                int next = _rng.Next(list.Count);
                if (next == _currentIndex)
                    next = (next + 1) % list.Count;
                _currentIndex = next;
            }
            else
            {
                _currentIndex = (_currentIndex + 1) % list.Count;
            }

            return list[_currentIndex];
        }
    }

    public static List<QuoteItem> ParseFlexibleJson(string jsonText)
    {
        var results = new List<QuoteItem>();
        if (string.IsNullOrWhiteSpace(jsonText))
            return results;

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            JsonElement root = doc.RootElement;
            JsonElement itemsArray;

            if (root.ValueKind == JsonValueKind.Array)
            {
                itemsArray = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("quotes", out var q) && q.ValueKind == JsonValueKind.Array)
                    itemsArray = q;
                else if (root.TryGetProperty("entries", out var e) && e.ValueKind == JsonValueKind.Array)
                    itemsArray = e;
                else if (root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array)
                    itemsArray = d;
                else if (root.TryGetProperty("items", out var it) && it.ValueKind == JsonValueKind.Array)
                    itemsArray = it;
                else if (root.TryGetProperty("sayings", out var s) && s.ValueKind == JsonValueKind.Array)
                    itemsArray = s;
                else
                    return results;
            }
            else
            {
                return results;
            }

            int idCounter = 1;
            foreach (var elem in itemsArray.EnumerateArray())
            {
                if (elem.ValueKind != JsonValueKind.Object)
                    continue;

                string quoteText = GetElementPropertyString(elem, "quoteText", "quotetext", "quote", "content", "text", "body", "saying", "title");
                if (string.IsNullOrWhiteSpace(quoteText))
                    continue;

                string author = GetElementPropertyString(elem, "author", "authorLine", "speaker", "by");
                string source = GetElementPropertyString(elem, "source", "book", "work", "origin");
                string type = GetElementPropertyString(elem, "type", "category", "genre", "tag");
                if (string.IsNullOrWhiteSpace(type))
                    type = "Quote";

                string meaningHeading = GetElementPropertyString(elem, "meaningHeading", "meaningheading");
                string meaningText = GetElementPropertyString(elem, "meaningText", "meaningtext", "meaning", "description", "context", "notes", "explanation");

                string cleanQuote = quoteText.Replace("\uFFFD", "").Trim().Trim('"', '\'', '\u201C', '\u201D').Trim();
                string authorLine = FormatAuthorAndSource(author, source);
                if (string.IsNullOrWhiteSpace(meaningHeading) && !string.IsNullOrWhiteSpace(meaningText))
                {
                    meaningHeading = type.Equals("Saying", StringComparison.OrdinalIgnoreCase) ? "Origin & Cultural Meaning" : "Context & Meaning";
                }

                string cleanSubtext = !string.IsNullOrEmpty(authorLine)
                    ? authorLine
                    : (!string.IsNullOrEmpty(meaningText) ? meaningText : "");

                results.Add(new QuoteItem
                {
                    Id = idCounter++,
                    Type = FormatTypeBadge(type),
                    QuoteText = cleanQuote,
                    AuthorLine = authorLine,
                    MeaningHeading = meaningHeading,
                    MeaningText = meaningText,
                    Subtext = cleanSubtext,
                    Author = author,
                    Source = source
                });
            }
        }
        catch { }

        return results;
    }

    private static string GetElementPropertyString(JsonElement elem, params string[] propNames)
    {
        foreach (var name in propNames)
        {
            if (elem.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                {
                    var val = prop.GetString();
                    if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
                }
                else if (prop.ValueKind == JsonValueKind.Array)
                {
                    var parts = new List<string>();
                    foreach (var item in prop.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var s = item.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) parts.Add(s);
                        }
                    }
                    if (parts.Count > 0) return string.Join(", ", parts);
                }
            }
        }
        return "";
    }

    private static List<QuoteItem> QuerySqliteDatabase(string dbFilePath)
    {
        var results = new List<QuoteItem>();
        IntPtr db = IntPtr.Zero;
        IntPtr stmt = IntPtr.Zero;

        try
        {
            byte[] pathBytes = ToNullTerminatedUtf8(dbFilePath);
            int rc = sqlite3_open_v2(pathBytes, out db, SQLITE_OPEN_READONLY | SQLITE_OPEN_NOMUTEX, IntPtr.Zero);
            if (rc != SQLITE_OK || db == IntPtr.Zero)
                return results;

            const string sql = @"
                SELECT id, type, title, content, author, source, tags
                FROM entries
                WHERE LOWER(TRIM(type)) IN ('quote', 'saying')
                  AND IFNULL(encrypted, 0) = 0
                ORDER BY id ASC;";

            byte[] sqlBytes = ToNullTerminatedUtf8(sql);
            rc = sqlite3_prepare_v2(db, sqlBytes, sqlBytes.Length, out stmt, IntPtr.Zero);
            if (rc != SQLITE_OK || stmt == IntPtr.Zero)
                return results;

            while (sqlite3_step(stmt) == SQLITE_ROW)
            {
                int id = sqlite3_column_int(stmt, 0);
                string type = ReadColumnString(stmt, 1);
                string title = ReadColumnString(stmt, 2);
                string content = ReadColumnString(stmt, 3);
                string author = ReadColumnString(stmt, 4);
                string source = ReadColumnString(stmt, 5);
                string tags = ReadColumnString(stmt, 6);

                string cleanQuote = ExtractCleanQuoteText(title, content);
                if (string.IsNullOrWhiteSpace(cleanQuote))
                    continue;

                string authorLine = FormatAuthorAndSource(author, source);
                var (meaningHeading, meaningText) = ExtractMeaningSection(content);
                string cleanSubtext = !string.IsNullOrEmpty(authorLine)
                    ? authorLine
                    : (!string.IsNullOrEmpty(meaningText) ? meaningText : FormatFallbackTags(tags));

                results.Add(new QuoteItem
                {
                    Id = id,
                    Type = FormatTypeBadge(type),
                    QuoteText = cleanQuote,
                    AuthorLine = authorLine,
                    MeaningHeading = meaningHeading,
                    MeaningText = meaningText,
                    Subtext = cleanSubtext,
                    Author = author?.Trim() ?? "",
                    Source = source?.Trim() ?? ""
                });
            }
        }
        finally
        {
            if (stmt != IntPtr.Zero)
                sqlite3_finalize(stmt);
            if (db != IntPtr.Zero)
                sqlite3_close_v2(db);
        }

        return results;
    }

    private static string FormatTypeBadge(string rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType))
            return "Quote";
        string t = rawType.Trim();
        return char.ToUpperInvariant(t[0]) + t.Substring(1).ToLowerInvariant();
    }

    private static string ExtractCleanQuoteText(string title, string content)
    {
        string candidate = !string.IsNullOrWhiteSpace(title) ? title.Trim() : "";
        if (string.IsNullOrWhiteSpace(candidate) && !string.IsNullOrWhiteSpace(content))
        {
            var firstLine = content
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(l => !l.TrimStart().StartsWith("#")) ?? "";
            candidate = firstLine.Trim();
        }

        candidate = candidate.Replace("\uFFFD", "").Trim().Trim('"', '\'', '\u201C', '\u201D').Trim();
        return candidate;
    }

    private static string FormatAuthorAndSource(string? author, string? source)
    {
        string a = (author ?? "").Replace("\uFFFD", "").Trim();
        string s = (source ?? "").Replace("\uFFFD", "").Trim();

        if (!string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(s))
            return $"— {a}  •  {s}";
        if (!string.IsNullOrEmpty(a))
            return $"— {a}";
        if (!string.IsNullOrEmpty(s))
            return $"— {s}";
        return "";
    }

    private static (string heading, string body) ExtractMeaningSection(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ("", "");

        var lines = content
            .Replace("\uFFFD", "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("#"))
            {
                string heading = lines[i].TrimStart('#', ' ').Trim();
                var bodyLines = new List<string>();
                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (lines[j].StartsWith("#"))
                        break;
                    string cleanLine = lines[j].TrimStart('-', '*', '•', ' ').Trim();
                    cleanLine = cleanLine.Replace("**", "");
                    if (!string.IsNullOrWhiteSpace(cleanLine))
                    {
                        bodyLines.Add(cleanLine);
                    }
                }

                if (bodyLines.Count > 0)
                {
                    string combined = string.Join(" ", bodyLines);
                    return (string.IsNullOrWhiteSpace(heading) ? "Context & Meaning" : heading, combined);
                }
            }
        }

        return ("", "");
    }

    private static string FormatFallbackTags(string? tags)
    {
        if (!string.IsNullOrWhiteSpace(tags))
        {
            var tagParts = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => !t.Equals("Saying", StringComparison.OrdinalIgnoreCase)
                         && !t.Equals("personalNotes", StringComparison.OrdinalIgnoreCase)
                         && !t.Equals("remindertoSELF", StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();

            if (tagParts.Length > 0)
                return string.Join(" • ", tagParts);
        }

        return "";
    }

    private static byte[] ToNullTerminatedUtf8(string text)
    {
        int byteCount = Encoding.UTF8.GetByteCount(text);
        byte[] bytes = new byte[byteCount + 1];
        Encoding.UTF8.GetBytes(text, 0, text.Length, bytes, 0);
        bytes[byteCount] = 0;
        return bytes;
    }

    private static string ReadColumnString(IntPtr stmt, int col)
    {
        IntPtr ptr = sqlite3_column_text(stmt, col);
        if (ptr == IntPtr.Zero)
            return "";
        int len = sqlite3_column_bytes(stmt, col);
        if (len <= 0)
            return "";
        return Marshal.PtrToStringUTF8(ptr, len) ?? "";
    }

    private static void SaveBackupCache(List<QuoteItem> items)
    {
        try
        {
            string? dir = Path.GetDirectoryName(CacheFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(items);
            File.WriteAllText(CacheFilePath, json, Encoding.UTF8);
        }
        catch { }
    }

    private static List<QuoteItem> LoadBackupCache()
    {
        try
        {
            if (File.Exists(CacheFilePath))
            {
                string json = File.ReadAllText(CacheFilePath, Encoding.UTF8);
                var items = JsonSerializer.Deserialize<List<QuoteItem>>(json);
                if (items != null && items.Count > 0 && items.Any(i => !string.IsNullOrEmpty(i.MeaningText) || !string.IsNullOrEmpty(i.AuthorLine)))
                    return items;
            }
        }
        catch { }
        return new List<QuoteItem>();
    }

    private static List<QuoteItem> GetDefaultFallbackQuotes() =>
    [
        new QuoteItem
        {
            Id = 1,
            Type = "Saying",
            QuoteText = "No matter how good you are, the wrong room will never see your value",
            AuthorLine = "",
            MeaningHeading = "Origin & Cultural Meaning",
            MeaningText = "If you're in a place that doesn't appreciate you, your effort won't be recognized, no matter how skilled you are. It's okay to seek spaces that value you."
        },
        new QuoteItem
        {
            Id = 2,
            Type = "Quote",
            QuoteText = "We suffer more often in imagination than in reality",
            AuthorLine = "— Seneca  •  Letters from a Stoic",
            MeaningHeading = "Context & Meaning",
            MeaningText = "Anticipated fears and anxiety cause far more distress than the actual events when they arrive."
        },
        new QuoteItem
        {
            Id = 3,
            Type = "Quote",
            QuoteText = "The soul becomes dyed with the color of its thoughts",
            AuthorLine = "— Marcus Aurelius  •  Meditations",
            MeaningHeading = "Context & Meaning",
            MeaningText = "What you dwell upon shapes your perception, character, and mental clarity over time."
        },
        new QuoteItem
        {
            Id = 4,
            Type = "Saying",
            QuoteText = "A fish with its mouth shut avoids the hook",
            AuthorLine = "",
            MeaningHeading = "Origin & Cultural Meaning",
            MeaningText = "Silence and thoughtful discretion protect against unnecessary conflict and regret."
        },
        new QuoteItem
        {
            Id = 5,
            Type = "Quote",
            QuoteText = "The wound is the place where the Light enters you",
            AuthorLine = "— Rumi",
            MeaningHeading = "Context & Meaning",
            MeaningText = "Pain and adversity break open illusions, creating space for deeper insight and compassion."
        },
        new QuoteItem
        {
            Id = 6,
            Type = "Quote",
            QuoteText = "He who conquers others is strong; he who conquers himself is mighty",
            AuthorLine = "— Lao Tzu  •  Tao Te Ching",
            MeaningHeading = "Context & Meaning",
            MeaningText = "Self-mastery and emotional discipline are far greater feats than dominance over others."
        },
        new QuoteItem
        {
            Id = 7,
            Type = "Saying",
            QuoteText = "Fall seven times, stand up eight",
            AuthorLine = "— Japanese Proverb",
            MeaningHeading = "Origin & Cultural Meaning",
            MeaningText = "Resilience is not the absence of stumbling, but the decision to rise each time."
        },
        new QuoteItem
        {
            Id = 8,
            Type = "Saying",
            QuoteText = "Smooth seas do not make skillful sailors",
            AuthorLine = "— African Proverb",
            MeaningHeading = "Origin & Cultural Meaning",
            MeaningText = "Comfort rarely builds competence; rough waters and trials forge true ability."
        },
        new QuoteItem
        {
            Id = 9,
            Type = "Quote",
            QuoteText = "It is not that we have a short time to live, but that we waste a lot of it",
            AuthorLine = "— Seneca  •  On the Shortness of Life",
            MeaningHeading = "Context & Meaning",
            MeaningText = "Life is long enough when invested intentionally rather than scattered on trivialities."
        },
        new QuoteItem
        {
            Id = 10,
            Type = "Saying",
            QuoteText = "If you want to go fast, go alone; if you want to go far, go together",
            AuthorLine = "— African Proverb",
            MeaningHeading = "Origin & Cultural Meaning",
            MeaningText = "Individual effort brings quick progress, but enduring achievements rely on community."
        },
        new QuoteItem
        {
            Id = 11,
            Type = "Quote",
            QuoteText = "Waste no more time arguing about what a good man should be. Be one.",
            AuthorLine = "— Marcus Aurelius  •  Meditations",
            MeaningHeading = "Context & Meaning",
            MeaningText = "Philosophy without action is useless debate; embody virtue through daily deeds."
        },
        new QuoteItem
        {
            Id = 12,
            Type = "Saying",
            QuoteText = "A tree that bends does not break in the storm",
            AuthorLine = "",
            MeaningHeading = "Origin & Cultural Meaning",
            MeaningText = "Adaptability and flexibility outlast rigid resistance under pressure."
        },
        new QuoteItem
        {
            Id = 13,
            Type = "Saying",
            QuoteText = "Quiet waters run deep, loud rivers are shallow",
            AuthorLine = "",
            MeaningHeading = "Origin & Cultural Meaning",
            MeaningText = "Substance and genuine strength carry quiet confidence without need for loud spectacle."
        },
        new QuoteItem
        {
            Id = 14,
            Type = "Quote",
            QuoteText = "Knowing yourself is the beginning of all wisdom",
            AuthorLine = "— Aristotle",
            MeaningHeading = "Context & Meaning",
            MeaningText = "All outer judgment and discernment begin with honest awareness of your own tendencies."
        },
        new QuoteItem
        {
            Id = 15,
            Type = "Quote",
            QuoteText = "A journey of a thousand miles begins with a single step",
            AuthorLine = "— Lao Tzu  •  Tao Te Ching",
            MeaningHeading = "Context & Meaning",
            MeaningText = "Monumental undertakings are conquered only by committing to the first modest action."
        },
        new QuoteItem
        {
            Id = 16,
            Type = "Saying",
            QuoteText = "Silence is an answer too, and often the loudest one",
            AuthorLine = "",
            MeaningHeading = "Origin & Cultural Meaning",
            MeaningText = "Refusing to engage or validate noise communicates clear boundaries."
        },
        new QuoteItem
        {
            Id = 17,
            Type = "Saying",
            QuoteText = "Do not be afraid of growing slowly, be afraid only of standing still",
            AuthorLine = "— Chinese Proverb",
            MeaningHeading = "Origin & Cultural Meaning",
            MeaningText = "Incremental, steady development compounds into extraordinary results over time."
        },
        new QuoteItem
        {
            Id = 18,
            Type = "Quote",
            QuoteText = "You have power over your mind, not outside events. Realize this, and you will find strength.",
            AuthorLine = "— Marcus Aurelius  •  Meditations",
            MeaningHeading = "Context & Meaning",
            MeaningText = "External conditions are outside your control; your judgment and response are entirely yours."
        },
        new QuoteItem
        {
            Id = 19,
            Type = "Saying",
            QuoteText = "The tongue has no bones, but it can break hearts",
            AuthorLine = "",
            MeaningHeading = "Origin & Cultural Meaning",
            MeaningText = "Careless speech inflicts deep and lasting damage; weigh your words before speaking."
        },
        new QuoteItem
        {
            Id = 20,
            Type = "Quote",
            QuoteText = "In the middle of difficulty lies opportunity",
            AuthorLine = "— Albert Einstein",
            MeaningHeading = "Context & Meaning",
            MeaningText = "Obstacles force creative pivots and reveal answers invisible during easy times."
        },
        new QuoteItem
        {
            Id = 21,
            Type = "Quote",
            QuoteText = "Do not look back in anger, or forward in fear, but around in awareness",
            AuthorLine = "— James Thurber",
            MeaningHeading = "Context & Meaning",
            MeaningText = "Dwelling in past grievance or future worry blinds you to what is present right now."
        },
        new QuoteItem
        {
            Id = 22,
            Type = "Quote",
            QuoteText = "A river cuts through rock not because of its power, but because of its persistence",
            AuthorLine = "— Jim Watkins",
            MeaningHeading = "Context & Meaning",
            MeaningText = "Consistent, focused repetition overcomes barriers that sudden bursts of force cannot."
        },
        new QuoteItem
        {
            Id = 23,
            Type = "Saying",
            QuoteText = "A bird does not sing because it has an answer, it sings because it has a song",
            AuthorLine = "",
            MeaningHeading = "Origin & Cultural Meaning",
            MeaningText = "True expression stems from inherent nature and purpose, not seeking external approval."
        },
        new QuoteItem
        {
            Id = 24,
            Type = "Saying",
            QuoteText = "Beware the calm waters, for still depths conceal the strongest currents",
            AuthorLine = "",
            MeaningHeading = "Origin & Cultural Meaning",
            MeaningText = "External stillness can mask immense hidden power and rapid movement underneath."
        },
        new QuoteItem
        {
            Id = 25,
            Type = "Quote",
            QuoteText = "Rule number five, show no love, love will get you killed",
            AuthorLine = "— 50 Cent  •  Get Rich or Die Tryin",
            MeaningHeading = "Context & Meaning",
            MeaningText = "Street mentality of emotional restraint, distrust, and self-preservation."
        },
        new QuoteItem
        {
            Id = 26,
            Type = "Saying",
            QuoteText = "The best time to plant a tree was 20 years ago. The second best time is now.",
            AuthorLine = "",
            MeaningHeading = "Origin & Cultural Meaning",
            MeaningText = "Do not waste energy lamenting lost time; begin building what matters today."
        }
    ];
}
