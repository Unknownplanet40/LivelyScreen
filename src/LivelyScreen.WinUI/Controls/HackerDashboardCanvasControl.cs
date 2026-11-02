using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using LivelyScreen.Models;
using LivelyScreen.Services.Dashboard;
using LivelyScreen.ViewModels;

namespace LivelyScreen_WinUI.Controls;

public sealed class HackerDashboardCanvasControl : UserControl
{
    // Exact palette from spec
    private static readonly Color ColorBg = Color.FromArgb(255, 0x08, 0x0C, 0x0A);       // #080C0A
    private static readonly Color ColorText = Color.FromArgb(255, 0xA8, 0xE6, 0xB8);     // #A8E6B8
    private static readonly Color ColorLabel = Color.FromArgb(255, 0x4E, 0x7D, 0x5C);    // #4E7D5C
    private static readonly Color ColorAccent = Color.FromArgb(255, 0x3D, 0xDC, 0x84);   // #3DDC84
    private static readonly Color ColorWarning = Color.FromArgb(255, 0xE3, 0xB3, 0x41);  // #E3B341
    private static readonly Color ColorCritical = Color.FromArgb(255, 0xE5, 0x53, 0x4B); // #E5534B
    private static readonly Color ColorRule = Color.FromArgb(255, 0x16, 0x29, 0x1E);     // 1px divider
    private static readonly Color ColorGridLine = Color.FromArgb(255, 0x11, 0x1F, 0x17); // faint chart gridline
    private static readonly Color ColorBarTrack = Color.FromArgb(255, 0x11, 0x1C, 0x15); // bar background track
    private static readonly Color ColorFillGreen = Color.FromArgb(38, 0x3D, 0xDC, 0x84);
    private static readonly Color ColorFillAmber = Color.FromArgb(38, 0xE3, 0xB3, 0x41);

    private readonly CanvasControl _canvas;
    private readonly HackerDashboardViewModel _viewModel;
    private readonly float[] _graphScratch = new float[HackerDashboardViewModel.GraphSampleCapacity];

    private CanvasTextFormat? _fmtRegular;
    private CanvasTextFormat? _fmtBold;
    private float _cachedFontSize;

    private readonly struct CachedLayoutKey : IEquatable<CachedLayoutKey>
    {
        public readonly int SlotId;
        public readonly bool Bold;
        public CachedLayoutKey(int slotId, bool bold) { SlotId = slotId; Bold = bold; }
        public bool Equals(CachedLayoutKey other) => SlotId == other.SlotId && Bold == other.Bold;
        public override bool Equals(object? obj) => obj is CachedLayoutKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SlotId, Bold);
    }

    private sealed class CachedLayoutEntry : IDisposable
    {
        public required string Text { get; set; }
        public required CanvasTextLayout Layout { get; set; }
        public float Width => (float)Layout.LayoutBounds.Width;
        public void Dispose() => Layout.Dispose();
    }

    private readonly Dictionary<CachedLayoutKey, CachedLayoutEntry> _layoutCache = new(256);

    private bool _isRunning;
    private bool _needsRedraw = true;
    private long _lastRenderTicks;

    public HackerDashboardViewModel ViewModel => _viewModel;

    public HackerDashboardCanvasControl()
    {
        _viewModel = new HackerDashboardViewModel(DispatcherQueue);
        _viewModel.StateUpdated += OnViewModelStateUpdated;

        _canvas = new CanvasControl
        {
            ClearColor = ColorBg,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _canvas.Draw += OnCanvasDraw;
        _canvas.SizeChanged += (_, _) =>
        {
            InvalidateTextLayouts();
            RequestRedraw();
        };

        Content = _canvas;
        Unloaded += (_, _) => Stop();
    }

    public void Start(AppSettings settings)
    {
        _viewModel.MaskSensitive = settings.MASK_SENSITIVE;
        if (_isRunning)
        {
            RequestRedraw();
            return;
        }

        _isRunning = true;
        _lastRenderTicks = Stopwatch.GetTimestamp();

        DashboardSampler.Shared.SnapshotPublished -= _viewModel.OnSnapshotReceived;
        DashboardSampler.Shared.SnapshotPublished += _viewModel.OnSnapshotReceived;
        DashboardSampler.Shared.AddRef();

        var existing = DashboardSampler.Shared.LatestSnapshot;
        if (existing != null)
        {
            _viewModel.ApplySnapshotOnUiThread(existing);
        }

        CompositionTarget.Rendering -= OnCompositionRendering;
        CompositionTarget.Rendering += OnCompositionRendering;
        RequestRedraw();
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;

        CompositionTarget.Rendering -= OnCompositionRendering;
        DashboardSampler.Shared.SnapshotPublished -= _viewModel.OnSnapshotReceived;
        DashboardSampler.Shared.ReleaseRef();
        InvalidateTextLayouts();
    }

    public void RecomputeLayout()
    {
        InvalidateTextLayouts();
        RequestRedraw();
    }

    private void OnViewModelStateUpdated()
    {
        RequestRedraw();
    }

    private void RequestRedraw()
    {
        _needsRedraw = true;
        _canvas.Invalidate();
    }

    private void OnCompositionRendering(object? sender, object e)
    {
        if (!_isRunning) return;

        long nowTicks = Stopwatch.GetTimestamp();
        double dt = _lastRenderTicks > 0
            ? (nowTicks - _lastRenderTicks) / (double)Stopwatch.Frequency
            : 0.016;
        _lastRenderTicks = nowTicks;

        bool animating = _viewModel.AdvanceAnimations(dt);
        if (animating || _needsRedraw)
        {
            _needsRedraw = false;
            _canvas.Invalidate();
        }
    }

    private void EnsureFonts(float screenHeight)
    {
        // Spec: One font size everywhere (about 1.4% of screen height), Cascadia Mono falling back to Consolas
        float targetSize = MathF.Max(10.0f, MathF.Round(screenHeight * 0.0136f, 1));
        if (_fmtRegular != null && _fmtBold != null && MathF.Abs(_cachedFontSize - targetSize) < 0.05f)
            return;

        _cachedFontSize = targetSize;
        InvalidateTextLayouts();

        _fmtRegular?.Dispose();
        _fmtBold?.Dispose();

        _fmtRegular = new CanvasTextFormat
        {
            FontFamily = "Cascadia Mono, Consolas",
            FontSize = targetSize,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            WordWrapping = CanvasWordWrapping.NoWrap
        };

        _fmtBold = new CanvasTextFormat
        {
            FontFamily = "Cascadia Mono, Consolas",
            FontSize = targetSize,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            WordWrapping = CanvasWordWrapping.NoWrap
        };
    }

    private void InvalidateTextLayouts()
    {
        foreach (var kvp in _layoutCache)
        {
            kvp.Value.Dispose();
        }
        _layoutCache.Clear();
    }

    private CachedLayoutEntry GetLayout(ICanvasResourceCreator creator, int slotId, string text, bool bold = false)
    {
        var key = new CachedLayoutKey(slotId, bold);
        if (_layoutCache.TryGetValue(key, out var entry))
        {
            if (string.Equals(entry.Text, text, StringComparison.Ordinal))
                return entry;
            entry.Dispose();
        }

        var fmt = bold ? _fmtBold! : _fmtRegular!;
        var layout = new CanvasTextLayout(creator, text, fmt, 4096f, 256f);
        var created = new CachedLayoutEntry { Text = text, Layout = layout };
        _layoutCache[key] = created;
        return created;
    }

    private void DrawTextLeft(CanvasDrawingSession ds, int slotId, string text, float x, float y, Color color, bool bold = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        var entry = GetLayout(ds, slotId, text, bold);
        ds.DrawTextLayout(entry.Layout, x, y, color);
    }

    private void DrawTextRight(CanvasDrawingSession ds, int slotId, string text, float rightX, float y, Color color, bool bold = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        var entry = GetLayout(ds, slotId, text, bold);
        ds.DrawTextLayout(entry.Layout, rightX - entry.Width, y, color);
    }

    private void OnCanvasDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        float w = (float)sender.ActualWidth;
        float h = (float)sender.ActualHeight;
        if (w < 64f || h < 64f) return;

        var ds = args.DrawingSession;
        ds.Clear(ColorBg);
        ds.Antialiasing = CanvasAntialiasing.Antialiased;

        EnsureFonts(h);
        float fontH = _cachedFontSize;
        float lineStep = fontH * 1.65f;

        var snap = _viewModel.LatestSnapshot;
        if (snap == null)
        {
            DrawTextLeft(ds, 1, "INITIALIZING TELEMETRY...", w * 0.02f, h * 0.02f, ColorAccent, bold: true);
            return;
        }

        bool mask = _viewModel.MaskSensitive;
        float padX = w * 0.014f;
        float padY = h * 0.016f;
        float contentW = w - (padX * 2f);

        // ====================================================================
        // 1. HEADER BAR (Host // OS + Build, Uptime, Clock)
        // ====================================================================
        string hostStr = DashboardFormatters.MaskHostname(snap.SystemInfo.HostName, mask);
        string osHeader = string.IsNullOrEmpty(snap.SystemInfo.OsBuildString)
            ? snap.SystemInfo.OsDisplayName
            : $"{snap.SystemInfo.OsDisplayName} ({snap.SystemInfo.OsBuildString})";
        string headerLeft = $"{hostStr}  //  {osHeader}";
        DrawTextLeft(ds, 10, headerLeft, padX, padY, ColorAccent, bold: true);

        string clockStr = snap.TimestampLocal.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        DrawTextRight(ds, 11, clockStr, w - padX, padY, ColorText, bold: false);

        string uptimeStr = DashboardFormatters.FormatUptime(snap.Uptime);
        float clockReserved = fontH * 6.8f;
        DrawTextRight(ds, 12, uptimeStr, w - padX - clockReserved, padY, ColorText, bold: false);

        float headerDividerY = padY + fontH * 1.72f;
        ds.DrawLine(padX, headerDividerY, w - padX, headerDividerY, ColorRule, 1f);

        // ====================================================================
        // 2. THREE-COLUMN MAIN AREA + BOTTOM EVENT LOG
        // ====================================================================
        float mainTopY = headerDividerY + h * 0.018f;
        float eventSectionTopY = h * 0.785f;
        float mainBottomY = eventSectionTopY - h * 0.015f;

        float colGap = contentW * 0.025f;
        float col1W = contentW * 0.215f;
        float col2W = contentW * 0.305f;
        float col3W = contentW - col1W - col2W - (colGap * 2f);

        float col1X = padX;
        float col2X = col1X + col1W + colGap;
        float col3X = col2X + col2W + colGap;

        DrawColumn1SystemAndStorage(ds, snap, mask, col1X, mainTopY, col1W, mainBottomY, fontH, lineStep);
        DrawColumn2CpuMemoryNetwork(ds, snap, mask, col2X, mainTopY, col2W, mainBottomY, fontH, lineStep);
        DrawColumn3ProcessesAndConnections(ds, snap, mask, col3X, mainTopY, col3W, mainBottomY, fontH, lineStep);

        // ====================================================================
        // 3. BOTTOM FULL-WIDTH EVENT LOG
        // ====================================================================
        ds.DrawLine(padX, eventSectionTopY, w - padX, eventSectionTopY, ColorRule, 1f);
        float evHeaderY = eventSectionTopY + h * 0.012f;
        DrawSectionHeader(ds, 900, "EVENT LOG", 901, "live from system", padX, evHeaderY, contentW, fontH);

        float evRowY = evHeaderY + fontH * 2.0f;
        float evRowStep = fontH * 1.48f;
        int evCount = _viewModel.EventLog.Count;
        int maxVisibleEvents = Math.Max(1, (int)((h - padY - evRowY) / evRowStep));
        int shown = 0;

        // Render newest first matching mockup (14:32:09, 14:32:07, 14:32:04...)
        for (int i = evCount - 1; i >= 0 && shown < maxVisibleEvents; i--, shown++)
        {
            var ev = _viewModel.EventLog[i];
            int baseSlot = 910 + shown * 4;
            string timeTxt = ev.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            string catTxt = ev.Category.ToString();
            string msgTxt = mask ? MaskEventMessageIfNeeded(ev.Message) : ev.Message;

            Color msgColor = ev.Severity switch
            {
                DashboardEventSeverity.Critical => ColorCritical,
                DashboardEventSeverity.Warning => ColorWarning,
                DashboardEventSeverity.Dim => ColorLabel,
                _ => ColorText
            };

            DrawTextLeft(ds, baseSlot, timeTxt, padX, evRowY, ColorLabel);
            DrawTextLeft(ds, baseSlot + 1, catTxt, padX + fontH * 6.2f, evRowY, ColorAccent, bold: true);
            DrawTextLeft(ds, baseSlot + 2, msgTxt, padX + fontH * 10.6f, evRowY, msgColor);

            evRowY += evRowStep;
        }
    }

    private void DrawColumn1SystemAndStorage(
        CanvasDrawingSession ds,
        DashboardSnapshot snap,
        bool mask,
        float x,
        float topY,
        float width,
        float bottomY,
        float fontH,
        float lineStep)
    {
        DrawSectionHeader(ds, 100, "SYSTEM", 0, string.Empty, x, topY, width, fontH);
        float y = topY + fontH * 2.0f;
        float valX = x + fontH * 5.2f;
        float maxValChars = Math.Max(12f, (width - (valX - x)) / (fontH * 0.61f));

        var sys = snap.SystemInfo;
        DrawKeyValueRow(ds, 102, "HOST", DashboardFormatters.MaskHostname(sys.HostName, mask), x, valX, ref y, lineStep, maxValChars);
        DrawKeyValueRow(ds, 104, "USER", DashboardFormatters.MaskUsername(sys.UserName, mask), x, valX, ref y, lineStep, maxValChars);
        DrawKeyValueRow(ds, 106, "CPU", CleanCpuBrand(sys.CpuName), x, valX, ref y, lineStep, maxValChars);

        string coresStr = sys.BaseClockMhz > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}C / {1}T @ {2:F2} GHz", sys.PhysicalCores, sys.LogicalCores, sys.BaseClockMhz / 1000.0)
            : string.Format(CultureInfo.InvariantCulture, "{0}C / {1}T", sys.PhysicalCores, sys.LogicalCores);
        DrawKeyValueRow(ds, 108, "CORES", coresStr, x, valX, ref y, lineStep, maxValChars);

        if (sys.RamTotalBytes > 0)
        {
            string ramGb = DashboardFormatters.FormatGigabytes(sys.RamTotalBytes, 1);
            string ramExtra = $"{sys.RamType} {(sys.RamSpeedMhz > 0 ? sys.RamSpeedMhz.ToString(CultureInfo.InvariantCulture) : string.Empty)}".Trim();
            string ramText = string.IsNullOrEmpty(ramExtra) ? ramGb : $"{ramGb} {ramExtra}";
            DrawKeyValueRow(ds, 110, "RAM", ramText, x, valX, ref y, lineStep, maxValChars);
        }

        if (!string.IsNullOrWhiteSpace(sys.GpuName))
        {
            string gpuShort = CleanGpuBrand(sys.GpuName);
            string vramTag = sys.GpuVramBytes >= (512UL * 1024 * 1024)
                ? $" {Math.Round(sys.GpuVramBytes / (1024.0 * 1024.0 * 1024.0)):F0}GB"
                : string.Empty;
            DrawKeyValueRow(ds, 112, "GPU", gpuShort + vramTag, x, valX, ref y, lineStep, maxValChars);
        }

        DrawKeyValueRow(ds, 114, "DISK", sys.PrimaryDiskSummary, x, valX, ref y, lineStep, maxValChars);
        DrawKeyValueRow(ds, 116, "BOARD", sys.BoardName, x, valX, ref y, lineStep, maxValChars);
        DrawKeyValueRow(ds, 118, "BIOS", sys.BiosVersionAndDate, x, valX, ref y, lineStep, maxValChars);
        DrawKeyValueRow(ds, 120, "DISPLAY", sys.DisplaySummary, x, valX, ref y, lineStep, maxValChars);
        if (!string.IsNullOrWhiteSpace(snap.BatteryStatus))
        {
            DrawKeyValueRow(ds, 122, "BATTERY", snap.BatteryStatus, x, valX, ref y, lineStep, maxValChars);
        }

        // STORAGE SECTION
        y += fontH * 0.9f;
        if (y + fontH * 3.0f >= bottomY) return;

        DrawSectionHeader(ds, 140, "STORAGE", 0, string.Empty, x, y, width, fontH);
        y += fontH * 2.0f;

        var drives = snap.Storage.Drives;
        var displayedDrives = _viewModel.DisplayedDrivePercents;
        int maxDrives = Math.Min(drives.Count, 4);

        for (int i = 0; i < maxDrives; i++)
        {
            var d = drives[i];
            float pct = i < displayedDrives.Length ? displayedDrives[i] : (float)d.UsedPercent;
            int slotBase = 145 + i * 3;

            DrawTextLeft(ds, slotBase, d.Name, x, y, ColorText);

            float barX = x + fontH * 2.5f;
            float rightLabelW = fontH * 6.8f;
            float barW = MathF.Max(24f, width - (barX - x) - rightLabelW);
            float barH = MathF.Max(5f, fontH * 0.56f);
            float barY = y + (fontH - barH) * 0.55f;

            DrawProgressBar(ds, barX, barY, barW, barH, pct);

            string capTxt = DashboardFormatters.FormatDriveUsage(d.UsedBytes, d.TotalBytes);
            DrawTextRight(ds, slotBase + 1, capTxt, x + width, y, ColorLabel);

            y += lineStep * 1.05f;
        }

        y += fontH * 0.25f;
        if (snap.Storage.ReadBytesPerSec.HasValue)
        {
            DrawTextLeft(ds, 170, "READ", x, y, ColorLabel);
            DrawTextRight(ds, 171, DashboardFormatters.FormatRateBytesPerSec(snap.Storage.ReadBytesPerSec.Value), x + fontH * 10.5f, y, ColorText);
            y += lineStep;
        }
        if (snap.Storage.WriteBytesPerSec.HasValue)
        {
            DrawTextLeft(ds, 172, "WRITE", x, y, ColorLabel);
            DrawTextRight(ds, 173, DashboardFormatters.FormatRateBytesPerSec(snap.Storage.WriteBytesPerSec.Value), x + fontH * 10.5f, y, ColorText);
        }
    }

    private void DrawColumn2CpuMemoryNetwork(
        CanvasDrawingSession ds,
        DashboardSnapshot snap,
        bool mask,
        float x,
        float topY,
        float width,
        float bottomY,
        float fontH,
        float lineStep)
    {
        float availH = bottomY - topY;

        // 1. CPU SECTION
        string cpuRight = snap.Cpu.TemperatureCelsius.HasValue
            ? string.Format(CultureInfo.InvariantCulture, "total {0:F0}%   temp {1:F0}C", snap.Cpu.TotalPercent, snap.Cpu.TemperatureCelsius.Value)
            : string.Format(CultureInfo.InvariantCulture, "total {0:F0}%", snap.Cpu.TotalPercent);

        DrawSectionHeader(ds, 200, "CPU", 201, cpuRight, x, topY, width, fontH);

        float cpuChartY = topY + fontH * 1.95f;
        float cpuChartH = availH * 0.115f;
        DrawFilledChart(ds, _viewModel.CpuHistory, x, cpuChartY, width, cpuChartH, 100f, ColorAccent, ColorFillGreen);

        // Per-logical-processor bars (2 columns up to 16 threads, 4 columns for >16 threads)
        float coresTopY = cpuChartY + cpuChartH + fontH * 0.85f;
        float memSectionY = topY + availH * 0.445f;
        float coresAvailH = MathF.Max(fontH * 2f, memSectionY - coresTopY - fontH * 0.8f);

        var corePercents = _viewModel.DisplayedCorePercents;
        int coreCount = corePercents.Length;
        if (coreCount > 0)
        {
            int cols = coreCount <= 16 ? 2 : 4;
            int rows = (coreCount + cols - 1) / cols;
            float rowStep = MathF.Min(lineStep * 1.02f, coresAvailH / Math.Max(1, rows));
            float colGap = width * 0.04f;
            float cellW = (width - colGap * (cols - 1)) / cols;
            float barH = MathF.Max(4f, MathF.Min(fontH * 0.56f, rowStep * 0.48f));

            for (int i = 0; i < coreCount; i++)
            {
                int r = i / cols;
                int c = i % cols;
                float cx = x + c * (cellW + colGap);
                float cy = coresTopY + r * rowStep;

                int slotBase = 210 + i * 2;
                string tLabel = i < 100
                    ? string.Format(CultureInfo.InvariantCulture, "T{0:D2}", i)
                    : string.Format(CultureInfo.InvariantCulture, "T{0}", i);
                DrawTextLeft(ds, slotBase, tLabel, cx, cy, ColorLabel);

                float labelW = fontH * 2.4f;
                float pctW = fontH * 2.7f;
                float barX = cx + labelW;
                float barW = MathF.Max(12f, cellW - labelW - pctW);
                float barY = cy + (fontH - barH) * 0.55f;

                float pct = corePercents[i];
                DrawProgressBar(ds, barX, barY, barW, barH, pct);

                string pctTxt = string.Format(CultureInfo.InvariantCulture, "{0:F0}%", MathF.Round(pct));
                DrawTextRight(ds, slotBase + 1, pctTxt, cx + cellW, cy, ColorText);
            }
        }

        // 2. MEMORY SECTION
        string memRight = snap.Memory.TotalBytes > 0
            ? string.Format(
                CultureInfo.InvariantCulture,
                "{0:F1} / {1:F1} GB  {2:F0}%",
                snap.Memory.UsedBytes / (1024.0 * 1024.0 * 1024.0),
                snap.Memory.TotalBytes / (1024.0 * 1024.0 * 1024.0),
                snap.Memory.UsedPercent)
            : string.Empty;
        DrawSectionHeader(ds, 400, "MEMORY", 401, memRight, x, memSectionY, width, fontH);

        float memChartY = memSectionY + fontH * 1.95f;
        float memChartH = availH * 0.115f;
        DrawFilledChart(ds, _viewModel.MemoryHistory, x, memChartY, width, memChartH, 100f, ColorWarning, ColorFillAmber);

        float memStatsY = memChartY + memChartH + fontH * 0.75f;
        float halfW = width * 0.50f;
        DrawTextLeft(ds, 402, "IN USE", x, memStatsY, ColorLabel);
        DrawTextLeft(ds, 403, DashboardFormatters.FormatGigabytes(snap.Memory.UsedBytes, 1), x + fontH * 5.0f, memStatsY, ColorText);

        if (snap.Memory.CachedBytes.HasValue)
        {
            DrawTextLeft(ds, 404, "CACHED", x + halfW + fontH * 0.8f, memStatsY, ColorLabel);
            DrawTextLeft(ds, 405, DashboardFormatters.FormatGigabytes(snap.Memory.CachedBytes.Value, 1), x + halfW + fontH * 6.8f, memStatsY, ColorText);
        }

        float memStatsY2 = memStatsY + lineStep;
        if (snap.Memory.CommitTotalBytes.HasValue && snap.Memory.CommitLimitBytes.HasValue)
        {
            string commitStr = string.Format(
                CultureInfo.InvariantCulture,
                "{0:F1} / {1:F1} GB",
                snap.Memory.CommitTotalBytes.Value / (1024.0 * 1024.0 * 1024.0),
                snap.Memory.CommitLimitBytes.Value / (1024.0 * 1024.0 * 1024.0));
            DrawTextLeft(ds, 406, "COMMIT", x, memStatsY2, ColorLabel);
            DrawTextLeft(ds, 407, commitStr, x + fontH * 5.0f, memStatsY2, ColorText);
        }

        if (snap.Memory.PagedPoolBytes.HasValue)
        {
            DrawTextLeft(ds, 408, "PAGED POOL", x + halfW + fontH * 0.8f, memStatsY2, ColorLabel);
            DrawTextLeft(ds, 409, DashboardFormatters.FormatMegabytes(snap.Memory.PagedPoolBytes.Value), x + halfW + fontH * 6.8f, memStatsY2, ColorText);
        }

        // 3. NETWORK SECTION
        float netSectionY = topY + availH * 0.745f;
        var net = snap.Network;
        string netTitle = net.IsAvailable
            ? $"NETWORK  {net.AdapterKindLabel} ({ShortenText(net.AdapterName, 18)})"
            : "NETWORK  OFFLINE";
        string netIp = net.IsAvailable ? DashboardFormatters.MaskIpv4OrEndpoint(net.Ipv4Address, mask) : string.Empty;
        DrawSectionHeader(ds, 420, netTitle, 421, netIp, x, netSectionY, width, fontH);

        float netChartY = netSectionY + fontH * 1.95f;
        float netChartH = availH * 0.105f;
        float subChartGap = width * 0.04f;
        float subChartW = (width - subChartGap) * 0.5f;

        float rxPeak = GetAutoScaleMax(_viewModel.NetRxHistory, 64f * 1024f);
        float txPeak = GetAutoScaleMax(_viewModel.NetTxHistory, 64f * 1024f);

        DrawFilledChart(ds, _viewModel.NetRxHistory, x, netChartY, subChartW, netChartH, rxPeak, ColorAccent, ColorFillGreen);
        DrawFilledChart(ds, _viewModel.NetTxHistory, x + subChartW + subChartGap, netChartY, subChartW, netChartH, txPeak, ColorWarning, ColorFillAmber);

        float netRateY = netChartY + netChartH + fontH * 0.55f;
        DrawTextLeft(ds, 422, "RX", x, netRateY, ColorAccent);
        DrawTextLeft(ds, 423, DashboardFormatters.FormatRateBytesPerSec(net.RxBytesPerSec), x + fontH * 2.2f, netRateY, ColorAccent);

        float txX = x + subChartW + subChartGap;
        DrawTextLeft(ds, 424, "TX", txX, netRateY, ColorWarning);
        DrawTextLeft(ds, 425, DashboardFormatters.FormatRateBytesPerSec(net.TxBytesPerSec), txX + fontH * 2.2f, netRateY, ColorWarning);

        if (net.IsAvailable)
        {
            float netMetaY = netRateY + lineStep;
            var metaParts = new List<string>(3);
            if (net.LinkSpeedMbps > 0)
                metaParts.Add($"LINK {net.LinkSpeedMbps} Mbps");
            if (net.WifiRssiDbm.HasValue)
                metaParts.Add($"RSSI {net.WifiRssiDbm.Value} dBm");
            if (!string.IsNullOrWhiteSpace(net.GatewayAddress))
                metaParts.Add($"GW {DashboardFormatters.MaskIpv4OrEndpoint(net.GatewayAddress, mask)}");

            string metaLine = string.Join("   ", metaParts);
            DrawTextLeft(ds, 426, metaLine, x, netMetaY, ColorLabel);
        }
    }

    private void DrawColumn3ProcessesAndConnections(
        CanvasDrawingSession ds,
        DashboardSnapshot snap,
        bool mask,
        float x,
        float topY,
        float width,
        float bottomY,
        float fontH,
        float lineStep)
    {
        float availH = bottomY - topY;

        // 1. TOP PROCESSES
        DrawSectionHeader(ds, 500, "TOP PROCESSES", 501, "sorted by CPU", x, topY, width, fontH);

        float procHeaderY = topY + fontH * 2.0f;
        float colPidX = x;
        float colNameX = x + fontH * 4.5f;
        float colCpuRightX = x + width - fontH * 6.2f;
        float colMemRightX = x + width;

        DrawTextLeft(ds, 502, "PID", colPidX, procHeaderY, ColorLabel);
        DrawTextLeft(ds, 503, "NAME", colNameX, procHeaderY, ColorLabel);
        DrawTextRight(ds, 504, "CPU", colCpuRightX, procHeaderY, ColorLabel);
        DrawTextRight(ds, 505, "MEM", colMemRightX, procHeaderY, ColorLabel);

        float rowY = procHeaderY + lineStep * 1.12f;
        var procs = snap.Processes.TopProcesses;
        for (int i = 0; i < procs.Count; i++)
        {
            var p = procs[i];
            int slotBase = 510 + i * 4;

            Color cpuColor = p.CpuPercent >= 25.0
                ? ColorCritical
                : (p.CpuPercent >= 5.0 ? ColorWarning : ColorText);

            DrawTextLeft(ds, slotBase, p.Pid.ToString(CultureInfo.InvariantCulture), colPidX, rowY, ColorLabel);
            DrawTextLeft(ds, slotBase + 1, ShortenText(p.Name, 26), colNameX, rowY, ColorText);
            DrawTextRight(ds, slotBase + 2, string.Format(CultureInfo.InvariantCulture, "{0:F1}%", p.CpuPercent), colCpuRightX, rowY, cpuColor);
            DrawTextRight(ds, slotBase + 3, DashboardFormatters.FormatMegabytes(p.WorkingSetBytes), colMemRightX, rowY, ColorText);

            rowY += lineStep * 1.06f;
        }

        // 2. ACTIVE CONNECTIONS
        float connSectionY = topY + availH * 0.50f;
        string connSummary = string.Format(
            CultureInfo.InvariantCulture,
            "ESTABLISHED {0}   LISTEN {1}",
            snap.Connections.EstablishedCount,
            snap.Connections.ListenCount);
        DrawSectionHeader(ds, 600, "ACTIVE CONNECTIONS", 601, connSummary, x, connSectionY, width, fontH);

        float connHeaderY = connSectionY + fontH * 2.0f;
        float colProtoX = x;
        float colRemoteX = x + fontH * 4.2f;
        float colStateX = x + width - fontH * 13.5f;
        float colProcRightX = x + width;

        DrawTextLeft(ds, 602, "PROTO", colProtoX, connHeaderY, ColorLabel);
        DrawTextLeft(ds, 603, "REMOTE", colRemoteX, connHeaderY, ColorLabel);
        DrawTextLeft(ds, 604, "STATE", colStateX, connHeaderY, ColorLabel);
        DrawTextRight(ds, 605, "PROCESS", colProcRightX, connHeaderY, ColorLabel);

        float connRowY = connHeaderY + lineStep * 1.12f;
        var rows = snap.Connections.ActiveRows;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            int slotBase = 610 + i * 4;

            string remoteTxt = DashboardFormatters.MaskIpv4OrEndpoint(r.RemoteEndpoint, mask);
            bool isEstab = string.Equals(r.State, "ESTAB", StringComparison.OrdinalIgnoreCase);
            Color stateColor = isEstab ? ColorAccent : ColorLabel;

            DrawTextLeft(ds, slotBase, r.Protocol, colProtoX, connRowY, ColorText);
            DrawTextLeft(ds, slotBase + 1, ShortenText(remoteTxt, 23), colRemoteX, connRowY, ColorText);
            DrawTextLeft(ds, slotBase + 2, r.State, colStateX, connRowY, stateColor, bold: isEstab);
            DrawTextRight(ds, slotBase + 3, ShortenText(r.ProcessName, 16), colProcRightX, connRowY, ColorLabel);

            connRowY += lineStep * 1.06f;
        }
    }

    private void DrawSectionHeader(
        CanvasDrawingSession ds,
        int leftSlotId,
        string leftTitle,
        int rightSlotId,
        string rightSubtitle,
        float x,
        float y,
        float width,
        float fontH)
    {
        DrawTextLeft(ds, leftSlotId, leftTitle, x, y, ColorAccent, bold: true);
        if (!string.IsNullOrEmpty(rightSubtitle))
        {
            DrawTextRight(ds, rightSlotId, rightSubtitle, x + width, y, ColorLabel, bold: false);
        }

        float ruleY = y + fontH * 1.48f;
        ds.DrawLine(x, ruleY, x + width, ruleY, ColorRule, 1f);
    }

    private void DrawKeyValueRow(
        CanvasDrawingSession ds,
        int slotBase,
        string label,
        string? value,
        float keyX,
        float valX,
        ref float y,
        float lineStep,
        float maxChars)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        DrawTextLeft(ds, slotBase, label, keyX, y, ColorLabel);
        DrawTextLeft(ds, slotBase + 1, ShortenText(value, (int)maxChars), valX, y, ColorText);
        y += lineStep;
    }

    private static void DrawProgressBar(CanvasDrawingSession ds, float x, float y, float width, float height, float percent)
    {
        percent = Math.Clamp(percent, 0f, 100f);
        ds.FillRectangle(x, y, width, height, ColorBarTrack);

        float fillW = width * (percent / 100f);
        if (fillW >= 1f)
        {
            Color fillColor = percent >= 90f
                ? ColorCritical
                : (percent >= 70f ? ColorWarning : ColorAccent);
            ds.FillRectangle(x, y, fillW, height, fillColor);
        }
    }

    private void DrawFilledChart(
        CanvasDrawingSession ds,
        RingBuffer<float> buffer,
        float x,
        float y,
        float width,
        float height,
        float maxScaleValue,
        Color strokeColor,
        Color fillColor)
    {
        // 4 faint horizontal gridlines
        for (int g = 0; g <= 3; g++)
        {
            float gy = y + (height / 3f) * g;
            ds.DrawLine(x, gy, x + width, gy, ColorGridLine, 1f);
        }

        int count = buffer.Count;
        if (count == 0) return;

        buffer.CopyTo(_graphScratch);
        int cap = HackerDashboardViewModel.GraphSampleCapacity;
        float safeMax = MathF.Max(1f, maxScaleValue);

        using var strokeBuilder = new CanvasPathBuilder(ds);
        using var fillBuilder = new CanvasPathBuilder(ds);

        int startOffset = cap - count;
        float firstX = x;
        float firstY = y + height;

        for (int i = 0; i < count; i++)
        {
            int slot = startOffset + i;
            float px = x + (slot / (float)(cap - 1)) * width;
            float norm = Math.Clamp(_graphScratch[i] / safeMax, 0f, 1f);
            float py = y + height - (norm * (height - 2f));

            if (i == 0)
            {
                firstX = px;
                firstY = py;
                strokeBuilder.BeginFigure(px, py);
                fillBuilder.BeginFigure(px, y + height);
                fillBuilder.AddLine(px, py);
            }
            else
            {
                strokeBuilder.AddLine(px, py);
                fillBuilder.AddLine(px, py);
            }
        }

        float lastX = count == 1 ? x + width : x + width;
        if (count == 1)
        {
            strokeBuilder.AddLine(lastX, firstY);
            fillBuilder.AddLine(lastX, firstY);
        }

        fillBuilder.AddLine(x + width, y + height);
        fillBuilder.EndFigure(CanvasFigureLoop.Closed);
        strokeBuilder.EndFigure(CanvasFigureLoop.Open);

        using var fillGeo = CanvasGeometry.CreatePath(fillBuilder);
        using var strokeGeo = CanvasGeometry.CreatePath(strokeBuilder);

        ds.FillGeometry(fillGeo, fillColor);
        ds.DrawGeometry(strokeGeo, strokeColor, 1.35f);
    }

    private float GetAutoScaleMax(RingBuffer<float> buffer, float minFloor)
    {
        float max = minFloor;
        int count = buffer.Count;
        for (int i = 0; i < count; i++)
        {
            if (buffer[i] > max)
                max = buffer[i];
        }
        return max * 1.15f;
    }

    private static string CleanCpuBrand(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string s = raw
            .Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("CPU", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Processor", string.Empty, StringComparison.OrdinalIgnoreCase);
        int atIdx = s.IndexOf('@');
        if (atIdx > 0) s = s[..atIdx];
        while (s.Contains("  ", StringComparison.Ordinal))
            s = s.Replace("  ", " ", StringComparison.Ordinal);
        return s.Trim();
    }

    private static string CleanGpuBrand(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return raw
            .Replace("NVIDIA GeForce ", "NVIDIA ", StringComparison.OrdinalIgnoreCase)
            .Replace("AMD Radeon(TM) ", "AMD Radeon ", StringComparison.OrdinalIgnoreCase)
            .Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string ShortenText(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || maxChars <= 3 || text.Length <= maxChars)
            return text;
        return string.Concat(text.AsSpan(0, maxChars - 2), "..");
    }

    private static string MaskEventMessageIfNeeded(string message)
    {
        int arrowIdx = message.IndexOf("-> ", StringComparison.Ordinal);
        if (arrowIdx >= 0)
        {
            string prefix = message[..(arrowIdx + 3)];
            string endpoint = message[(arrowIdx + 3)..].Trim();
            return prefix + DashboardFormatters.MaskIpv4OrEndpoint(endpoint, maskSensitive: true);
        }
        return message;
    }
}
