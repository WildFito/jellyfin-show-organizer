# ShowOrganizer Jellyfin Plugin

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

* **Supported Jellyfin Server Version**: 10.11.11
* **Supported Metadata Providers**: TMDB (The Movie Database)

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

## Manual Installation

1. Build the project in Release mode.
2. Locate the compiled `Jellyfin.Plugin.ShowOrganizer.dll` and `TMDbLib.dll` in `src/Jellyfin.Plugin.ShowOrganizer/bin/Release/net9.0/`.
3. Create a folder named `ShowOrganizer` inside your Jellyfin server's `plugins/` directory:
   ```bash
   mkdir -p /path/to/jellyfin/config/plugins/ShowOrganizer
   ```
4. Copy the compiled DLLs into it and restart your Jellyfin server.

## Repository Installation (Future)

Add the URL of the hosted `manifest.json` file under **Dashboard -> Plugins -> Repositories** to install and update ShowOrganizer from Jellyfin's Plugin Catalog.

## Documentation Links

* [Architecture Overview](docs/architecture.md)
* [NFO Format Details](docs/nfo-format.md)
* [Development Setup](docs/development.md)
* [Release and Packaging Guide](docs/releasing.md)
