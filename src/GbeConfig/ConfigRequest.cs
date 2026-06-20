namespace AchievementOverlay.GbeConfig;

/// <summary>
/// Inputs for a config generation run, collected by the Add-game dialog.
/// </summary>
public sealed class ConfigRequest
{
    /// <summary>The game's root folder (the Steam DLL may be nested below it).</summary>
    public required string GameDir { get; init; }

    /// <summary>Explicit AppID, or null/empty to auto-detect.</summary>
    public string? AppId { get; init; }

    /// <summary>Steam Web API key (required — used for the achievement schema).</summary>
    public required string ApiKey { get; init; }

    /// <summary>Optional Firecrawl API key, used to scrape hidden-achievement descriptions from SteamDB.</summary>
    public string? FirecrawlApiKey { get; init; }

    /// <summary>Folder where the GBE release lives / is downloaded to (reused across games).</summary>
    public required string GbeFolder { get; init; }

    /// <summary>Download the latest GBE release into <see cref="GbeFolder"/>; if false, reuse what's there.</summary>
    public bool DownloadLatest { get; init; } = true;

    /// <summary>Back up the original Steam DLL to <c>*.original</c> before replacing it.</summary>
    public bool Backup { get; init; } = true;

    /// <summary>Overwrite an existing <c>steam_settings/</c> folder.</summary>
    public bool Force { get; init; }
}
