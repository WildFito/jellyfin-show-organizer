# NFO File Format Configuration

ShowOrganizer registers a unique external identifier with Jellyfin under the key `ShowOrganizer`. 

## Series Metadata (tvshow.nfo)

The exact episode group ID is defined at the **Series level** in your `tvshow.nfo` file. You do not need to add it to season or episode NFO files.

### XML Field

Add the `<showorganizerid>` tag to the `<tvshow>` XML element:

```xml
<tvshow>
    <title>Dragon Ball Z Kai</title>
    <tmdbid>61709</tmdbid>
    <showorganizerid>648fc7202f8d0900e3864f62</showorganizerid>
</tvshow>
```

Alternatively, it can be defined using Jellyfin's standard `<uniqueid>` tag format:

```xml
<tvshow>
    <title>Dragon Ball Z Kai</title>
    <uniqueid type="tmdb">61709</uniqueid>
    <uniqueid type="ShowOrganizer">648fc7202f8d0900e3864f62</uniqueid>
</tvshow>
```

## How ShowOrganizer Accesses IDs

ShowOrganizer does not parse the XML NFO files directly. Instead, it relies on Jellyfin's native NFO parser to load these values into the series `ProviderIds` collection.

During metadata refreshes:
* TMDB series ID is read from `Series.ProviderIds["Tmdb"]`.
* ShowOrganizer mapping config is read from `Series.ProviderIds["ShowOrganizer"]`.

### Value Format

The external ID accepts either raw TMDb Episode Group IDs or prefix-qualified references:

```
648fc7202f8d0900e3864f62
```

Supported format:
* TMDb Group ID (e.g. `648fc7202f8d0900e3864f62` or `tmdb:648fc7202f8d0900e3864f62`)
