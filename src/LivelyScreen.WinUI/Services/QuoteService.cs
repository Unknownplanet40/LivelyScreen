using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

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

    private static readonly object _syncLock = new();
    private static List<QuoteItem> _cachedQuotes = new();
    private static string _lastLoadedPath = "";
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
        string resolved = ResolveDatabasePath(configuredPath);
        ResolvedDatabasePath = resolved;

        lock (_syncLock)
        {
            try
            {
                if (File.Exists(resolved))
                {
                    DateTime writeTime = File.GetLastWriteTimeUtc(resolved);
                    if (!forceReload && _cachedQuotes.Count > 0 && !UsingFallbackCache &&
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

            // Fallback to local persisted JSON cache if DB moved or OneDrive is syncing
            var backup = LoadBackupCache();
            if (backup.Count > 0)
            {
                _cachedQuotes = backup;
                UsingFallbackCache = true;
                LastStatusMessage = $"Source offline • Using {backup.Count} cached quotes";
                return _cachedQuotes;
            }

            // Ultimate built-in fallback
            _cachedQuotes = GetDefaultFallbackQuotes();
            UsingFallbackCache = true;
            LastStatusMessage = "Database not found • Using default quotes";
            return _cachedQuotes;
        }
    }

    public static QuoteItem GetNextQuote(string? configuredPath, bool randomOrder = true)
    {
        var list = EnsureQuotesLoaded(configuredPath);
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

        candidate = candidate.Replace("\uFFFD", "").Trim().Trim('"', '“', '”', '\'').Trim();
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
            File.WriteAllText(CacheFilePath, json);
        }
        catch { }
    }

    private static List<QuoteItem> LoadBackupCache()
    {
        try
        {
            if (File.Exists(CacheFilePath))
            {
                string json = File.ReadAllText(CacheFilePath);
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
            MeaningText = "If you're in a place that doesn't appreciate you, your effort won't be recognized, no matter how skilled you are. It's okay to look for people and places that value you."
        },
        new QuoteItem
        {
            Id = 2,
            Type = "Quote",
            QuoteText = "Rule number five, show no love, love will get you killed",
            AuthorLine = "— 50 Cent  •  Get Rich or Die Tryin",
            MeaningHeading = "Context & Meaning",
            MeaningText = "Street mentality of emotional restraint, distrust, and self-preservation."
        },
        new QuoteItem
        {
            Id = 3,
            Type = "Saying",
            QuoteText = "A fish with its mouth shut avoids the hook",
            AuthorLine = "",
            MeaningHeading = "Origin & Cultural Meaning",
            MeaningText = "Silence and careful speech protect you from unnecessary trouble."
        }
    ];
}
