using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReleaseVerifier
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: ReleaseVerifier <path-to-zip> <path-to-build-yaml>");
                return 1;
            }

            try
            {
                string zipPath = Path.GetFullPath(args[0]);
                string buildYamlPath = Path.GetFullPath(args[1]);

                Console.WriteLine($"Verifying ZIP: {zipPath}");
                Console.WriteLine($"Using build.yaml: {buildYamlPath}");

                if (!File.Exists(zipPath))
                {
                    Console.WriteLine($"ERROR: Zip file not found at '{zipPath}'");
                    return 1;
                }

                if (!File.Exists(buildYamlPath))
                {
                    Console.WriteLine($"ERROR: build.yaml not found at '{buildYamlPath}'");
                    return 1;
                }

                // 1. Verify ZIP contents
                using var archive = ZipFile.OpenRead(zipPath);
                Console.WriteLine($"Zip contains {archive.Entries.Count} entries.");
                foreach (var entry in archive.Entries)
                {
                    Console.WriteLine($" - {entry.FullName}");
                }

                if (archive.Entries.Count != 2)
                {
                    Console.WriteLine($"ERROR: ZIP should contain exactly 2 files, found {archive.Entries.Count}");
                    return 1;
                }

                var dllEntry = archive.GetEntry("Jellyfin.Plugin.ShowOrganizer.dll");
                var metaEntry = archive.GetEntry("meta.json");

                if (dllEntry == null)
                {
                    Console.WriteLine("ERROR: ZIP does not contain Jellyfin.Plugin.ShowOrganizer.dll");
                    return 1;
                }

                if (metaEntry == null)
                {
                    Console.WriteLine("ERROR: ZIP does not contain meta.json");
                    return 1;
                }

                // 2. Extract DLL to verify version
                string tempDll = Path.Combine(Path.GetTempPath(), $"temp_show_organizer_{Guid.NewGuid()}.dll");
                dllEntry.ExtractToFile(tempDll, true);
                var versionInfo = FileVersionInfo.GetVersionInfo(tempDll);
                string dllVersion = versionInfo.FileVersion ?? "";
                File.Delete(tempDll);

                // 3. Read build.yaml
                string yamlContent = File.ReadAllText(buildYamlPath);
                var match = Regex.Match(yamlContent, @"version:\s*[""']?([^""'\r\n]+)[""']?");
                if (!match.Success)
                {
                    Console.WriteLine("ERROR: Could not parse version from build.yaml");
                    return 1;
                }
                string yamlVersion = match.Groups[1].Value.Trim();

                Console.WriteLine($"DLL FileVersion: '{dllVersion}'");
                Console.WriteLine($"build.yaml version: '{yamlVersion}'");

                if (dllVersion != yamlVersion)
                {
                    Console.WriteLine("ERROR: DLL FileVersion does not match build.yaml version");
                    return 1;
                }

                // 4. Read meta.json
                using var metaStream = metaEntry.Open();
                using var doc = JsonDocument.Parse(metaStream);
                var root = doc.RootElement;

                string metaVersion = root.GetProperty("version").GetString() ?? "";
                if (metaVersion != yamlVersion)
                {
                    Console.WriteLine($"ERROR: meta.json version ({metaVersion}) does not match build.yaml version ({yamlVersion})");
                    return 1;
                }

                // Validate other fields
                var fields = new[] { "name", "guid", "targetAbi", "category", "owner", "imageUrl" };
                foreach (var field in fields)
                {
                    string yamlPattern = $@"{field}:\s*[""']?([^""'\r\n]+)[""']?";
                    var fieldMatch = Regex.Match(yamlContent, yamlPattern);
                    if (!fieldMatch.Success)
                    {
                        Console.WriteLine($"ERROR: build.yaml does not contain field '{field}'");
                        return 1;
                    }
                    string yamlVal = fieldMatch.Groups[1].Value.Trim();
                    string metaVal = root.GetProperty(field).GetString() ?? "";

                    Console.WriteLine($"Validating field '{field}': build.yaml='{yamlVal}', meta.json='{metaVal}'");

                    if (yamlVal != metaVal)
                      {
                        Console.WriteLine($"ERROR: meta.json field '{field}' ({metaVal}) does not match build.yaml ({yamlVal})");
                        return 1;
                    }
                }

                Console.WriteLine("SUCCESS: Package verification passed!");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Verification failed: {ex.Message}");
                return 1;
            }
        }
    }
}
