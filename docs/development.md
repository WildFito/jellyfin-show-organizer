# Development Guide

This guide describes how to compile, test, deploy, and debug the ShowOrganizer plugin locally.

## Prerequisites

* **.NET 9.0 SDK**: Make sure you have the Net 9 SDK installed.
* **Jellyfin NuGet Feeds**: The plugin references official Jellyfin assemblies which are retrieved from the default NuGet package source.

## Building the Project

Run the following command from the repository root to restore and build the solution:

```bash
dotnet restore
dotnet build --configuration Release
```

## Running Tests

Execute the unit and integration tests:

```bash
dotnet test
```

The test suite covers:
* `ShowOrderReference` format parsing.
* Dragon Ball Z Kai Saga Order boundary mapping tests.
* Opt-in provider checks verifying that if the ShowOrganizer ID is missing, no network calls are initiated.

## Local Deployment (Docker/UNRAID)

To install the plugin manually into a Jellyfin server running in a Docker container (such as on UNRAID):

1. Compile the plugin in Release mode.
2. Locate the output assembly file `Jellyfin.Plugin.ShowOrganizer.dll` in `src/Jellyfin.Plugin.ShowOrganizer/bin/Release/net9.0/`.
3. Create a folder named `ShowOrganizer` inside your Jellyfin server's `plugins/` directory:
   ```bash
   mkdir -p /path/to/jellyfin/config/plugins/ShowOrganizer
   ```
4. Copy `Jellyfin.Plugin.ShowOrganizer.dll` into that folder. *(Note: Jellyfin Server bundles TMDbLib at runtime; only `Jellyfin.Plugin.ShowOrganizer.dll` is required).*
5. Restart your Jellyfin container to load the plugin.

## Logging and Troubleshooting

ShowOrganizer uses Jellyfin's standard logging framework (`ILogger`). Operational tracing (per-episode mapping steps, cache hits/misses, coordinate resolution) is logged at `LogLevel.Debug` in the standard Release build.

* **Information**: Emitted for major plugin lifecycle events and once-per-series plugin activation.
* **Warning**: Emitted once per series configuration state for user-correctable configuration issues (e.g. missing TMDb Programme Id, malformed group ID, unsupported provider prefix, or non-existent TMDb Episode Group).
* **Error**: Emitted for unexpected runtime/API/network exceptions.
* **Debug**: Operational tracing per episode invocation (resolution steps, coordinate mapping, unmappable episode details, cache hits/misses).

### Enabling Debug Logging in Jellyfin

To enable detailed Debug logging for ShowOrganizer without changing the global Jellyfin log level, add a category override for `Jellyfin.Plugin.ShowOrganizer` in your Jellyfin server's logging configuration:

```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Jellyfin.Plugin.ShowOrganizer": "Debug"
    }
  }
}
```
