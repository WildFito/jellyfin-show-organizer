# Release and Packaging Guide

This document describes the manual-dispatch, validate-first release procedure for ShowOrganizer. All release builds, package validations, and deployment updates are automated via GitHub Actions using the `.github/workflows/release.yml` workflow.

---

## Release Contract

Our release process enforces a strict consistency contract between the metadata version, git tags, and release archives.

* **Authoritative Source**: The version defined in `build.yaml` (under `version: "X.X.X.X"`) is the single source of truth.
* **Tag Naming**: Git tags must be prefixed with a `v` followed by the exact version (e.g., `v0.1.0.0`).
* **Validation Order**: The workflow validates the package contents, build, and tests *before* creating any Git tags or GitHub Releases.

> [!IMPORTANT]
> * **DO NOT** manually create the Git tag first.
> * **DO NOT** manually create the GitHub Release first.
> The release workflow handles tagging and release creation automatically only after all validation steps pass successfully.

---

## Human Release Procedure

Follow these steps to publish a new release of the plugin:

### 1. Update Version and Notes
1. Choose a new four-part version number (e.g. `0.1.0.0`).
2. Open `build.yaml` and update the `version` property to the new string:
   ```yaml
   version: "0.1.0.0"
   ```
3. Update the `changelog` property in `build.yaml` with user-facing release notes.
4. Commit and push your changes to `main`:
   ```bash
   git add build.yaml
   git commit -m "Bump version to 0.1.0.0 and update changelog"
   git push origin main
   ```
5. Wait for the standard push/PR CI workflow (`build.yml`) to pass on GitHub.

### 2. Trigger the Release Workflow
1. Go to the **Actions** tab on your GitHub repository.
2. Select the **Release ShowOrganizer** workflow on the left sidebar.
3. Click the **Run workflow** dropdown on the right side.
4. Select branch **main** and click **Run workflow**.

### 3. Automated Validation and Tagging
The manual release workflow runs on GitHub runners and executes the following steps:
1. **Pre-flight Check**: Verifies that the workflow is running on the `main` branch and that the tag `vX.X.X.X` (and corresponding GitHub Release) does not already exist.
2. **Build and Test**: Runs `dotnet build` and `dotnet test` in Release mode.
3. **Packaging**: Invokes JPRM 1.1.0 to package the plugin.
4. **Verification**: Runs the custom `ReleaseVerifier` tool on the packaged ZIP to ensure it contains only `Jellyfin.Plugin.ShowOrganizer.dll` and `meta.json` (no PDBs, host DLLs, or source files), and validates that all metadata aligns with `build.yaml`.
5. **Git Tagging**: Creates an annotated tag `vX.X.X.X` pointing to the exact checkout SHA and pushes it to origin.
6. **GitHub Release**: Creates the GitHub Release, attaches the changelog as notes, and uploads the JPRM ZIP file as a release asset.
7. **Manifest Update**: Uses JPRM to update `manifest.json` with the new version details and pushes the update back to `main`.

### 4. Post-Release Verification
1. Verify that `manifest.json` on `main` contains the new release entry with the correct download URL, checksum, and timestamp.
2. Confirm the plugin installs successfully via the Jellyfin catalog using the repository URL:
   `https://raw.githubusercontent.com/WildFito/jellyfin-show-organizer/main/manifest.json`

---

## Recovering from Release Failures

### Failures Before Tagging
If any step fails before tag creation (such as build compilation, unit tests, or ZIP validation), the workflow stops immediately. 
* **State**: No tags are pushed, no GitHub Releases are created, and `manifest.json` remains untouched.
* **Recovery**: Simply commit the necessary code fixes to `main` and trigger the workflow again.

### Failures After Tagging
If tagging succeeds but subsequent release publishing or manifest updating fails:
* **State**: The Git tag `vX.X.X.X` exists on origin, but the GitHub Release or `manifest.json` update is missing or incomplete.
* **Important Rule**: Once a version is tagged and validated, the release binary is immutable. Do not delete or reuse tag versions on different code.
* **Recovery**:
  1. Manually create the GitHub Release from the pushed tag and upload the generated `showorganizer_X.X.X.X.zip` asset.
  2. Manually run JPRM to update `manifest.json` locally and commit/push it to `main`:
     ```bash
     python -m jprm repo add --plugin-url "https://github.com/WildFito/jellyfin-show-organizer/releases/download/vX.X.X.X/showorganizer_X.X.X.X.zip" . "artifacts/showorganizer_X.X.X.X.zip"
     git add manifest.json
     git commit -m "Manual manifest update for failed release vX.X.X.X"
     git push origin main
     ```
