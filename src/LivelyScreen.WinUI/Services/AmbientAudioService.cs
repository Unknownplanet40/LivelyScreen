using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using LivelyScreen.Models;

namespace LivelyScreen.Services;

public static class AmbientAudioService
{
    private static MediaPlayer? _player;
    private static byte[]? _rainWavCache;
    private static byte[]? _hearthWavCache;

    private static readonly List<string> _playlist = new();
    private static int _playlistIndex = 0;
    private static readonly string[] _supportedExtensions =
    {
        ".flac", ".mp3", ".wav", ".m4a", ".aac", ".wma", ".ogg"
    };

    private static GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private static System.Threading.Timer? _duckingTimer;
    private static bool _isDucked = false;
    private static string? _currentTrackName;

    public static string? CurrentTrackName => _isDucked ? null : _currentTrackName;

    public static async void Start(AppSettings settings)
    {
        Stop();

        if (settings.AmbientSoundMode == AmbientSoundscapeMode.None)
            return;

        try
        {
            _isDucked = await IsExternalMediaPlayingAsync();

            _player = new MediaPlayer
            {
                Volume = Math.Clamp(settings.AmbientSoundVolume, 0, 100) / 100.0
            };

            switch (settings.AmbientSoundMode)
            {
                case AmbientSoundscapeMode.CustomMusicFolder:
                    StartCustomMusic(settings);
                    break;

                case AmbientSoundscapeMode.SoothingRain:
                    _player.IsLoopingEnabled = true;
                    _rainWavCache ??= GenerateRainNoiseWav(6, 44100);
                    PlayFromBytes(_rainWavCache);
                    _currentTrackName = "Soothing Rain";
                    break;

                case AmbientSoundscapeMode.WarmHearth:
                    _player.IsLoopingEnabled = true;
                    _hearthWavCache ??= GenerateHearthNoiseWav(6, 44100);
                    PlayFromBytes(_hearthWavCache);
                    _currentTrackName = "Warm Hearth";
                    break;
            }

            // Monitor every 1.2s to auto-duck ambient music if Spotify starts, or resume when Spotify pauses
            _duckingTimer = new System.Threading.Timer(OnDuckingTick, null, 1200, 1200);
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"AmbientAudioService error: {ex.Message}");
        }
    }

    private static async Task<bool> IsExternalMediaPlayingAsync()
    {
        try
        {
            _sessionManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = _sessionManager?.GetCurrentSession();
            if (session != null)
            {
                var info = session.GetPlaybackInfo();
                return info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
        }
        catch { }
        return false;
    }

    private static async void OnDuckingTick(object? state)
    {
        bool externalPlaying = await IsExternalMediaPlayingAsync();

        if (externalPlaying && !_isDucked)
        {
            _isDucked = true;
            try { _player?.Pause(); } catch { }
            LivelyScreen_WinUI.App.Log("AmbientAudioService: external media playing (Spotify/etc), auto-ducking screensaver audio.");
        }
        else if (!externalPlaying && _isDucked)
        {
            _isDucked = false;
            try { _player?.Play(); } catch { }
            LivelyScreen_WinUI.App.Log("AmbientAudioService: external media stopped, resuming screensaver audio.");
        }
    }

    private static void StartCustomMusic(AppSettings settings)
    {
        _playlist.Clear();
        _playlistIndex = 0;

        string target = settings.AmbientCustomAudioPath?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(target)) return;

        if (Directory.Exists(target))
        {
            try
            {
                var files = Directory.EnumerateFiles(target, "*.*", SearchOption.AllDirectories)
                    .Where(f => _supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                if (files.Count > 0)
                {
                    if (settings.AmbientMusicShuffle)
                    {
                        var rng = new Random();
                        int n = files.Count;
                        while (n > 1)
                        {
                            n--;
                            int k = rng.Next(n + 1);
                            (files[k], files[n]) = (files[n], files[k]);
                        }
                    }
                    _playlist.AddRange(files);
                }
            }
            catch (Exception ex)
            {
                LivelyScreen_WinUI.App.Log($"Error scanning custom music folder: {ex.Message}");
            }
        }
        else if (File.Exists(target))
        {
            _playlist.Add(target);
        }

        if (_playlist.Count == 0 || _player == null) return;

        _player.IsLoopingEnabled = _playlist.Count == 1;
        _player.MediaEnded += OnTrackEnded;

        PlayTrackAtIndex(_playlistIndex);
    }

    private static void OnTrackEnded(MediaPlayer sender, object args)
    {
        if (_playlist.Count <= 1) return;

        _playlistIndex = (_playlistIndex + 1) % _playlist.Count;
        PlayTrackAtIndex(_playlistIndex);
    }

    private static void PlayTrackAtIndex(int index)
    {
        if (_player == null || index < 0 || index >= _playlist.Count) return;

        try
        {
            string trackPath = _playlist[index];
            _currentTrackName = Path.GetFileNameWithoutExtension(trackPath);
            var uri = new Uri(trackPath);
            _player.Source = MediaSource.CreateFromUri(uri);
            if (!_isDucked)
            {
                _player.Play();
            }
            LivelyScreen_WinUI.App.Log($"AmbientAudioService loaded: {_currentTrackName} ({index + 1}/{_playlist.Count}, isDucked={_isDucked})");
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"Error playing track {_playlist[index]}: {ex.Message}");
            if (_playlist.Count > 1)
            {
                _playlistIndex = (_playlistIndex + 1) % _playlist.Count;
                PlayTrackAtIndex(_playlistIndex);
            }
        }
    }

    public static void Stop()
    {
        _duckingTimer?.Dispose();
        _duckingTimer = null;
        _isDucked = false;

        if (_player != null)
        {
            try
            {
                _player.MediaEnded -= OnTrackEnded;
                _player.Pause();
                _player.Source = null;
                _player.Dispose();
            }
            catch { }
            finally
            {
                _player = null;
                _currentTrackName = null;
                _playlist.Clear();
            }
        }
    }

    private static void PlayFromBytes(byte[] wavData)
    {
        if (_player == null) return;
        var ms = new MemoryStream(wavData);
        var ras = ms.AsRandomAccessStream();
        _player.Source = MediaSource.CreateFromStream(ras, "audio/wav");
        if (!_isDucked)
        {
            _player.Play();
        }
    }

    private static byte[] GenerateRainNoiseWav(int seconds, int sampleRate)
    {
        int sampleCount = seconds * sampleRate;
        short[] samples = new short[sampleCount];
        var rand = new Random(101);

        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            double white = rand.NextDouble() * 2.0 - 1.0;
            b0 = 0.99886 * b0 + white * 0.0555179;
            b1 = 0.99332 * b1 + white * 0.0750759;
            b2 = 0.96900 * b2 + white * 0.1538520;
            b3 = 0.86650 * b3 + white * 0.3104856;
            b4 = 0.55000 * b4 + white * 0.5329522;
            b5 = -0.7616 * b5 - white * 0.0168980;
            double pink = b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362;
            b6 = white * 0.115926;
            double val = Math.Clamp(pink * 0.09, -1.0, 1.0);
            samples[i] = (short)(val * 32767);
        }

        return CreateWav(samples, sampleRate);
    }

    private static byte[] GenerateHearthNoiseWav(int seconds, int sampleRate)
    {
        int sampleCount = seconds * sampleRate;
        short[] samples = new short[sampleCount];
        var rand = new Random(202);

        double lowPass = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            double white = rand.NextDouble() * 2.0 - 1.0;
            lowPass += (white - lowPass) * 0.04;

            double crackle = 0;
            if (rand.NextDouble() < 0.0018)
            {
                crackle = (rand.NextDouble() * 2.0 - 1.0) * 0.8;
            }

            double val = Math.Clamp(lowPass * 0.45 + crackle, -1.0, 1.0);
            samples[i] = (short)(val * 32767);
        }

        return CreateWav(samples, sampleRate);
    }

    private static byte[] CreateWav(short[] samples, int sampleRate)
    {
        int sampleCount = samples.Length;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + sampleCount * 2);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write((short)1); // Mono
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(sampleCount * 2);
        for (int i = 0; i < sampleCount; i++)
        {
            bw.Write(samples[i]);
        }
        return ms.ToArray();
    }
}
