# ShowOrganizer Jellyfin Plugin

[![Build ShowOrganizer](https://github.com/WildFito/jellyfin-show-organizer/actions/workflows/build.yml/badge.svg)](https://github.com/WildFito/jellyfin-show-organizer/actions/workflows/build.yml)
[![GitHub Release](https://img.shields.io/github/v/release/WildFito/jellyfin-show-organizer)](https://github.com/WildFito/jellyfin-show-organizer/releases)
[![License](https://img.shields.io/github/license/WildFito/jellyfin-show-organizer)](LICENSE)

ShowOrganizer is a metadata provider plugin for Jellyfin Server that overrides built-in episode ordering logic to support exact episode groups, while preserving local custom numbering.

## What It Does

ShowOrganizer allows users to map a TV Series in Jellyfin to a specific TMDB (The Movie Database) **Episode Group** (such as a Saga or Story Arc ordering) by entering a unique identifier. This permits custom season and episode ordering on a per-show basis.

## Why It Exists (Current Jellyfin Limitations)

Jellyfin's native TMDB integration selects episode groups using a generic type lookup (e.g., DVD, Absolute, Digital). It only resolves to the *first* group matching that type. 
For shows like *Dragon Ball Z Kai*, TMDB contains multiple groups with the same type (e.g., *Absolute (Japan)* and *Absolute (International)*). In this case, Jellyfin only allows selecting the first match (*Absolute (Japan)*) and does not permit picking the international variant or specific custom saga orderings.

ShowOrganizer addresses this limitation by letting users configure the exact TMDB Episode Group ID they want to map their series to.

## Key Features

* **Exact Episode Group Mapping**: Map any TV show to its exact TMDB Episode Group ID.
* **Numbering Preservation**: Resolved original coordinates from TMDB are used strictly for remote data lookups. Jellyfin's custom season and episode indices (`IndexNumber`, `ParentIndexNumber`) remain unchanged in the library so physical file renames are not required.
* **DI Registration**: Registers services dynamically into Jellyfin's DI framework.
* **Shared Memory Cache**: Utilizes Jellyfin's built-in `IMemoryCache` to cache group definitions for 1 hour to ensure high scanning performance.
* **Opt-In Fallback**: If the `ShowOrganizer` ID is missing on a Series, the plugin steps aside immediately and lets standard providers run.

## Supported Versions & Providers

* **Supported Jellyfin Server Version**: 10.11.x (currently built and tested against **10.11.11**, targetAbi `10.11.0.0`)
* **Supported Metadata Providers**: TMDB (The Movie Database)

## TMDB API Key Configuration

Because the plugin does not include a custom settings UI, the TMDB API key must be configured in the plugin's configuration file:

1. Stop your Jellyfin server.
2. Locate the ShowOrganizer configuration file under your Jellyfin configuration directory at:
   `plugins/configurations/Jellyfin.Plugin.ShowOrganizer.xml`
3. If it does not exist, create the file with the following contents, replacing `YOUR_TMDB_API_KEY` with your actual TMDB API key:
   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
     <TmdbApiKey>YOUR_TMDB_API_KEY</TmdbApiKey>
   </PluginConfiguration>
   ```
4. Start your Jellyfin server.

## How to Use ShowOrganizer

### A. What ShowOrganizer Does

ShowOrganizer allows Jellyfin to organize TV shows using exact **TheMovieDb (TMDb) Episode Groups** (such as Sagas, Story Arcs, or Custom Broadcast Orders) while retaining the canonical episode metadata from TMDb.

* **TheMovieDb Programme Id**: Identifies the canonical TMDb TV series (e.g. `61709`).
* **TheMovieDb Show Group Programme Id**: Identifies the exact TMDb Episode Group used for custom ordering (e.g. `tmdb:648fc7202f8d0900e3864f62`).
* **Preserved Custom Numbering**: Your local Jellyfin season and episode numbers (SxxExx) remain unchanged. ShowOrganizer uses canonical TMDb coordinates behind the scenes to fetch metadata without renumbering your files.

---

### B. Requirements

Before configuring a show with ShowOrganizer, ensure you have:

1. The canonical **TheMovieDb Programme Id** for the series.
2. A valid **TheMovieDb Episode Group ID** for your target ordering.
3. ShowOrganizer enabled and prioritized in Jellyfin Library Metadata Provider settings.
4. Your media files and folders already numbered according to your chosen Episode Group structure.

---

### C. How to Find IDs on TheMovieDb (TMDb)

1. **Finding the TV Series ID**:
   Navigate to the show on TMDb (e.g. `https://www.themoviedb.org/tv/61709-dragon-ball-z-kai`).
   The numeric portion immediately following `/tv/` is the TV Series ID (`61709`).

2. **Finding the Episode Group ID**:
   Navigate to the Episode Group page under the show (e.g. `https://www.themoviedb.org/tv/61709-dragon-ball-z-kai/episode_group/648fc7202f8d0900e3864f62`).
   The hexadecimal hash following `/episode_group/` is the Episode Group ID (`648fc7202f8d0900e3864f62`).

---

### D. Dragon Ball Z Kai Worked Example

Follow these steps to configure *Dragon Ball Z Kai* with its official **Saga Order (Story Arc)**:

1. Open **Dragon Ball Z Kai** in your Jellyfin web interface.
2. Click the three dots `...` and select **Edit Metadata**.
3. In **TheMovieDb Programme Id**, verify or enter the canonical series ID:
   `61709`
4. In **TheMovieDb Show Group Programme Id** (or NFO `<showorganizerid>`), enter the group reference:
   `tmdb:648fc7202f8d0900e3864f62`
5. Click **Save**.
6. Ensure **ShowOrganizer** is ordered above standard metadata providers in your library settings (**Dashboard -> Libraries -> TV Shows -> Manage Library -> Metadata Readers / Providers**).
7. Refresh metadata for the series (**Refresh Metadata -> Replace all metadata**).
8. **Result**: Jellyfin will organize seasons into Sagas (Season 1: *Saiyan Saga*, Season 2: *Namek Saga*, etc.) and pull accurate episode titles and descriptions while leaving your SxxExx file numbering untouched.

---

### E. How Episode Group Mapping Works

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

### F. Fallback Behavior

ShowOrganizer is strictly **opt-in**. If:
* Neither ID is present, or
* Either ID is missing, malformed, or unresolvable, or
* An episode coordinate cannot be mapped inside the configured Episode Group,

ShowOrganizer gracefully declines to provide metadata (`HasMetadata = false`). Jellyfin's `ProviderManager` then automatically falls back to the next configured metadata provider (e.g. standard TMDb or TVDB).

---

### G. Limitations

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
    <showorganizerid>tmdb:648fc7202f8d0900e3864f62</showorganizerid>
</tvshow>
```

Alternatively, configure the value in the Jellyfin web interface under **Edit Metadata -> External IDs -> ShowOrganizer** with: `tmdb:648fc7202f8d0900e3864f62`.

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
