using System;
using System.Collections.Generic;
using System.IO;

namespace GemRecommender;

/// <summary>
/// Loads gem CSVs that ship with the plugin.
///
/// Expected format per file (semicolon-delimited, optional header row):
///   Gem_name;Level
///
/// The gem type ("Skill", "Spirit", or "Support") is passed explicitly
/// by the caller based on which file is being loaded.
/// </summary>
public static class GemDatabase
{
    // ── Load ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads a single-type gem CSV (Gem_name;Level) and tags every entry
    /// with <paramref name="gemType"/>.
    /// </summary>
    public static List<GemDatabaseEntry> LoadFromCsv(string filePath, string gemType, out string? error)
    {
        error = null;
        var entries = new List<GemDatabaseEntry>();

        if (!File.Exists(filePath))
        {
            error = $"File not found: {filePath}";
            return entries;
        }

        string[] lines;
        try   { lines = File.ReadAllLines(filePath); }
        catch (Exception ex) { error = ex.Message; return entries; }

        // Detect delimiter from the first non-empty line
        var delimiter = ';';
        foreach (var raw in lines)
        {
            var l = raw.Trim();
            if (string.IsNullOrWhiteSpace(l)) continue;
            delimiter = l.Contains(';') ? ';' : ',';
            break;
        }

        var firstRow = true;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(delimiter);
            if (parts.Length < 2) continue;

            // Skip header row: detected when the level column is non-numeric
            if (firstRow)
            {
                firstRow = false;
                if (!int.TryParse(parts[1].Trim(), out _))
                    continue;
            }

            if (!int.TryParse(parts[1].Trim(), out var level))
                continue;

            entries.Add(new GemDatabaseEntry
            {
                Type    = gemType,
                GemName = parts[0].Trim(),
                Level   = level
            });
        }

        return entries;
    }

    // ── Lookup helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the minimum required level for <paramref name="gemName"/>,
    /// or -1 if the gem is not in the database.
    /// </summary>
    public static int GetMinLevel(List<GemDatabaseEntry> db, string gemName)
    {
        foreach (var entry in db)
            if (string.Equals(entry.GemName, gemName, StringComparison.OrdinalIgnoreCase))
                return entry.Level;
        return -1;
    }

    /// <summary>
    /// Returns true when <paramref name="gemName"/> exists in the database.
    /// </summary>
    public static bool Exists(List<GemDatabaseEntry> db, string gemName)
        => GetMinLevel(db, gemName) >= 0;

    /// <summary>
    /// Validates every gem in a <see cref="BuildDefinition"/> against the
    /// database and returns a list of warning strings for any problems found.
    /// </summary>
    public static List<string> ValidateBuild(BuildDefinition build, List<GemDatabaseEntry> db)
    {
        var warnings = new List<string>();

        foreach (var gem in build.Gems)
        {
            if (string.IsNullOrWhiteSpace(gem.Id))
            {
                warnings.Add($"Gem at index {build.Gems.IndexOf(gem)} has an empty id.");
                continue;
            }

            if (!Exists(db, gem.Id))
            {
                warnings.Add($"'{gem.Id}' - not found in gem database.");
            }

            if (gem.Type.Equals("support", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(gem.TargetSkill))
            {
                warnings.Add($"'{gem.Id}' — support gem is missing 'targetSkill'.");
            }
        }

        return warnings;
    }
}
