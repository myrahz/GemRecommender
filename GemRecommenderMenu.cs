using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace GemRecommender;

public class GemRecommenderMenu(
    GemRecommenderSettings                          settings,
    Func<BuildDefinition?>                          getBuild,
    Func<List<GemDatabaseEntry>>                    getDatabase,
    Func<List<string>>                              getWarnings,
    Func<string, List<(string Name, string Path)>>  scanForDotBuildFiles,
    Func<string[], string?>                         importDotBuildFiles,
    Func<BuildDefinition, string?>                  saveBuildDefinition,
    Func<string, string?>                           renameBuild,
    Func<string?>                                   duplicateBuild)
{
    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly Vector4 ColGold    = new(1.00f, 0.75f, 0.20f, 1f);
    private static readonly Vector4 ColSkill   = new(0.35f, 0.65f, 1.00f, 1f);
    private static readonly Vector4 ColSupport = new(0.40f, 0.90f, 0.45f, 1f);
    private static readonly Vector4 ColSpirit  = new(0.80f, 0.45f, 1.00f, 1f);
    private static readonly Vector4 ColNone    = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Vector4 ColWarn    = new(0.95f, 0.70f, 0.20f, 1f);
    private static readonly Vector4 ColErr     = new(0.95f, 0.30f, 0.30f, 1f);
    private static readonly Vector4 ColDelBtn  = new(0.55f, 0.10f, 0.10f, 1f);
    private static readonly Vector4 ColDelBtnH = new(0.80f, 0.20f, 0.20f, 1f);

    private static Vector4 TypeColor(string? type) => type?.ToLowerInvariant() switch
    {
        "skill"   => ColSkill,
        "support" => ColSupport,
        "spirit"  => ColSpirit,
        _         => ColNone
    };

    // ── Import state ──────────────────────────────────────────────────────────
    private bool   _scanFolderPathInit = false;
    private string _scanFolderPath     = "";
    private List<(string Name, string Path)> _scannedBuildFiles = [];
    private bool[] _selectedBuildMask  = [];
    private string? _importStatus;
    private bool    _importSuccess;

    // ── Build editor state ────────────────────────────────────────────────────
    private List<GemEntry>? _editorGems;
    private string  _editorBuildName     = "";
    private string  _editorLastBuildName = "\0"; // force first load
    private string? _editorSaveError;
    private string? _renameStatus;
    private bool    _renameSuccess;
    private string? _duplicateStatus;
    private bool    _duplicateSuccess;
    private string  _gemPickerSearch = ""; // shared; only one combo open at a time

    // ── Entry point ───────────────────────────────────────────────────────────

    public void DrawConfiguration()
    {
        var database = getDatabase();

        DrawImportBuild();
        ImGui.Spacing();

        DrawBuildEditor();
        ImGui.Spacing();

        DrawDatabaseBrowser(database);
    }

    // ── .build folder scanner ─────────────────────────────────────────────────

    private void DrawImportBuild()
    {
        ImGui.TextColored(ColGold, "Import .build File(s)");
        ImGui.Separator();

        if (!_scanFolderPathInit)
        {
            _scanFolderPath    = settings.BuildsFolderPath.Value ?? "Builds";
            _scanFolderPathInit = true;
        }

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 74f);
        ImGui.InputText("##scanfolder", ref _scanFolderPath, 512);
        ImGui.SameLine();
        if (ImGui.Button("Scan##scanbtn"))
        {
            _scannedBuildFiles = scanForDotBuildFiles(_scanFolderPath);
            _selectedBuildMask = new bool[_scannedBuildFiles.Count];
            _importStatus      = _scannedBuildFiles.Count == 0 ? "No .build files found." : null;
            _importSuccess     = false;
        }

        if (_scannedBuildFiles.Count > 0)
        {
            ImGui.Spacing();
            for (var i = 0; i < _scannedBuildFiles.Count; i++)
            {
                var sel = _selectedBuildMask[i];
                if (ImGui.Checkbox(_scannedBuildFiles[i].Name + "##scan" + i, ref sel))
                    _selectedBuildMask[i] = sel;
            }

            ImGui.Spacing();
            var selectedCount = _selectedBuildMask.Count(b => b);
            if (selectedCount == 0) ImGui.BeginDisabled();
            if (ImGui.Button(selectedCount > 0
                    ? $"Import Selected ({selectedCount})##importsel"
                    : "Import Selected##importsel"))
            {
                var paths      = _scannedBuildFiles
                    .Where((_, i) => _selectedBuildMask[i])
                    .Select(f => f.Path)
                    .ToArray();
                var error      = importDotBuildFiles(paths);
                _importSuccess = error == null;
                _importStatus  = error ?? $"Imported {paths.Length} file(s) successfully.";
                if (_importSuccess) _editorLastBuildName = "\0"; // force editor reload
            }
            if (selectedCount == 0) ImGui.EndDisabled();
        }

        if (_importStatus != null)
        {
            ImGui.SameLine();
            ImGui.TextColored(_importSuccess ? ColSupport : ColErr, _importStatus);
        }
    }


    // ── Build editor ──────────────────────────────────────────────────────────

    // Subtle per-type background tint used on each gem card.
    private static uint TypeBgColor(string? type)
    {
        var c = type?.ToLowerInvariant() switch
        {
            "skill"   => new Vector4(0.35f, 0.65f, 1.00f, 0.10f),
            "support" => new Vector4(0.40f, 0.90f, 0.45f, 0.10f),
            "spirit"  => new Vector4(0.80f, 0.45f, 1.00f, 0.10f),
            _         => new Vector4(0f,    0f,    0f,    0f),
        };
        return ImGui.ColorConvertFloat4ToU32(c);
    }

    private void DrawBuildEditor()
    {
        if (!ImGui.CollapsingHeader("Build Editor##buildeditor"))
            return;

        ImGui.Indent(10f);
        ImGui.Spacing();

        // Build validation warnings
        var warnings = getWarnings();
        foreach (var w in warnings)
            ImGui.TextColored(ColWarn, $"  [!] {w}");

        // Reload editor state whenever the active build switches
        var currentName = settings.SelectedBuild.Value ?? "";
        if (_editorGems == null || currentName != _editorLastBuildName)
            LoadEditorState(currentName);

        // ── Rename / Duplicate row ────────────────────────────────────────────
        ImGui.Text("File name:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180f);
        ImGui.InputText("##buildname", ref _editorBuildName, 128);
        ImGui.SameLine();
        if (ImGui.Button("Rename##renamebuild"))
        {
            var err        = renameBuild(_editorBuildName);
            _renameSuccess = err == null;
            _renameStatus  = err ?? "Renamed.";
            if (_renameSuccess) _editorLastBuildName = _editorBuildName;
        }
        ImGui.SameLine();
        if (ImGui.Button("Duplicate##dupbuild"))
        {
            var err           = duplicateBuild();
            _duplicateSuccess = err == null;
            _duplicateStatus  = err ?? "Duplicated.";
            if (_duplicateSuccess) _editorLastBuildName = "\0"; // reload into new copy
        }

        var statusLine = _renameStatus != null
            ? (_renameStatus, _renameSuccess)
            : _duplicateStatus != null
                ? (_duplicateStatus, _duplicateSuccess)
                : (null, false);
        if (statusLine.Item1 != null)
        {
            ImGui.SameLine();
            ImGui.TextColored(statusLine.Item2 ? ColSupport : ColErr, statusLine.Item1);
        }

        ImGui.Separator();
        ImGui.Spacing();

        // ── Gem list (auto-saves when anything changes) ───────────────────────
        var gems = _editorGems!;
        int toMoveUp = -1, toMoveDown = -1, toDelete = -1;
        var anyChanged = false;

        for (var i = 0; i < gems.Count; i++)
        {
            anyChanged |= DrawGemEntry(i, gems.Count, ref toMoveUp, ref toMoveDown, ref toDelete);
            if (i < gems.Count - 1) ImGui.Separator();
        }

        // Apply list mutations after the loop
        if (toDelete >= 0)                                   { gems.RemoveAt(toDelete); anyChanged = true; }
        if (toMoveUp > 0)                                    { (gems[toMoveUp], gems[toMoveUp - 1])         = (gems[toMoveUp - 1],     gems[toMoveUp]);     anyChanged = true; }
        if (toMoveDown >= 0 && toMoveDown < gems.Count - 1) { (gems[toMoveDown], gems[toMoveDown + 1])     = (gems[toMoveDown + 1],   gems[toMoveDown]);   anyChanged = true; }

        ImGui.Spacing();
        if (ImGui.Button("+ Add Gem##addgem")) { gems.Add(new GemEntry { Id = "", Type = "skill" }); anyChanged = true; }

        // Auto-save on any change
        if (anyChanged)
        {
            var err = saveBuildDefinition(new BuildDefinition { Gems = [.. gems] });
            _editorSaveError = err; // null = success, shown only on error
        }

        if (_editorSaveError != null)
        {
            ImGui.Spacing();
            ImGui.TextColored(ColErr, $"Save error: {_editorSaveError}");
        }

        ImGui.Unindent(10f);
    }

    private void LoadEditorState(string buildName)
    {
        var build = getBuild();
        _editorGems = build?.Gems.Select(g => new GemEntry
        {
            Id                         = g.Id,
            Type                       = g.Type,
            RequiredGemLevel           = g.RequiredGemLevel,
            MaxGemLevel                = g.MaxGemLevel,
            MinCharLevel               = g.MinCharLevel,
            MaxCharLevel               = g.MaxCharLevel,
            TargetSkill                = g.TargetSkill,
            DisplayName                = g.DisplayName,
            PrioritizeOverEmptySockets = g.PrioritizeOverEmptySockets,
        }).ToList() ?? [];
        _editorBuildName     = buildName;
        _editorLastBuildName = buildName;
        _editorSaveError     = null;
        _renameStatus        = null;
        _duplicateStatus     = null;
    }

    // ── Single gem card ───────────────────────────────────────────────────────

    // Returns true if any property was changed this frame.
    private bool DrawGemEntry(int i, int total, ref int toMoveUp, ref int toMoveDown, ref int toDelete)
    {
        var gem     = _editorGems![i];
        var changed = false;

        // Split draw list into background (ch 0) and foreground (ch 1) so the
        // tinted rect can be painted behind all widgets without a second pass.
        var dl        = ImGui.GetWindowDrawList();
        var cardStart = ImGui.GetCursorScreenPos();
        var cardW     = ImGui.GetContentRegionAvail().X;
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1); // widgets drawn on ch 1

        // ── Row 1: move / type / gem picker / level badge / delete ────────────

        if (i == 0) ImGui.BeginDisabled();
        if (ImGui.ArrowButton("##up_" + i, ImGuiDir.Up))   toMoveUp = i;
        if (i == 0) ImGui.EndDisabled();

        ImGui.SameLine();

        if (i == total - 1) ImGui.BeginDisabled();
        if (ImGui.ArrowButton("##dn_" + i, ImGuiDir.Down)) toMoveDown = i;
        if (i == total - 1) ImGui.EndDisabled();

        ImGui.SameLine();

        // Type combo
        var types   = new[] { "skill", "spirit", "support" };
        var typeIdx = Array.IndexOf(types, gem.Type?.ToLowerInvariant() ?? "skill");
        if (typeIdx < 0) typeIdx = 0;
        ImGui.SetNextItemWidth(82f);
        if (ImGui.Combo("##type_" + i, ref typeIdx, types, types.Length))
        {
            gem.Type = types[typeIdx];
            if (gem.Type != "support") gem.TargetSkill = null;
            changed = true;
        }

        ImGui.SameLine();

        // Gem picker — BeginCombo with inline search box
        var comboLabel = string.IsNullOrEmpty(gem.Id) ? "(select gem)" : gem.Label;
        ImGui.SetNextItemWidth(210f);
        if (ImGui.BeginCombo("##gempick_" + i, comboLabel))
        {
            if (ImGui.IsWindowAppearing())
            {
                _gemPickerSearch = "";
                ImGui.SetKeyboardFocusHere();
            }
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("##psearch_" + i, ref _gemPickerSearch, 64);
            ImGui.Separator();
            if (ImGui.BeginChild("##pscroll_" + i, new Vector2(0f, 200f)))
            {
                foreach (var entry in getDatabase())
                {
                    // Show only gems whose type matches the current slot type.
                    if (!string.IsNullOrEmpty(gem.Type) &&
                        !entry.Type.Equals(gem.Type, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!string.IsNullOrEmpty(_gemPickerSearch) &&
                        !entry.GemName.Contains(_gemPickerSearch, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var isCurrent = entry.GemName.Equals(gem.Id, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(entry.GemName + "##gpi_" + i + "_" + entry.GemName, isCurrent))
                    {
                        gem.Id   = entry.GemName;
                        if (!string.IsNullOrEmpty(entry.Type))
                            gem.Type = entry.Type.ToLowerInvariant();
                        changed = true;
                        ImGui.CloseCurrentPopup();
                    }
                    if (isCurrent) ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndChild();
            ImGui.EndCombo();
        }

        ImGui.SameLine();

        // Gem level badge
        ImGui.TextColored(TypeColor(gem.Type), $"Lv.{gem.RequiredGemLevel ?? 1}");

        ImGui.SameLine();

        // Delete button
        ImGui.PushStyleColor(ImGuiCol.Button,        ColDelBtn);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColDelBtnH);
        if (ImGui.SmallButton(" × ##del_" + i)) toDelete = i;
        ImGui.PopStyleColor(2);

        // ── Row 2: target skill (support only) ────────────────────────────────

        if (gem.Type?.Equals("support", StringComparison.OrdinalIgnoreCase) == true)
        {
            var skillIds = _editorGems
                .Where(g => g != gem &&
                            (g.Type.Equals("skill",   StringComparison.OrdinalIgnoreCase) ||
                             g.Type.Equals("spirit",  StringComparison.OrdinalIgnoreCase)) &&
                            !string.IsNullOrEmpty(g.Id))
                .Select(g => g.Id)
                .ToArray();

            ImGui.Indent(50f);
            ImGui.Text("> Target:");
            ImGui.SameLine();

            if (skillIds.Length == 0)
            {
                ImGui.TextColored(ColErr, "(no skill/spirit gems in build yet)");
            }
            else
            {
                var targetIdx = Array.IndexOf(skillIds, gem.TargetSkill ?? "");
                if (targetIdx < 0) { targetIdx = 0; gem.TargetSkill = skillIds[0]; changed = true; }
                ImGui.SetNextItemWidth(210f);
                if (ImGui.Combo("##target_" + i, ref targetIdx, skillIds, skillIds.Length))
                {
                    gem.TargetSkill = skillIds[targetIdx];
                    changed = true;
                }
            }
            ImGui.Unindent(50f);
        }

        // ── Optional fields ───────────────────────────────────────────────────

        ImGui.Indent(50f);

        if (gem.RequiredGemLevel.HasValue)
        {
            var v = gem.RequiredGemLevel.Value;
            ImGui.Text("Req Gem Lv:"); ImGui.SameLine();
            ImGui.SetNextItemWidth(90f);
            if (ImGui.InputInt("##rgl_" + i, ref v, 0, 0)) { gem.RequiredGemLevel = Math.Clamp(v, 1, 21); changed = true; }
            ImGui.SameLine();
            if (ImGui.SmallButton("×##rgl_x_" + i)) { gem.RequiredGemLevel = null; changed = true; }
        }

        if (gem.MaxGemLevel.HasValue)
        {
            var v = gem.MaxGemLevel.Value;
            ImGui.Text("Max Gem Lv:"); ImGui.SameLine();
            ImGui.SetNextItemWidth(90f);
            if (ImGui.InputInt("##mgl_" + i, ref v, 0, 0)) { gem.MaxGemLevel = Math.Clamp(v, 1, 21); changed = true; }
            ImGui.SameLine();
            if (ImGui.SmallButton("×##mgl_x_" + i)) { gem.MaxGemLevel = null; changed = true; }
        }

        if (gem.MinCharLevel > 0)
        {
            var v = gem.MinCharLevel;
            ImGui.Text("Min Char Lv:"); ImGui.SameLine();
            ImGui.SetNextItemWidth(90f);
            if (ImGui.InputInt("##mcl_" + i, ref v, 0, 0)) { gem.MinCharLevel = Math.Clamp(v, 0, 100); changed = true; }
            ImGui.SameLine();
            if (ImGui.SmallButton("×##mcl_x_" + i)) { gem.MinCharLevel = 0; changed = true; }
        }

        if (gem.MaxCharLevel.HasValue)
        {
            var v = gem.MaxCharLevel.Value;
            ImGui.Text("Max Char Lv:"); ImGui.SameLine();
            ImGui.SetNextItemWidth(90f);
            if (ImGui.InputInt("##xcl_" + i, ref v, 0, 0)) { gem.MaxCharLevel = Math.Clamp(v, 1, 100); changed = true; }
            ImGui.SameLine();
            if (ImGui.SmallButton("×##xcl_x_" + i)) { gem.MaxCharLevel = null; changed = true; }
        }

        if (gem.DisplayName != null)
        {
            var v = gem.DisplayName;
            ImGui.Text("Display Name:"); ImGui.SameLine();
            ImGui.SetNextItemWidth(180f);
            if (ImGui.InputText("##dn_" + i, ref v, 128)) { gem.DisplayName = v; changed = true; }
            ImGui.SameLine();
            if (ImGui.SmallButton("×##dn_x_" + i)) { gem.DisplayName = null; changed = true; }
        }

        if (gem.PrioritizeOverEmptySockets)
        {
            var v = gem.PrioritizeOverEmptySockets;
            if (ImGui.Checkbox("Prioritize over empty sockets##poes_" + i, ref v))
            {
                gem.PrioritizeOverEmptySockets = v;
                changed = true;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("×##poes_x_" + i)) { gem.PrioritizeOverEmptySockets = false; changed = true; }
        }

        // Add Field combo
        var available = GetAvailableFields(gem);
        if (available.Count > 0)
        {
            ImGui.SetNextItemWidth(150f);
            if (ImGui.BeginCombo("##addf_" + i, "+ Add Field"))
            {
                foreach (var field in available)
                {
                    if (ImGui.Selectable(field + "##af_" + i + "_" + field))
                    {
                        SetDefaultForField(gem, field);
                        changed = true;
                    }
                }
                ImGui.EndCombo();
            }
        }

        ImGui.Unindent(50f);

        // Paint the type-tinted background behind all widgets drawn on ch 1.
        var cardEnd = ImGui.GetCursorScreenPos();
        dl.ChannelsSetCurrent(0);
        dl.AddRectFilled(
            cardStart - new Vector2(4f, 2f),
            new Vector2(cardStart.X + cardW + 4f, cardEnd.Y + 2f),
            TypeBgColor(gem.Type),
            4f);
        dl.ChannelsMerge();

        return changed;
    }

    private static List<string> GetAvailableFields(GemEntry gem)
    {
        var fields = new List<string>();
        if (!gem.RequiredGemLevel.HasValue)    fields.Add("RequiredGemLevel");
        if (!gem.MaxGemLevel.HasValue)         fields.Add("MaxGemLevel");
        if (gem.MinCharLevel == 0)             fields.Add("MinCharLevel");
        if (!gem.MaxCharLevel.HasValue)        fields.Add("MaxCharLevel");
        if (gem.DisplayName == null)           fields.Add("DisplayName");
        if (!gem.PrioritizeOverEmptySockets)   fields.Add("PrioritizeOverEmptySockets");
        return fields;
    }

    private static void SetDefaultForField(GemEntry gem, string field)
    {
        switch (field)
        {
            case "RequiredGemLevel":           gem.RequiredGemLevel          = 1;      break;
            case "MaxGemLevel":                gem.MaxGemLevel               = 20;     break;
            case "MinCharLevel":               gem.MinCharLevel              = 1;      break;
            case "MaxCharLevel":               gem.MaxCharLevel              = 60;     break;
            case "DisplayName":                gem.DisplayName               = gem.Id; break;
            case "PrioritizeOverEmptySockets": gem.PrioritizeOverEmptySockets = true;  break;
        }
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
            ImGui.TextColored(TypeColor(entry.Type), $"[{entry.Type[0]}]");
            ImGui.SameLine();
            ImGui.Text($"{entry.GemName}  (min Lv.{entry.Level})");
            if (entry.IsLineage)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.8f, 0.6f, 1.0f, 1f), "[Lineage]");
            }
            if (entry.ItemOnly)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1f), "[ItemOnly]");
            }
            if (!string.IsNullOrEmpty(entry.Tags))
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"  {entry.Tags}");
            }
        }

        if (database.Count == 0)
            ImGui.TextColored(ColNone, "No gems loaded — check the CSV paths in settings.");

        ImGui.Unindent(10f);
    }
}
