using System.IO;

namespace AchievementOverlay;

/// <summary>
/// Plays achievement unlock sounds. Uses System.Media.SoundPlayer for .wav files.
/// Supports a custom sound file path, falling back to an embedded default.
/// </summary>
public sealed class UnlockSoundPlayer : IDisposable
{
    /// <summary>
    /// How many loaded files to keep. Per-game sounds make alternation normal — two games unlocking
    /// in one session would otherwise re-read a wav from disk on every notification, synchronously,
    /// on the dispatcher thread.
    /// </summary>
    private const int MaxCachedPlayers = 8;

    private System.Media.SoundPlayer? _defaultPlayer;
    private readonly Dictionary<string, System.Media.SoundPlayer> _customPlayers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Plays a resolved choice rather than reading config here, so the real unlock, the recent panel
    /// and the settings window's "Show me" all go through one implementation and a preview can never
    /// sound different from the thing it previews. Fire-and-forget; errors are logged and swallowed.
    /// </summary>
    /// <param name="fallBackToDefaultOnError">
    /// Set for a file the <em>game</em> supplied: an override must never leave the user worse off
    /// than no override. A path the user typed deliberately does not fall back — silence is the
    /// honest report that the file they chose is wrong.
    /// </param>
    public void Play(bool enabled, string? customPath, bool fallBackToDefaultOnError = false)
    {
        if (!enabled)
            return;

        var hasCustom = !string.IsNullOrEmpty(customPath);
        if (hasCustom && TryPlayFile(customPath!))
            return;

        if (hasCustom && !fallBackToDefaultOnError)
            return;

        TryPlayEmbeddedDefault();
    }

    private bool TryPlayFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Logger.Warn($"Sound file not found: '{path}'");
                return false;
            }

            if (!_customPlayers.TryGetValue(path, out var player))
            {
                if (_customPlayers.Count >= MaxCachedPlayers)
                    ClearCustomPlayers();

                player = new System.Media.SoundPlayer(path);
                player.Load(); // throws here for anything that isn't a PCM wav
                _customPlayers[path] = player;
            }

            player.Play();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not play sound '{path}': {ex.Message}");
            return false;
        }
    }

    private void TryPlayEmbeddedDefault()
    {
        try
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
        catch (Exception ex)
        {
            Logger.Warn($"Error playing sound: {ex.Message}");
        }
    }

    private void ClearCustomPlayers()
    {
        foreach (var player in _customPlayers.Values)
            player.Dispose();
        _customPlayers.Clear();
    }

    public void Dispose()
    {
        _defaultPlayer?.Dispose();
        _defaultPlayer = null;
        ClearCustomPlayers();
    }
}
