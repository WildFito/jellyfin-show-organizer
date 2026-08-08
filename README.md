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
