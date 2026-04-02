using System.IO;

namespace AchievementOverlay;

/// <summary>
/// Plays achievement unlock sounds. Uses System.Media.SoundPlayer for .wav files.
/// Supports a custom sound file path from config, falling back to an embedded default.
/// </summary>
public sealed class UnlockSoundPlayer : IDisposable
{
    private readonly AppConfig _config;
    private System.Media.SoundPlayer? _defaultPlayer;
    private System.Media.SoundPlayer? _customPlayer;
    private string? _customPlayerPath;

    public UnlockSoundPlayer(AppConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Plays the unlock sound if sound is enabled in config.
    /// Fire-and-forget, non-blocking. Errors are logged and swallowed.
    /// </summary>
    public void Play()
    {
        if (!_config.SoundEnabled)
            return;

        try
        {
            var customPath = _config.SoundPath;
            if (!string.IsNullOrEmpty(customPath))
            {
                if (File.Exists(customPath))
                    PlayFile(customPath);
                else
                    Logger.Warn($"Custom sound file not found: '{customPath}'");
            }
            else
            {
                PlayEmbeddedDefault();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Error playing sound: {ex.Message}");
        }
    }

    private void PlayFile(string path)
    {
        if (_customPlayer == null || _customPlayerPath != path)
        {
            _customPlayer?.Dispose();
            _customPlayer = new System.Media.SoundPlayer(path);
            _customPlayerPath = path;
            _customPlayer.Load();
        }
        _customPlayer.Play();
    }

    private void PlayEmbeddedDefault()
    {
        if (_defaultPlayer == null)
        {
            var stream = typeof(UnlockSoundPlayer).Assembly
                .GetManifestResourceStream("AchievementOverlay.achievement_sound.wav");

            if (stream == null)
            {
                Logger.Warn("Embedded default sound not found");
                return;
            }

            _defaultPlayer = new System.Media.SoundPlayer(stream);
            _defaultPlayer.Load();
        }

        _defaultPlayer.Play();
    }

    public void Dispose()
    {
        _defaultPlayer?.Dispose();
        _defaultPlayer = null;
        _customPlayer?.Dispose();
        _customPlayer = null;
    }
}
