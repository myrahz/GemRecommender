using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using ExileCore2;
using ExileCore2.PoEMemory.Components;
using ImGuiNET;
using Newtonsoft.Json;

namespace GemRecommender;

public class GemRecommender : BaseSettingsPlugin<GemRecommenderSettings>
{
    // ── State ─────────────────────────────────────────────────────────────────

    private BuildDefinition?       _build;
    private List<GemDatabaseEntry> _database = [];
    private List<string>           _warnings = [];
    private string?                _loadError;
    private GemRecommenderMenu?    _menu;

    private List<(string DisplayName, string FullPath)> _buildFiles = [];
    private string? _lastSelectedBuild;

    private record RecommendationSlot(
        GemEntry          BuildGem,
        UncutGemInfo?     UncutToUse,           // null when equipping from inventory
        bool              IsUpgrade,
        int               CurrentEquippedLevel,
        string?           SupportToRemove,
        InventoryGemInfo? InventoryGem = null);  // non-null when equipping from inventory

    private List<RecommendationSlot> _recommendations = [];
    private int _recommendationIndex = 0;

    private static readonly Regex UncutGemRegex = new(
        @"^Uncut (Skill|Support|Spirit) Gem \(Level (\d+)\)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── ImGui overlay flags ───────────────────────────────────────────────────
    // NoInputs is intentionally absent so navigation arrows and combo can be clicked.

    private const ImGuiWindowFlags OverlayFlags =
          ImGuiWindowFlags.NoTitleBar
        | ImGuiWindowFlags.NoResize
        | ImGuiWindowFlags.NoMove
        | ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoScrollWithMouse
        | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoCollapse
        | ImGuiWindowFlags.NoBringToFrontOnFocus
        | ImGuiWindowFlags.NoFocusOnAppearing;

    // ── Gem-type colours ──────────────────────────────────────────────────────

    private static readonly Vector4 ColGold    = new(1.00f, 0.75f, 0.20f, 1f);
    private static readonly Vector4 ColSkill   = new(0.35f, 0.65f, 1.00f, 1f);
    private static readonly Vector4 ColSupport = new(0.40f, 0.90f, 0.45f, 1f);
    private static readonly Vector4 ColSpirit  = new(0.80f, 0.45f, 1.00f, 1f);
    private static readonly Vector4 ColNone    = new(0.55f, 0.55f, 0.55f, 1f);
private static readonly Vector4 ColErr     = new(0.95f, 0.30f, 0.30f, 1f);

    private static Vector4 TypeColor(string? type) => type?.ToLowerInvariant() switch
    {
        "skill"   => ColSkill,
        "support" => ColSupport,
        "spirit"  => ColSpirit,
        _         => ColNone
    };

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override bool Initialise()
    {
        LoadGemDatabase();
        LoadBuildsFolder();

        Settings.ReloadData.OnPressed += ReloadAll;

        _menu = new GemRecommenderMenu(
            Settings,
            () => _build,
            () => _database,
            () => _warnings);

        return true;
    }

    public override void DrawSettings()
    {
        base.DrawSettings();
        _menu?.DrawConfiguration();
    }

    // ── Render ────────────────────────────────────────────────────────────────

    public override void Render()
    {
        if (!Settings.Enable) return;
        if (GameController == null) return;

        // Detect build selection change from the settings panel dropdown
        if (_lastSelectedBuild != Settings.SelectedBuild.Value)
        {
            _lastSelectedBuild = Settings.SelectedBuild.Value;
            LoadSelectedBuild();
        }

        var player = BuildPlayerState();
        _recommendations = BuildRecommendations(player);
        _recommendationIndex = Math.Clamp(
            _recommendationIndex, 0, Math.Max(0, _recommendations.Count - 1));

        var bg  = Settings.BackgroundColor.Value;
        var bgV = new Vector4(bg.R / 255f, bg.G / 255f, bg.B / 255f, bg.A / 255f);
        var pad = (float)Settings.BackgroundPadding.Value;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, bgV);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(pad, pad));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f, 5f));

        ImGui.SetNextWindowPos(new Vector2(Settings.WindowX, Settings.WindowY), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(Settings.WindowWidth, 0), ImGuiCond.Always);

        ImGui.Begin("##GemRecommender", OverlayFlags);
        DrawOverlayContent(player);
        ImGui.End();

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor();

        DrawInventoryHighlights();
    }

    // ── Overlay content ───────────────────────────────────────────────────────

    private void DrawOverlayContent(PlayerState player)
    {
        ImGui.TextColored(ColGold, "  Gem Advisor");

        // ── Build selector ────────────────────────────────────────────────
        if (_buildFiles.Count > 0)
        {
            var buildNames = _buildFiles.Select(b => b.DisplayName).ToArray();
            var currentIdx = Math.Max(0,
                _buildFiles.FindIndex(b => b.DisplayName == Settings.SelectedBuild.Value));

            ImGui.SetNextItemWidth(-1f);
            if (ImGui.Combo("##buildsel", ref currentIdx, buildNames, buildNames.Length))
            {
                Settings.SelectedBuild.Value = buildNames[currentIdx];
                _lastSelectedBuild           = buildNames[currentIdx];
                LoadSelectedBuild();
                _recommendations     = [];
                _recommendationIndex = 0;
            }
        }

        ImGui.Separator();

        if (_loadError != null)
        {
            ImGui.TextColored(ColErr, "  [!] " + _loadError);
            return;
        }

        if (_build == null)
        {
            ImGui.TextColored(ColErr, "  No build loaded.");
            return;
        }

        if (_recommendations.Count == 0)
        {
            ImGui.TextColored(ColNone, "  No recommendations.");
            ImGui.TextWrapped("  Put uncut gems in inventory -- they must meet gem level and character level requirements.");
            ImGui.Separator();
            DrawProgress(player);
            return;
        }

        var slot = _recommendations[_recommendationIndex];

        // ── Navigation + gem name ─────────────────────────────────────────
        var hasPrev = _recommendationIndex > 0;
        var hasNext = _recommendationIndex < _recommendations.Count - 1;

        if (!hasPrev) ImGui.BeginDisabled();
        if (ImGui.ArrowButton("##prev", ImGuiDir.Left)) _recommendationIndex--;
        if (!hasPrev) ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.TextColored(ColNone, $"{_recommendationIndex + 1}/{_recommendations.Count}");
        ImGui.SameLine();

        if (!hasNext) ImGui.BeginDisabled();
        if (ImGui.ArrowButton("##next", ImGuiDir.Right)) _recommendationIndex++;
        if (!hasNext) ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.TextColored(TypeColor(slot.BuildGem.Type), $"  {slot.BuildGem.Label}");

        // ── Action line ───────────────────────────────────────────────────
        if (slot.InventoryGem != null)
        {
            ImGui.TextColored(TypeColor(slot.BuildGem.Type),
                $"  Equip from inventory  Lv.{slot.InventoryGem.Level}");
        }
        else if (slot.UncutToUse != null)
        {
            var actionLabel = slot.IsUpgrade ? "Upgrade:" : "Use:";
            ImGui.TextColored(TypeColor(slot.UncutToUse.Type),
                $"  {actionLabel} Uncut {CapFirst(slot.UncutToUse.Type)} Gem  Lv.{slot.UncutToUse.Level}");

            if (slot.IsUpgrade)
                ImGui.TextColored(ColNone, $"  (currently Lv.{slot.CurrentEquippedLevel})");
        }

        if (slot.BuildGem.Type.Equals("support", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(slot.BuildGem.TargetSkill))
        {
            ImGui.TextColored(ColNone, $"  -> {slot.BuildGem.TargetSkill}");
        }

        if (!string.IsNullOrWhiteSpace(slot.SupportToRemove))
        {
            ImGui.TextColored(ColErr, $"  Remove: {slot.SupportToRemove}");
        }

        ImGui.Separator();
        DrawProgress(player);
    }

    private void DrawProgress(PlayerState player)
    {
        if (_build == null) return;
        var total = _build.Gems.Count;
        var owned = _build.Gems.Count(g =>
            player.OwnedGems.Contains(g.Id, StringComparer.OrdinalIgnoreCase));
        ImGui.TextColored(ColNone, $"  {owned}/{total} build gems obtained");
    }

    // ── Recommendation builder ────────────────────────────────────────────────

    private List<RecommendationSlot> BuildRecommendations(PlayerState player)
    {
        if (_build == null) return [];

        var charLevel = player.CharacterLevel;
        var uncutPool = ScanInventoryUncutGems()
            .Where(u => charLevel >= u.DropLevel)
            .ToList();

        // Family suppression: for each family+targetSkill group, only the
        // highest-priority non-owned eligible gem may be recommended.
        var suppressedByFamily = BuildFamilySuppressedSet(player, charLevel);

        // Classify each build gem that still needs work
        var missing  = new List<(GemEntry Gem, EquippedSkillGemInfo? EquippedInfo)>();
        var upgrades = new List<(GemEntry Gem, EquippedSkillGemInfo  EquippedInfo)>();

        foreach (var buildGem in _build.Gems)
        {
            if (RecommendationEngine.HasGem(player, buildGem.Id)) continue;
            if (!RecommendationEngine.MeetsCharLevelCondition(buildGem, charLevel)) continue;
            if (suppressedByFamily.Contains(buildGem.Id)) continue;

            var equippedInfo = player.EquippedSkillGems
                .FirstOrDefault(e => e.Name.Equals(buildGem.Id, StringComparison.OrdinalIgnoreCase));

            if (equippedInfo != null)
                upgrades.Add((buildGem, equippedInfo));
            else
                missing.Add((buildGem, null));
        }

        var slots         = new List<RecommendationSlot>();
        var remainingPool = uncutPool.ToList();

        // Pass 1: allocate to missing gems
        // Inventory gems are checked first (no uncut cost); fall back to uncut pool.
        // Highest uncut level wins so higher-priority build gems get the best gems.
        foreach (var (buildGem, _) in missing)
        {
            string? supportToRemove = null;

            if (buildGem.Type.Equals("support", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetSupportSlot(buildGem, player, out supportToRemove))
                    continue;
            }

            // Prefer equipping an existing inventory gem over creating from an uncut
            var invGem = player.InventoryGems
                .Where(g => g.Name.Equals(buildGem.Id, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(g => g.Level)
                .FirstOrDefault();

            if (invGem != null)
            {
                slots.Add(new RecommendationSlot(buildGem, null, false, 0, supportToRemove, invGem));
                continue;
            }

            // No inventory gem — try to create from an uncut gem.
            // Supports: use the lowest sufficient uncut gem (preserve high-tier gems for skill/spirit).
            // Skill/spirit: use the highest available (higher-priority gems earn the best uncut).
            var isSupport = buildGem.Type.Equals("support", StringComparison.OrdinalIgnoreCase);
            var candidates = remainingPool
                .Where(u => u.Type.Equals(buildGem.Type, StringComparison.OrdinalIgnoreCase))
                .Where(u => u.Level >= (buildGem.RequiredGemLevel ?? 1));
            var uncut = isSupport
                ? candidates.OrderBy(u => u.Level).FirstOrDefault()
                : candidates.OrderByDescending(u => u.Level).FirstOrDefault();

            if (uncut == null) continue;

            remainingPool.Remove(uncut);
            slots.Add(new RecommendationSlot(buildGem, uncut, false, 0, supportToRemove));
        }

        // Pass 2: allocate remaining uncut gems to upgrades (use highest available)
        foreach (var (buildGem, equippedInfo) in upgrades)
        {
            // Check for a better version of this gem already in inventory
            var invGem = player.InventoryGems
                .Where(g => g.Name.Equals(buildGem.Id, StringComparison.OrdinalIgnoreCase))
                .Where(g => g.Level > equippedInfo.Level)
                .OrderByDescending(g => g.Level)
                .FirstOrDefault();

            if (invGem != null)
            {
                slots.Add(new RecommendationSlot(buildGem, null, true, equippedInfo.Level, null, invGem));
                continue;
            }

            var uncut = remainingPool
                .Where(u => u.Type.Equals(buildGem.Type, StringComparison.OrdinalIgnoreCase))
                .Where(u => u.Level > equippedInfo.Level)
                .OrderByDescending(u => u.Level)
                .FirstOrDefault();

            if (uncut == null) continue;

            remainingPool.Remove(uncut);
            slots.Add(new RecommendationSlot(buildGem, uncut, true, equippedInfo.Level, null));
        }

        // Pass 3: offer upgrades for already-completed gems using leftover uncut gems.
        // maxGemLevel is intentionally ignored here — it is a "done" threshold, not an upgrade cap.
        foreach (var buildGem in _build.Gems)
        {
            if (buildGem.Type.Equals("support", StringComparison.OrdinalIgnoreCase)) continue;
            if (!RecommendationEngine.HasGem(player, buildGem.Id)) continue; // not yet completed
            if (!RecommendationEngine.MeetsCharLevelCondition(buildGem, charLevel)) continue;
            if (slots.Any(s => s.BuildGem.Id.Equals(buildGem.Id, StringComparison.OrdinalIgnoreCase)))
                continue; // already has a slot from Pass 1 or 2

            var equippedInfo = player.EquippedSkillGems
                .FirstOrDefault(e => e.Name.Equals(buildGem.Id, StringComparison.OrdinalIgnoreCase));

            if (equippedInfo == null) continue;

            // Check for a better inventory copy first
            var invGem = player.InventoryGems
                .Where(g => g.Name.Equals(buildGem.Id, StringComparison.OrdinalIgnoreCase))
                .Where(g => g.Level > equippedInfo.Level)
                .OrderByDescending(g => g.Level)
                .FirstOrDefault();

            if (invGem != null)
            {
                slots.Add(new RecommendationSlot(buildGem, null, true, equippedInfo.Level, null, invGem));
                continue;
            }

            var uncut = remainingPool
                .Where(u => u.Type.Equals(buildGem.Type, StringComparison.OrdinalIgnoreCase))
                .Where(u => u.Level > equippedInfo.Level)
                .OrderByDescending(u => u.Level)
                .FirstOrDefault();

            if (uncut == null) continue;

            remainingPool.Remove(uncut);
            slots.Add(new RecommendationSlot(buildGem, uncut, true, equippedInfo.Level, null));
        }

        // Sort by priority category, then by build order within each category.
        // Cat 0 = equip from inventory, no clearing needed  (ready to slot)
        // Cat 1 = equip from inventory, needs socket clear
        // Cat 2 = prioritizeOverEmptySockets                (create/upgrade, high priority)
        // Cat 3 = new gem, socket is free                   (create from uncut)
        // Cat 4 = new gem but needs support removal         (support swap)
        // Cat 5 = skill/spirit upgrade (below maxGemLevel or beyond)
        slots.Sort((a, b) =>
        {
            static int Category(RecommendationSlot s)
            {
                var fromInv = s.InventoryGem != null;
                if (fromInv && s.SupportToRemove == null) return 0;
                if (fromInv) return 1;
                if (s.BuildGem.PrioritizeOverEmptySockets) return 2;
                if (s.IsUpgrade) return 5;
                if (s.SupportToRemove != null) return 4;
                return 3;
            }

            var catDiff = Category(a) - Category(b);
            if (catDiff != 0) return catDiff;
            return _build.Gems.IndexOf(a.BuildGem) - _build.Gems.IndexOf(b.BuildGem);
        });

        return slots;
    }

    // Returns IDs suppressed because a higher-priority family member is still achievable.
    private HashSet<string> BuildFamilySuppressedSet(PlayerState player, int charLevel)
    {
        var suppressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_build == null) return suppressed;

        var familyGroups = _build.Gems
            .Where(g => !string.IsNullOrWhiteSpace(g.Family))
            .GroupBy(g => (
                Family: g.Family!.ToLowerInvariant(),
                Target: g.TargetSkill?.ToLowerInvariant() ?? ""));

        foreach (var group in familyGroups)
        {
            var inBuildOrder = group
                .OrderBy(g => _build.Gems.IndexOf(g))
                .ToList();

            // First non-owned gem in the group that meets the char-level condition = active tier
            GemEntry? activeGem = null;
            foreach (var gem in inBuildOrder)
            {
                if (!RecommendationEngine.HasGem(player, gem.Id) &&
                    RecommendationEngine.MeetsCharLevelCondition(gem, charLevel))
                {
                    activeGem = gem;
                    break;
                }
            }

            if (activeGem == null) continue;

            // All other non-owned siblings in the same group are suppressed
            foreach (var gem in inBuildOrder)
            {
                if (!gem.Id.Equals(activeGem.Id, StringComparison.OrdinalIgnoreCase) &&
                    !RecommendationEngine.HasGem(player, gem.Id))
                {
                    suppressed.Add(gem.Id);
                }
            }
        }

        return suppressed;
    }

    // ── Support socket helpers ────────────────────────────────────────────────

    private bool TryGetSupportSlot(GemEntry supportGem, PlayerState player, out string? supportToRemove)
    {
        supportToRemove = null;
        if (string.IsNullOrWhiteSpace(supportGem.TargetSkill)) return false;

        var targetSkill = player.EquippedSkillGems
            .FirstOrDefault(e => e.Name.Equals(supportGem.TargetSkill, StringComparison.OrdinalIgnoreCase));

        if (targetSkill == null) return false;

        if (targetSkill.FreeSockets > 0) return true;

        // All sockets used — look for something removable:
        // 1. A support not in the build plan at all
        supportToRemove = FindNonBuildSupportToRemove(targetSkill);
        if (supportToRemove != null) return true;

        // 2. A lower-priority family member being superseded by this gem
        supportToRemove = FindFamilyMemberToReplace(supportGem, targetSkill);
        return supportToRemove != null;
    }

    private string? FindNonBuildSupportToRemove(EquippedSkillGemInfo skillInfo)
    {
        if (_build == null) return null;

        var buildSupports = _build.Gems
            .Where(g => g.Type.Equals("support", StringComparison.OrdinalIgnoreCase)
                     && (g.TargetSkill?.Equals(skillInfo.Name, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(g => g.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return skillInfo.CurrentSupports
            .FirstOrDefault(s => !buildSupports.Contains(s));
    }

    // Finds a lower-priority (later in build.json) family member currently socketed
    // into skillInfo that should be removed to make room for supportGem.
    private string? FindFamilyMemberToReplace(GemEntry supportGem, EquippedSkillGemInfo skillInfo)
    {
        if (string.IsNullOrWhiteSpace(supportGem.Family) || _build == null) return null;

        var myIndex = _build.Gems.IndexOf(supportGem);

        var lowerTierIds = _build.Gems
            .Where(g => !string.IsNullOrWhiteSpace(g.Family)
                     && g.Family.Equals(supportGem.Family, StringComparison.OrdinalIgnoreCase)
                     && (g.TargetSkill?.Equals(skillInfo.Name, StringComparison.OrdinalIgnoreCase) ?? false)
                     && _build.Gems.IndexOf(g) > myIndex)
            .Select(g => g.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return skillInfo.CurrentSupports
            .FirstOrDefault(s => lowerTierIds.Contains(s));
    }

    // ── Inventory scan ────────────────────────────────────────────────────────

    private List<UncutGemInfo> ScanInventoryUncutGems()
    {
        if (Settings.DebugGemMode.Value)
        {
            return [new UncutGemInfo
            {
                Type      = Settings.DebugUncutGemType.Value ?? "skill",
                Level     = Settings.DebugUncutGemLevel.Value,
                DropLevel = 0,
            }];
        }
        return ScanInventoryUncutGemsFromGame();
    }

    private List<UncutGemInfo> ScanInventoryUncutGemsFromGame()
    {
        var result = new List<UncutGemInfo>();
        try
        {
            var inventory = GameController?.IngameState?.IngameUi
                ?.InventoryPanel[ExileCore2.Shared.Enums.InventoryIndex.PlayerInventory]
                .VisibleInventoryItems;

            if (inventory == null) return result;

            foreach (var item in inventory)
            {
                var path = item?.Item?.Path;
                if (path == null) continue;

                var baseType = GameController?.Files?.BaseItemTypes?.Translate(path);
                if (baseType == null) continue;

                var match = UncutGemRegex.Match(baseType.BaseName ?? "");
                if (!match.Success) continue;

                result.Add(new UncutGemInfo
                {
                    Type      = match.Groups[1].Value.ToLowerInvariant(),
                    Level     = int.Parse(match.Groups[2].Value),
                    DropLevel = baseType.DropLevel,
                });
            }
        }
        catch (Exception ex)
        {
            LogError($"[GemRecommender] ScanInventoryUncutGems: {ex.Message}");
        }
        return result;
    }

    // Scans inventory for already-created skill gems (path contains Metadata/Items/Gems).
    private List<InventoryGemInfo> ScanInventoryCreatedGems()
    {
        var result = new List<InventoryGemInfo>();
        try
        {
            var inventory = GameController?.IngameState?.IngameUi
                ?.InventoryPanel[ExileCore2.Shared.Enums.InventoryIndex.PlayerInventory]
                .VisibleInventoryItems;

            if (inventory == null) return result;

            foreach (var item in inventory)
            {
                var path = item?.Item?.Path;
                if (path == null) continue;

                if (!path.Contains("Metadata/Items/Gems", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip uncut gems — they are handled by ScanInventoryUncutGems
                var baseType = GameController?.Files?.BaseItemTypes?.Translate(path);
                if (baseType != null && UncutGemRegex.IsMatch(baseType.BaseName ?? ""))
                    continue;

                var name    = item?.Entity?.GetComponent<Base>()?.Name;
                var level   = item?.Entity?.GetComponent<SkillGem>()?.Level ?? 0;
                var sockets = item?.Entity?.GetComponent<Sockets>()?.NumberOfSockets ?? 0;

                if (string.IsNullOrWhiteSpace(name)) continue;

                result.Add(new InventoryGemInfo
                {
                    Name    = name,
                    Level   = level,
                    Sockets = sockets,
                });
            }
        }
        catch (Exception ex)
        {
            LogError($"[GemRecommender] ScanInventoryCreatedGems: {ex.Message}");
        }
        return result;
    }

    // ── Inventory highlight ───────────────────────────────────────────────────

    private void DrawInventoryHighlights()
    {
        if (_recommendations.Count == 0) return;

        var current = _recommendations[_recommendationIndex];

        // Nothing to highlight if neither an uncut gem nor an inventory gem is involved.
        if (current.UncutToUse == null && current.InventoryGem == null) return;

        try
        {
            var inventory = GameController?.IngameState?.IngameUi
                ?.InventoryPanel[ExileCore2.Shared.Enums.InventoryIndex.PlayerInventory]
                ?.VisibleInventoryItems;

            if (inventory == null) return;

            foreach (var item in inventory)
            {
                if (item == null) continue;

                var path = item.Item?.Path;
                if (path == null) continue;

                bool matches;

                if (current.UncutToUse != null)
                {
                    // Match uncut gems by type+level via the regex.
                    var baseType = GameController?.Files?.BaseItemTypes?.Translate(path);
                    if (baseType == null) continue;

                    var match = UncutGemRegex.Match(baseType.BaseName ?? "");
                    if (!match.Success) continue;

                    var type  = match.Groups[1].Value.ToLowerInvariant();
                    var level = int.Parse(match.Groups[2].Value);
                    matches = type.Equals(current.UncutToUse.Type, StringComparison.OrdinalIgnoreCase)
                           && level == current.UncutToUse.Level;
                }
                else
                {
                    // Match created gems by name+level via entity components.
                    if (!path.Contains("Metadata/Items/Gems", StringComparison.OrdinalIgnoreCase)) continue;

                    var baseType = GameController?.Files?.BaseItemTypes?.Translate(path);
                    if (baseType != null && UncutGemRegex.IsMatch(baseType.BaseName ?? "")) continue;

                    var name  = item.Entity?.GetComponent<Base>()?.Name;
                    var level = item.Entity?.GetComponent<SkillGem>()?.Level ?? 0;
                    matches = name != null
                           && name.Equals(current.InventoryGem!.Name, StringComparison.OrdinalIgnoreCase)
                           && level == current.InventoryGem.Level;
                }

                if (!matches) continue;

                var drawRect = item.GetClientRect();
                drawRect.Top    += 3;
                drawRect.Bottom -= 3;
                drawRect.Left   += 3;
                drawRect.Right  -= 3;
                Graphics.DrawFrame(drawRect, Settings.ColorBorder.Value, Settings.BorderThickness.Value);
                break; // one item highlighted per step
            }
        }
        catch (Exception ex)
        {
            LogError($"[GemRecommender] DrawInventoryHighlights: {ex.Message}");
        }
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private void LoadGemDatabase()
    {
        _database = [];

        (string path, string type)[] files =
        [
            (ResolvePath(Settings.SkillGemsCsvPath.Value   ?? @"Data\skill_gems.csv"),   "Skill"),
            (ResolvePath(Settings.SpiritGemsCsvPath.Value  ?? @"Data\spirit_gems.csv"),  "Spirit"),
            (ResolvePath(Settings.SupportGemsCsvPath.Value ?? @"Data\support_gems.csv"), "Support"),
        ];

        foreach (var (path, type) in files)
        {
            var entries = GemDatabase.LoadFromCsv(path, type, out var error);
            if (error != null)
                LogError($"[GemRecommender] {type} CSV: {error}");
            else
                _database.AddRange(entries);
        }
    }

    private void LoadBuildsFolder()
    {
        _build      = null;
        _loadError  = null;
        _warnings   = [];
        _buildFiles = [];

        var folder = ResolveBuildsFolder();

        try
        {
            Directory.CreateDirectory(folder);
            SeedExampleBuilds(folder);
        }
        catch (Exception ex)
        {
            _loadError = $"Builds folder not found: {folder} ({ex.Message})";
            LogError($"[GemRecommender] {_loadError}");
            Settings.SelectedBuild.Values = [];
            return;
        }

        var jsonFiles = Directory.GetFiles(folder, "*.json")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (jsonFiles.Length == 0)
        {
            _loadError = $"No .json build files found in: {folder}";
            LogError($"[GemRecommender] {_loadError}");
            Settings.SelectedBuild.Values = [];
            return;
        }

        _buildFiles = [.. jsonFiles
            .Select(f => (DisplayName: Path.GetFileNameWithoutExtension(f), FullPath: f))];

        var names = _buildFiles.Select(b => b.DisplayName).ToList();
        Settings.SelectedBuild.Values = names;

        if (!names.Contains(Settings.SelectedBuild.Value))
            Settings.SelectedBuild.Value = names[0];

        _lastSelectedBuild = Settings.SelectedBuild.Value;
        LoadSelectedBuild();
    }

    private void LoadSelectedBuild()
    {
        _build     = null;
        _loadError = null;
        _warnings  = [];

        var selected = _buildFiles.FirstOrDefault(b =>
            b.DisplayName == Settings.SelectedBuild.Value);

        if (selected == default)
        {
            _loadError = "No build selected.";
            return;
        }

        var path = selected.FullPath;

        if (!File.Exists(path))
        {
            _loadError = $"Build file not found: {path}";
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            _build   = JsonConvert.DeserializeObject<BuildDefinition>(json);

            if (_build == null)
            {
                _loadError = "Build JSON is empty or invalid.";
                return;
            }

            // DB level is the acquisition floor; build level can demand higher, but not lower
            foreach (var gem in _build.Gems)
            {
                var dbLevel    = GemDatabase.GetMinLevel(_database, gem.Id);
                var buildLevel = gem.RequiredGemLevel ?? 1;
                gem.RequiredGemLevel = dbLevel > 0 ? Math.Max(buildLevel, dbLevel) : buildLevel;
            }

            _warnings = GemDatabase.ValidateBuild(_build, _database);
        }
        catch (Exception ex)
        {
            _loadError = $"Build JSON parse error: {ex.Message}";
            _build = null;
        }
    }

    private void ReloadAll()
    {
        LoadGemDatabase();
        LoadBuildsFolder();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private PlayerState BuildPlayerState()
    {
        var skillGems    = ReadEquippedSkillGems();
        var inventoryGems = ScanInventoryCreatedGems();
        var owned        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Equipped gems: count as owned when at/above maxGemLevel (or when no cap is set)
        foreach (var info in skillGems)
        {
            var entry = _build?.Gems.FirstOrDefault(g =>
                g.Id.Equals(info.Name, StringComparison.OrdinalIgnoreCase));

            if (entry?.MaxGemLevel is int maxLvl && info.Level < maxLvl)
            {
                // equipped but not yet at required level
            }
            else
            {
                owned.Add(info.Name);
            }

            foreach (var sup in info.CurrentSupports)
                owned.Add(sup);
        }

        // Inventory gems: only count as owned when they exceed the maxGemLevel threshold.
        // Otherwise always recommend equipping them — a gem in the bag isn't helping the build.
        var usableInventoryGems = new List<InventoryGemInfo>();
        foreach (var invGem in inventoryGems)
        {
            var entry = _build?.Gems.FirstOrDefault(g =>
                g.Id.Equals(invGem.Name, StringComparison.OrdinalIgnoreCase));
            if (entry == null) continue; // not in build — ignore

            if (entry.MaxGemLevel is int maxLvl && invGem.Level >= maxLvl)
                owned.Add(invGem.Name);      // already satisfies the "done" threshold
            else
                usableInventoryGems.Add(invGem); // needs equipping
        }

        return new PlayerState
        {
            OwnedGems         = [.. owned],
            EquippedGems      = [.. skillGems.Select(g => g.Name)],
            EquippedSkillGems = skillGems,
            InventoryGems     = usableInventoryGems,
            CharacterLevel    = ReadCharacterLevel(),
        };
    }

    private List<EquippedSkillGemInfo> ReadEquippedSkillGems()
    {
        var result = new List<EquippedSkillGemInfo>();
        try
        {
            var rows = GameController?.IngameState?.IngameUi?.SkillPanel?.Rows;
            if (rows == null) return result;

            foreach (var row in rows)
            {
                if (row?.GemElement == null) continue;

                var name    = row.GemElement.Entity?.GetComponent<Base>()?.Name;
                var level   = row.GemElement.Entity?.GetComponent<SkillGem>()?.Level ?? 0;
                var sockets = row.GemElement.Entity?.GetComponent<Sockets>()?.NumberOfSockets ?? 0;

                if (string.IsNullOrWhiteSpace(name)) continue;

                var supports = new List<string>();
                var supportElements = row.SupportGemElements;
                if (supportElements != null)
                {
                    foreach (var sup in supportElements)
                    {
                        var supName = sup?.GemType?.BaseName;
                        if (!string.IsNullOrWhiteSpace(supName))
                            supports.Add(supName);
                    }
                }

                result.Add(new EquippedSkillGemInfo
                {
                    Name            = name,
                    Level           = level,
                    MaxSockets      = sockets,
                    CurrentSupports = supports,
                });
            }
        }
        catch (Exception ex)
        {
            LogError($"[GemRecommender] ReadEquippedSkillGems: {ex.Message}");
        }
        return result;
    }

    private int ReadCharacterLevel()
    {
        if (Settings.DebugPlayerLevelMode.Value)
            return Settings.DebugPlayerLevel.Value;
        try   { return GameController?.Player?.GetComponent<Player>()?.Level ?? 0; }
        catch { return 0; }
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        return Path.Combine(DirectoryFullName, path);
    }

    // Build files are user data, so they live under ExileCore2's per-plugin config
    // directory (…\config\GemRecommender). An empty setting means "use that folder";
    // a relative setting is resolved against it; an absolute setting is used as-is.
    private string ResolveBuildsFolder()
    {
        var configured = Settings.BuildsFolderPath.Value;
        if (string.IsNullOrWhiteSpace(configured))
            return ConfigDirectory;
        if (Path.IsPathRooted(configured))
            return configured;
        return Path.Combine(ConfigDirectory, configured);
    }

    // Copy the example builds shipped next to the plugin DLL into the config folder
    // on first run, so the user starts with working examples in the right place.
    private void SeedExampleBuilds(string targetFolder)
    {
        if (Directory.GetFiles(targetFolder, "*.json").Length > 0) return;

        var shipped = Path.Combine(DirectoryFullName, "Builds");
        if (!Directory.Exists(shipped)) return;

        foreach (var src in Directory.GetFiles(shipped, "*.json"))
        {
            var name = Path.GetFileName(src);
            if (string.Equals(name, "example_build.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var dest = Path.Combine(targetFolder, name);
            if (!File.Exists(dest))
                File.Copy(src, dest);
        }
    }

    private static string CapFirst(string s)
        => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}
