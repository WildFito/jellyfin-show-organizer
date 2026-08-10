# ShowOrganizer Jellyfin Plugin

[![Build ShowOrganizer](https://github.com/WildFito/jellyfin-show-organizer/actions/workflows/build.yml/badge.svg)](https://github.com/WildFito/jellyfin-show-organizer/actions/workflows/build.yml)
[![GitHub Release](https://img.shields.io/github/v/release/WildFito/jellyfin-show-organizer)](https://github.com/WildFito/jellyfin-show-organizer/releases)
[![License](https://img.shields.io/github/license/WildFito/jellyfin-show-organizer)](LICENSE)

ShowOrganizer is a metadata provider for Jellyfin that lets a TV series use a specific [The Movie Database (TMDb)](https://www.themoviedb.org/) Episode Group — such as a saga, story arc, or alternative episode ordering — while preserving the custom season and episode numbering used by the files in Jellyfin. ShowOrganizer maps each custom Jellyfin `SxxExx` episode to its canonical TMDb episode so Jellyfin can retrieve the correct metadata without renumbering the user's library.

## Compatibility & Requirements

* **Supported Jellyfin Server Version**: 10.11.x (currently built and tested against **10.11.11**, targetAbi `10.11.0.0`)
* **Supported Metadata Provider**: [The Movie Database (TMDb)](https://www.themoviedb.org/)

## Installation

### Repository Installation (Recommended)

1. In your Jellyfin administrator panel, go to **Dashboard -> Plugins -> Repositories**.
2. Click **Add** and enter:
   * **Repository Name**: `ShowOrganizer`
   * **Repository URL**: `https://raw.githubusercontent.com/WildFito/jellyfin-show-organizer/main/manifest.json`
3. Save, navigate to the **Catalog** tab, select **ShowOrganizer**, and click **Install**.
4. Restart your Jellyfin server.

### Manual Installation

1. Download or compile `Jellyfin.Plugin.ShowOrganizer.dll`.
2. Create a folder named `ShowOrganizer` in your Jellyfin plugins directory (`plugins/ShowOrganizer`).
3. Copy `Jellyfin.Plugin.ShowOrganizer.dll` into the directory and restart Jellyfin.

## How to Use

### 1. Find the IDs on TMDb

To configure a show, you need two IDs from [The Movie Database (TMDb)](https://www.themoviedb.org/):

* **TheMovieDb Programme Id**:
  Open the series page on TMDb (e.g., `https://www.themoviedb.org/tv/61709-dragon-ball-z-kai`).
  Copy the numeric ID following `/tv/`:
  `/tv/61709-dragon-ball-z-kai` $\rightarrow$ `61709`

* **TheMovieDb Show Group Programme Id**:
  Open your desired Episode Group page (e.g., `https://www.themoviedb.org/tv/61709-dragon-ball-z-kai/episode_group/648fc7202f8d0900e3864f62`).
  Copy the Episode Group ID following `/episode_group/`:
  `/episode_group/648fc7202f8d0900e3864f62` $\rightarrow$ `648fc7202f8d0900e3864f62`

### 2. Configure the Series in Jellyfin

1. Open the TV series in your Jellyfin web interface.
2. Click the three dots `...` and select **Edit Metadata**.
3. Set **TheMovieDb Programme Id**:
   `61709`
4. Set **TheMovieDb Show Group Programme Id**:
   `648fc7202f8d0900e3864f62`
5. Click **Save**.

> [!NOTE]
> Depending on your Jellyfin language/locale setting, *Programme Id* may appear as *Series Id*.
> Legacy `tmdb:<episode-group-id>` values remain supported for backward compatibility.

### 3. Refresh Existing Metadata

When configuring or updating ShowOrganizer on an existing series or library folder:

1. Ensure the relevant series or episode metadata fields are not locked in Jellyfin (locked metadata fields prevent Jellyfin from replacing existing metadata).
2. Click `...` on the series, select **Refresh Metadata**, and choose **Replace all metadata**.

### 4. How the Mapping Works

ShowOrganizer uses the season and episode numbering of your Jellyfin library to locate the corresponding episode in the selected TMDb Episode Group.

It then resolves that entry to the canonical TMDb episode and retrieves its metadata, while preserving the season and episode numbering already used by your files and Jellyfin library.

ShowOrganizer does not rename or renumber your files.

> [!NOTE]
> **Provider fallback:** ShowOrganizer follows Jellyfin's standard metadata-provider behavior. If it cannot provide metadata for an item — for example because no Show Group is configured or the episode cannot be mapped — it returns no metadata for that item and Jellyfin can continue with the following configured provider.

## Limitations

* **Season / Saga Artwork**: TMDb Episode Groups define episode orderings and saga subgroup names, but TMDb's Episode Group API does not provide custom subgroup poster artwork. Saga season posters may require local image files (`season01.jpg`, `season02.jpg`) or another image provider.

## Optional: NFO Configuration

If you manage library metadata using local NFO files (`tvshow.nfo`), add the `<showorganizerid>` element to `<tvshow>`:

```xml
<tvshow>
    <title>Dragon Ball Z Kai</title>
    <tmdbid>61709</tmdbid>
    <showorganizerid>648fc7202f8d0900e3864f62</showorganizerid>
</tvshow>
```

> [!NOTE]
> Legacy NFO values formatted as `<showorganizerid>tmdb:648fc7202f8d0900e3864f62</showorganizerid>` remain fully readable.

## Advanced: Custom TMDb API Key

ShowOrganizer **automatically reuses Jellyfin's built-in TMDb credentials**, so no separate TMDb API key is normally required.

If you wish to provide a custom TMDb API key as an advanced override, create `plugins/configurations/Jellyfin.Plugin.ShowOrganizer.xml` under your Jellyfin configuration directory:

```xml
<?xml version="1.0" encoding="utf-8"?>
<PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <TmdbApiKey>YOUR_TMDB_API_KEY</TmdbApiKey>
</PluginConfiguration>
```

## Technical Documentation & Development

For developer details, coordinate mapping invariants, provider return semantics, and architecture, see:
* [Architecture Overview](docs/architecture.md)
* [NFO Format Specification](docs/nfo-format.md)
* [Development Guide](docs/development.md)
* [Release and Packaging Guide](docs/releasing.md)
