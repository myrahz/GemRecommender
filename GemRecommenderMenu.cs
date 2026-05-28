using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace GemRecommender;

public class GemRecommenderMenu(
    GemRecommenderSettings          settings,
    Func<BuildDefinition?>          getBuild,
    Func<List<GemDatabaseEntry>>    getDatabase,
    Func<List<string>>              getWarnings)
{
    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly Vector4 ColGold    = new(1.00f, 0.75f, 0.20f, 1f);
    private static readonly Vector4 ColSkill   = new(0.35f, 0.65f, 1.00f, 1f);
    private static readonly Vector4 ColSupport = new(0.40f, 0.90f, 0.45f, 1f);
    private static readonly Vector4 ColSpirit  = new(0.80f, 0.45f, 1.00f, 1f);
    private static readonly Vector4 ColNone    = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Vector4 ColWarn    = new(0.95f, 0.70f, 0.20f, 1f);
    private static readonly Vector4 ColErr     = new(0.95f, 0.30f, 0.30f, 1f);

    private static Vector4 TypeColor(string? type) => type?.ToLowerInvariant() switch
    {
        "skill"   => ColSkill,
        "support" => ColSupport,
        "spirit"  => ColSpirit,
        _         => ColNone
    };

    // ── Entry point ───────────────────────────────────────────────────────────

    public void DrawConfiguration()
    {
        var build    = getBuild();
        var database = getDatabase();
        var warnings = getWarnings();

        DrawDebugControls();
        ImGui.Spacing();

        DrawBuildOrder(build, warnings);
        ImGui.Spacing();

        DrawDatabaseBrowser(database);
    }

    // ── Debug controls ────────────────────────────────────────────────────────

    private void DrawDebugControls()
    {
        ImGui.TextColored(ColGold, "Debug Simulation");
        ImGui.Separator();

        // Gem simulation toggle
        var gemActive = settings.DebugGemMode.Value;
        if (gemActive) ImGui.PushStyleColor(ImGuiCol.Button, ColWarn);
        if (ImGui.Button(gemActive ? "Gem Sim: ON##dbggem" : "Gem Sim: OFF##dbggem"))
            settings.DebugGemMode.Value = !gemActive;
        if (gemActive) ImGui.PopStyleColor();

        if (gemActive)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColWarn,
                $"  {settings.DebugUncutGemType.Value} Lv.{settings.DebugUncutGemLevel.Value}");
        }

        ImGui.SameLine();
        ImGui.Spacing();

        // Player level simulation toggle
        var lvlActive = settings.DebugPlayerLevelMode.Value;
        if (lvlActive) ImGui.PushStyleColor(ImGuiCol.Button, ColWarn);
        if (ImGui.Button(lvlActive ? "Lvl Sim: ON##dbglvl" : "Lvl Sim: OFF##dbglvl"))
            settings.DebugPlayerLevelMode.Value = !lvlActive;
        if (lvlActive) ImGui.PopStyleColor();

        if (lvlActive)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColWarn, $"  Player Lv.{settings.DebugPlayerLevel.Value}");
        }
    }

    // ── Build order ───────────────────────────────────────────────────────────

    private void DrawBuildOrder(BuildDefinition? build, List<string> warnings)
    {
        ImGui.TextColored(ColGold, "Build Gem Order");
        ImGui.Separator();

        if (warnings.Count > 0)
        {
            foreach (var w in warnings)
                ImGui.TextColored(ColWarn, $"  [!] {w}");
            ImGui.Spacing();
        }

        if (build == null)
        {
            ImGui.TextColored(ColErr, "No build loaded.");
            return;
        }

        ImGui.TextColored(ColNone, "Gems are recommended in this order (top = highest priority):");
        ImGui.Spacing();

        for (var i = 0; i < build.Gems.Count; i++)
        {
            var g = build.Gems[i];
            var conditions = BuildConditionString(g);

            ImGui.TextColored(TypeColor(g.Type),
                $"  {i + 1,2}.  [{g.Type.ToUpperInvariant()[0]}]  {g.Label}  (gem Lv.{g.RequiredGemLevel ?? 1})");

            if (conditions.Length > 0)
                ImGui.TextColored(ColNone, $"        {conditions}");

            if (g.Type.Equals("support", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(g.TargetSkill))
            {
                ImGui.TextColored(ColNone, $"        → {g.TargetSkill}");
            }
        }
    }

    private static string BuildConditionString(GemEntry g)
    {
        var parts = new List<string>();
        if (g.MinCharLevel > 0)
            parts.Add($"char ≥ {g.MinCharLevel}");
        if (g.MaxCharLevel.HasValue)
            parts.Add($"char ≤ {g.MaxCharLevel}");
        if (g.MaxGemLevel.HasValue)
            parts.Add($"max gem Lv.{g.MaxGemLevel}");
        return parts.Count > 0 ? string.Join("  ·  ", parts) : "";
    }

    // ── Database browser ──────────────────────────────────────────────────────

    private void DrawDatabaseBrowser(List<GemDatabaseEntry> database)
    {
        if (!ImGui.CollapsingHeader($"Gem Database ({database.Count} entries)##db"))
            return;

        ImGui.Indent(10f);
        ImGui.Spacing();

        foreach (var entry in database)
        {
            ImGui.TextColored(TypeColor(entry.Type),
                $"[{entry.Type[0]}]  {entry.GemName}  (min Lv.{entry.Level})");
        }

        if (database.Count == 0)
            ImGui.TextColored(ColNone, "No gems loaded — check the CSV paths in settings.");

        ImGui.Unindent(10f);
    }
}
