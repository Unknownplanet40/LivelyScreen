using System;
using System.Runtime.InteropServices;

namespace LivelyScreen.Services;

public static class AudioSpectrumService
{
    private static readonly Random _rand = new();
    private static double _phase = 0;
    private static float[] _currentBands = new float[5];
    private static float _peakSmoothed = 0f;

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
    }

    [Guid("C0221673-0667-426C-971B-42C90E8628C4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float pfPeak);
        [PreserveSig]
        int GetMeteringChannelCount(out int pnChannelCount);
        [PreserveSig]
        int GetChannelsPeakValues(int u32ChannelCount, [Out] float[] afPeakValues);
        [PreserveSig]
        int QueryHardwareSupport(out int pdwHardwareSupportMask);
    }

    private static IAudioMeterInformation? _audioMeter;
    private static bool _initAttempted = false;

    private static void EnsureInitialized()
    {
        if (_initAttempted) return;
        _initAttempted = true;

        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            // eRender = 0, eMultimedia = 1
            int hr = enumerator.GetDefaultAudioEndpoint(0, 1, out var device);
            if (hr == 0 && device != null)
            {
                var iid = typeof(IAudioMeterInformation).GUID;
                hr = device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var meterObj);
                if (hr == 0 && meterObj is IAudioMeterInformation meter)
                {
                    _audioMeter = meter;
                }
            }
        }
        catch
        {
            _audioMeter = null;
        }
    }

    public static float[] GetSpectrumBands(bool isMediaPlaying)
    {
        EnsureInitialized();

        float peak = 0f;
        bool hasHardwareMeter = false;

        if (_audioMeter != null)
        {
            try
            {
                int hr = _audioMeter.GetPeakValue(out peak);
                if (hr == 0)
                {
                    hasHardwareMeter = true;
                }
            }
            catch
            {
                _audioMeter = null;
                _initAttempted = false;
            }
        }

        // If hardware peak is not available or 0 but media session is playing, synthesize lively movement
        if (!hasHardwareMeter || peak < 0.005f)
        {
            if (isMediaPlaying)
            {
                peak = (float)(0.35 + 0.35 * Math.Sin(_phase * 2.5));
            }
            else
            {
                peak = 0f;
            }
        }

        // Smooth peak
        _peakSmoothed = _peakSmoothed * 0.7f + peak * 0.3f;
        _phase += 0.25;
        if (_phase > Math.PI * 2) _phase -= Math.PI * 2;

        if (_peakSmoothed < 0.01f)
        {
            for (int i = 0; i < 5; i++)
            {
                _currentBands[i] = Math.Max(0f, _currentBands[i] * 0.75f - 0.02f);
            }
            return _currentBands;
        }

        // Generate 5 equalizer bands (Bass, Mid-Low, Mid, Mid-High, Treble)
        float b0 = (float)Math.Clamp(_peakSmoothed * (0.8 + 0.3 * Math.Sin(_phase + 0.1)), 0.05, 1.0);
        float b1 = (float)Math.Clamp(_peakSmoothed * (1.1 + 0.3 * Math.Sin(_phase * 1.3 + 1.2)), 0.05, 1.0);
        float b2 = (float)Math.Clamp(_peakSmoothed * (0.95 + 0.35 * Math.Sin(_phase * 1.7 + 2.4)), 0.05, 1.0);
        float b3 = (float)Math.Clamp(_peakSmoothed * (0.75 + 0.25 * Math.Sin(_phase * 2.1 + 3.6)), 0.05, 1.0);
        float b4 = (float)Math.Clamp(_peakSmoothed * (0.6 + 0.25 * Math.Sin(_phase * 2.6 + 4.8)), 0.05, 1.0);

        _currentBands[0] = _currentBands[0] * 0.4f + b0 * 0.6f;
        _currentBands[1] = _currentBands[1] * 0.4f + b1 * 0.6f;
        _currentBands[2] = _currentBands[2] * 0.4f + b2 * 0.6f;
        _currentBands[3] = _currentBands[3] * 0.4f + b3 * 0.6f;
        _currentBands[4] = _currentBands[4] * 0.4f + b4 * 0.6f;

        return _currentBands;
    }
}
