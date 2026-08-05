using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpectreBodies
{
    public sealed class SpectreEntry
    {
        [JsonPropertyName("metadata")] public string Metadata { get; set; } = "";
        [JsonPropertyName("altMetadata")] public List<string> AltMetadata { get; set; } = new();
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
        [JsonPropertyName("tier")] public string Tier { get; set; } = "";
        [JsonPropertyName("phase")] public string Phase { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("source")] public string Source { get; set; } = "";
        [JsonPropertyName("acquisition")] public string Acquisition { get; set; } = "";
        [JsonPropertyName("acquisitionNote")] public string AcquisitionNote { get; set; } = "";
        [JsonPropertyName("note")] public string Note { get; set; } = "";
    }

    public sealed class SpectreDataFile
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("spectres")] public List<SpectreEntry> Spectres { get; set; } = new();
    }

    // Bundled, hand-curated database of world-findable spectres. Loaded once from an
    // embedded resource (Data/spectre-data.json). Fail-soft: any load/parse error leaves
    // an empty database so the plugin keeps working without it.
    public sealed class SpectreDatabase
    {
        private const string ResourceSuffix = ".spectre-data.json";

        private readonly Dictionary<string, SpectreEntry> _byMetadata =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<SpectreEntry> All { get; private set; } = Array.Empty<SpectreEntry>();
        public bool IsLoaded { get; private set; }
        public int Count => _byMetadata.Count;

        public static SpectreDatabase Load()
        {
            var db = new SpectreDatabase();
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var resName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase));
                if (resName == null) return db;

                using var stream = asm.GetManifestResourceStream(resName);
                if (stream == null) return db;

                var file = JsonSerializer.Deserialize<SpectreDataFile>(stream);
                if (file?.Spectres == null || file.Spectres.Count == 0) return db;

                var all = new List<SpectreEntry>(file.Spectres.Count);
                foreach (var entry in file.Spectres)
                {
                    if (string.IsNullOrWhiteSpace(entry.Metadata)) continue;
                    all.Add(entry);
                    Index(db._byMetadata, entry.Metadata, entry);
                    if (entry.AltMetadata != null)
                    {
                        foreach (var alt in entry.AltMetadata)
                            Index(db._byMetadata, alt, entry);
                    }
                }

                db.All = all;
                db.IsLoaded = all.Count > 0;
            }
            catch
            {
                db._byMetadata.Clear();
                db.All = Array.Empty<SpectreEntry>();
                db.IsLoaded = false;
            }
            return db;
        }

        private static void Index(Dictionary<string, SpectreEntry> dict, string key, SpectreEntry entry)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            dict[key] = entry;
        }

        public bool TryLookup(string metadata, out SpectreEntry entry) =>
            _byMetadata.TryGetValue(metadata ?? "", out entry);

        public string DisplayName(string metadata)
        {
            if (!string.IsNullOrEmpty(metadata) && _byMetadata.TryGetValue(metadata, out var entry))
                return entry.Name;
            return metadata ?? "";
        }
    }
}
