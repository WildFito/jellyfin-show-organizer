# Release and Packaging Guide

This document describes the release procedure and packaging automation for ShowOrganizer. All releases are automated using GitHub Actions and the Jellyfin Plugin Repository Manager (JPRM).

## Release Contract

Our release process enforces a strict consistency contract between the metadata version, git tags, and release archives.

* **Authoritative Source**: The version defined in `build.yaml` (under `version: "X.X.X.X"`) is the single source of truth.
* **Tag Naming**: Git tags must be prefixed with a `v` followed by the exact version (e.g. `v0.1.0.0`).
* **Workflow Validation**: The release workflow will fail if the Git tag (without the leading `v`) does not match the version string in `build.yaml`.

---

## Release Procedure

Follow these steps to publish a new release of the plugin:

### 1. Preparation
1. Decide on a new four-part version number (e.g. `0.1.0.0`).
2. Update the `version` property in `build.yaml` to the new version string (e.g. `version: "0.1.0.0"`).
3. Update the `changelog` property in `build.yaml` with concise, user-facing release notes for the new version.
4. Commit and push the changes to `main`:
   ```bash
   git add build.yaml
   git commit -m "Bump version to 0.1.0.0 and update changelog"
   git push origin main
   ```
5. Wait for the standard push CI workflow (`build.yml`) to pass on GitHub.

### 2. Tagging and Publishing
1. Create a local Git tag matching the new version:
   ```bash
   git tag v0.1.0.0
   ```
2. Push the tag to GitHub:
   ```bash
   git push origin v0.1.0.0
   ```
3. Go to your GitHub repository under **Releases -> Draft a new release**:
   * Select the tag you just pushed (`v0.1.0.0`).
   * Set the Release Title to `ShowOrganizer 0.1.0.0`.
   * Add release notes listing major features, bug fixes, and compatibility requirements.
   * Click **Publish release**.

### 3. Automated Execution
Once the release is published, the `.github/workflows/release.yml` workflow triggers:
1. Validates the tag name (`v0.1.0.0`) against the version inside `build.yaml` (`0.1.0.0`).
2. Compiles and runs all unit tests in Release mode.
3. Packages the runtime files using JPRM (`showorganizer_0.1.0.0.zip` containing only `Jellyfin.Plugin.ShowOrganizer.dll` and `meta.json`).
4. Uploads the zip archive to the newly created GitHub Release assets.
5. Updates the repository catalog `manifest.json` with the new version entry.
6. Commits and pushes the updated `manifest.json` back to `main` as `github-actions[bot]`.

### 4. Verification
1. Verify that `manifest.json` on `main` contains the new release entry with the correct download URL, checksum, and timestamp.
2. Confirm the plugin installs successfully via the Jellyfin catalog using the repository URL:
   `https://raw.githubusercontent.com/WildFito/jellyfin-show-organizer/main/manifest.json`

---

## Recovering from Release Failures

If the release workflow fails midway (e.g. due to build error, tag mismatch, or network issue):

1. **Delete the GitHub Release**: Go to GitHub Releases, select the failed release, and click **Delete**.
2. **Delete the Git Tag**:
   * Delete locally: `git tag -d v0.1.0.0`
   * Delete on remote: `git push origin --delete v0.1.0.0`
3. **Fix the Issue**: Commit any code fixes to `main`.
4. **Retry**: Redo the tagging and publishing steps above.
