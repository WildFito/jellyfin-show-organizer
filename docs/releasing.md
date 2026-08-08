# Release and Packaging Guide

This guide details the release process, packaging standards, and repository manifest management for the ShowOrganizer plugin.

## Configuration Profiles

### build.yaml
The `build.yaml` file defines the plugin identity, target environments, and build rules. It is located at the root of the repository and is consumed by build tools like the Jellyfin Plugin Repository Manager (JPRM).

Important properties:
* **name**: "ShowOrganizer"
* **guid**: Must remain stable (`f98bb2d0-ea65-4f36-be5d-ff63d7d7b1d1`) so that user updates do not break.
* **targetAbi**: Set to `10.11.0.0` to target Jellyfin v10.11 server environments.

### manifest.json
The `manifest.json` file is a manifest catalog listing all available releases of the plugin. This is hosted in a public location and configured in the Jellyfin Dashboard under **Plugins -> Repositories** so users can install and update the plugin directly from the catalog.

## Packaging Releases (JPRM)

To package a new release:

1. Update the version number in `build.yaml` and the assembly file.
2. Use JPRM to package the binaries:
   ```bash
   jprm pack
   ```
3. This creates a release zip archive containing the plugin dlls and generates/updates the catalog metadata.
4. Update the repository `manifest.json` with the new release entry, which includes:
   * Plugin GUID and version.
   * Target server ABI version (`10.11.0.0`).
   * Source download URL.
   * SHA-256 checksum.
   * Changelog.
