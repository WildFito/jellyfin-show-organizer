# ShowOrganizer Jellyfin Plugin

[![Build ShowOrganizer](https://github.com/WildFito/jellyfin-show-organizer/actions/workflows/build.yml/badge.svg)](https://github.com/WildFito/jellyfin-show-organizer/actions/workflows/build.yml)
[![GitHub Release](https://img.shields.io/github/v/release/WildFito/jellyfin-show-organizer)](https://github.com/WildFito/jellyfin-show-organizer/releases)
[![License](https://img.shields.io/github/license/WildFito/jellyfin-show-organizer)](LICENSE)

ShowOrganizer is a metadata provider for Jellyfin that lets a TV series use a specific [The Movie Database (TMDb)](https://www.themoviedb.org/) Episode Group — such as a saga, story-arc, or alternative episode ordering — while preserving the custom season and episode numbering used by the files in Jellyfin. ShowOrganizer maps each custom Jellyfin `SxxExx` episode to its canonical TMDb episode so Jellyfin can retrieve the correct metadata without renumbering the user's library.

## Supported Versions & Providers

* **Supported Jellyfin Server Version**: 10.11.x (currently built and tested against **10.11.11**, targetAbi `10.11.0.0`)
* **Supported Metadata Provider**: [The Movie Database (TMDb)](https://www.themoviedb.org/)

## TMDb API Key Credentials

Out of the box, ShowOrganizer **automatically reuses Jellyfin's built-in TMDb API key**. No manual API key setup is required if Jellyfin's standard TMDb provider is active.

*(Optional Advanced Override)*: If you wish to provide a custom TMDb API key, you can optionally define it in `plugins/configurations/Jellyfin.Plugin.ShowOrganizer.xml`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <TmdbApiKey>YOUR_TMDB_API_KEY</TmdbApiKey>
</PluginConfiguration>
```

## How to Use ShowOrganizer

### A. How to Find IDs on TMDb

1. **TheMovieDb Programme Id**:
   Open the series page on TMDb (e.g. `https://www.themoviedb.org/tv/61709-dragon-ball-z-kai`).
   Copy the numeric ID following `/tv/`:
   `/tv/61709-dragon-ball-z-kai` -> `61709`

2. **TheMovieDb Show Group Programme Id**:
   Open your desired Episode Group page (e.g. `https://www.themoviedb.org/tv/61709-dragon-ball-z-kai/episode_group/648fc7202f8d0900e3864f62`).
   Copy the hexadecimal hash following `/episode_group/`:
   `/episode_group/648fc7202f8d0900e3864f62` -> `648fc7202f8d0900e3864f62`

---

### B. Dragon Ball Z Kai Step-by-Step Example

1. Open **Dragon Ball Z Kai** in your Jellyfin web interface.
2. Click the three dots `...` and select **Edit Metadata**.
3. In **TheMovieDb Programme Id**, enter:
   `61709`
4. In **TheMovieDb Show Group Programme Id** (or NFO `<showorganizerid>`), enter:
   `648fc7202f8d0900e3864f62`
   *(Note: Legacy `tmdb:<group-id>` values remain fully supported for backward compatibility).*
5. Click **Save**.
6. Ensure **ShowOrganizer** is ordered above standard metadata providers in your library settings (**Dashboard -> Libraries -> TV Shows -> Manage Library -> Metadata Readers / Providers**).
7. Refresh metadata for the series (**Refresh Metadata -> Replace all metadata**).

> [!NOTE]
> **Prerequisite**: Your local files and folders must already be organized and numbered according to the season/episode structure of your chosen TMDb Episode Group.

---

### C. How Mapping Works

Suppose you have **Season 2, Episode 3** (`S02E03`) of *Dragon Ball Z Kai* in your local library:

* **Custom Jellyfin Coordinate**: `S02E03`
* **TMDb Episode Group Lookup**:
  * **Group Order**: Custom Season 2 maps to Episode Group **Order 2** (*Namek Saga*).
  * **Episode Order**: Custom Episode 3 maps to Episode **Order 2** (0-based: `3 - 1 = 2`).
* **Canonical TMDb Episode Resolved**: Season 1, Episode 19 (*"Run, Gohan! Long-Awaited Namek!"*).
* **Metadata Applied**: Title, Overview, Air Date, Cast & Crew from S01E19 are applied to your item, while Jellyfin displays it as `S02E03` in your library.

> [!NOTE]
> **Indexing Rules**: TMDb Episode Group subgroup `Order` is 1-based (`Season N` -> `Group Order N`). Episode `Order` inside a subgroup is 0-based (`Episode E` -> `Group Episode Order E - 1`).

---

### D. Fallback Behavior

ShowOrganizer is strictly **opt-in**. If:
* Neither ID is present, or
* Either ID is missing, malformed, or unresolvable, or
* An episode coordinate cannot be mapped inside the configured Episode Group,

ShowOrganizer gracefully declines to provide metadata (`HasMetadata = false`). Jellyfin's `ProviderManager` then automatically falls back to the next configured metadata provider (e.g. standard TMDb or TVDB).

---

### E. Limitations

* **Season Artwork**: TMDb Episode Groups provide custom saga names and episode structures, but subgroup artwork is not supplied by TMDb Episode Groups. Saga season posters may need to be provided via local artwork or another image provider.
* **Canonical IDs**: ShowOrganizer maps episode ordering; it does not alter canonical TMDb IDs.
* **Multiple Cuts**: Multiple custom cuts or fan-editions can reference the same canonical TMDb series/episode IDs while using different custom ordering groups.

---

## NFO Configuration Example

To enable ShowOrganizer for a series, write the group ID in the series level NFO file (`tvshow.nfo`) under the `<showorganizerid>` XML element:

```xml
<tvshow>
    <title>Dragon Ball Z Kai</title>
    <tmdbid>61709</tmdbid>
    <showorganizerid>648fc7202f8d0900e3864f62</showorganizerid>
</tvshow>
```

Alternatively, configure the value in the Jellyfin web interface under **Edit Metadata -> External IDs -> TheMovieDb Show Group** with: `648fc7202f8d0900e3864f62` (or `tmdb:648fc7202f8d0900e3864f62`).

## Repository Installation

To add the ShowOrganizer plugin catalog repository to your Jellyfin server:

1. Go to **Dashboard -> Plugins** in your Jellyfin administrator panel.
2. Select the **Repositories** tab (or click **Manage Repositories**).
3. Click **Add** to add a new repository catalog.
4. Set the following values:
   * **Repository Name**: `ShowOrganizer`
   * **Repository URL**: `https://raw.githubusercontent.com/WildFito/jellyfin-show-organizer/main/manifest.json`
5. Save, then select the **Catalog** tab.
6. Find and click on **ShowOrganizer**, then click **Install**.
7. Restart your Jellyfin server.

## Manual Installation

1. Build the project in Release mode.
2. Locate the compiled plugin DLL file `Jellyfin.Plugin.ShowOrganizer.dll` in `src/Jellyfin.Plugin.ShowOrganizer/bin/Release/net9.0/`.
3. Create a folder named `ShowOrganizer` inside your Jellyfin server's `plugins/` directory:
   ```bash
   mkdir -p /path/to/jellyfin/config/plugins/ShowOrganizer
   ```
4. Copy `Jellyfin.Plugin.ShowOrganizer.dll` into the directory and restart your Jellyfin server.

## Build & Test Instructions

### Building the Plugin

To compile the plugin in Release mode:

```bash
dotnet restore
dotnet build --configuration Release
```

### Running Tests

To run the unit and integration test suite:

```bash
dotnet test
```
*(If your host SDK is higher than Net 9, configure roll-forward: `$env:DOTNET_ROLL_FORWARD="Major"; dotnet test`)*

## Documentation Links

* [Architecture Overview](docs/architecture.md)
* [NFO Format Details](docs/nfo-format.md)
* [Development Setup](docs/development.md)
* [Release and Packaging Guide](docs/releasing.md)
