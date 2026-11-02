using Microsoft.UI.Dispatching;
using LivelyScreen.Services.Dashboard;

namespace LivelyScreen.ViewModels;

public sealed class HackerDashboardViewModel
{
    public const int GraphSampleCapacity = 60;
    public const int MaxEventLogCapacity = 24;
    public const double BarEaseDurationSeconds = 0.30; // ~300 ms ease

    private readonly DispatcherQueue? _dispatcherQueue;

    public RingBuffer<float> CpuHistory { get; } = new(GraphSampleCapacity);
    public RingBuffer<float> MemoryHistory { get; } = new(GraphSampleCapacity);
    public RingBuffer<float> NetRxHistory { get; } = new(GraphSampleCapacity);
    public RingBuffer<float> NetTxHistory { get; } = new(GraphSampleCapacity);
    public RingBuffer<DashboardEventEntry> EventLog { get; } = new(MaxEventLogCapacity);

    public DashboardSnapshot? LatestSnapshot { get; private set; }
    public bool MaskSensitive { get; set; }

    private float[] _displayedCorePercents = [];
    private float[] _targetCorePercents = [];

    private float[] _displayedDrivePercents = [];
    private float[] _targetDrivePercents = [];

    public ReadOnlySpan<float> DisplayedCorePercents => _displayedCorePercents;
    public ReadOnlySpan<float> DisplayedDrivePercents => _displayedDrivePercents;

    public event Action? StateUpdated;

    public HackerDashboardViewModel(DispatcherQueue? dispatcherQueue = null)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public void OnSnapshotReceived(DashboardSnapshot snapshot)
    {
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => ApplySnapshotOnUiThread(snapshot));
        }
        else
        {
            ApplySnapshotOnUiThread(snapshot);
        }
    }

    public void ApplySnapshotOnUiThread(DashboardSnapshot snapshot)
    {
        bool isFirst = LatestSnapshot == null;
        LatestSnapshot = snapshot;

        CpuHistory.Add((float)DashboardFormatters.ClampPercent(snapshot.Cpu.TotalPercent));
        MemoryHistory.Add((float)DashboardFormatters.ClampPercent(snapshot.Memory.UsedPercent));
        NetRxHistory.Add((float)DashboardFormatters.SanitizeNonNegative(snapshot.Network.RxBytesPerSec));
        NetTxHistory.Add((float)DashboardFormatters.SanitizeNonNegative(snapshot.Network.TxBytesPerSec));

        // Update per-core bar targets
        int coreCount = snapshot.Cpu.PerCorePercent.Count;
        if (_targetCorePercents.Length != coreCount)
        {
            _targetCorePercents = new float[coreCount];
            _displayedCorePercents = new float[coreCount];
            isFirst = true;
        }
        for (int i = 0; i < coreCount; i++)
        {
            float v = (float)DashboardFormatters.ClampPercent(snapshot.Cpu.PerCorePercent[i]);
            _targetCorePercents[i] = v;
            if (isFirst)
            {
                _displayedCorePercents[i] = v;
            }
        }

        // Update storage drive bar targets
        int driveCount = snapshot.Storage.Drives.Count;
        if (_targetDrivePercents.Length != driveCount)
        {
            _targetDrivePercents = new float[driveCount];
            _displayedDrivePercents = new float[driveCount];
            isFirst = true;
        }
        for (int i = 0; i < driveCount; i++)
        {
            float v = (float)DashboardFormatters.ClampPercent(snapshot.Storage.Drives[i].UsedPercent);
            _targetDrivePercents[i] = v;
            if (isFirst)
            {
                _displayedDrivePercents[i] = v;
            }
        }

        // Append new diff events
        for (int i = 0; i < snapshot.NewEvents.Count; i++)
        {
            EventLog.Add(snapshot.NewEvents[i]);
        }

        StateUpdated?.Invoke();
    }

    /// <summary>
    /// Smoothly eases bar values toward their latest target over ~300 ms.
    /// Returns true if any bar is still animating.
    /// </summary>
    public bool AdvanceAnimations(double dtSeconds)
    {
        if (dtSeconds <= 0.0 || double.IsNaN(dtSeconds) || double.IsInfinity(dtSeconds))
            return false;

        // Exponential decay factor reaching ~95% in 300 ms
        float alpha = (float)Math.Clamp(1.0 - Math.Exp(-dtSeconds / 0.10), 0.0, 1.0);
        bool anyMoving = false;

        for (int i = 0; i < _displayedCorePercents.Length; i++)
        {
            float diff = _targetCorePercents[i] - _displayedCorePercents[i];
            if (MathF.Abs(diff) > 0.08f)
            {
                _displayedCorePercents[i] += diff * alpha;
                anyMoving = true;
            }
            else
            {
                _displayedCorePercents[i] = _targetCorePercents[i];
            }
        }

        for (int i = 0; i < _displayedDrivePercents.Length; i++)
        {
            float diff = _targetDrivePercents[i] - _displayedDrivePercents[i];
            if (MathF.Abs(diff) > 0.08f)
            {
                _displayedDrivePercents[i] += diff * alpha;
                anyMoving = true;
            }
            else
            {
                _displayedDrivePercents[i] = _targetDrivePercents[i];
            }
        }

        return anyMoving;
    }
}
