using System;
using System.Collections.Generic;
using System.Linq;

namespace GemRecommender;

/// <summary>
/// Pure-logic recommendation engine — no ExileCore or ImGui dependencies.
///
/// Eligibility rules:
///   Skills  — not owned · uncut type == "skill"  · uncutLevel >= requiredGemLevel
///             · charLevel in [minCharLevel, maxCharLevel]
///   Supports — not owned · uncut type == "support" · uncutLevel >= requiredGemLevel
///              · charLevel in range · targetSkill already owned
///   Spirits  — same shape as skills but type == "spirit"
///
/// Ordering: position in build.json (first entry = highest priority).
/// </summary>
public static class RecommendationEngine
{
    // ── Public API ────────────────────────────────────────────────────────────

    public static GemRecommendation RecommendGem(BuildDefinition build, PlayerState player)
    {
        if (build?.Gems == null || build.Gems.Count == 0)
            return GemRecommendation.None("No build definition loaded.");

        if (player.UncutGem == null)
            return GemRecommendation.None("No uncut gem specified.");

        var uncutType  = player.UncutGem.Type.ToLowerInvariant();
        var uncutLevel = player.UncutGem.Level;

        List<GemEntry> eligible = uncutType switch
        {
            "skill"   => GetEligibleSkills(build, player, uncutLevel),
            "support" => GetEligibleSupports(build, player, uncutLevel),
            "spirit"  => GetEligibleSpirits(build, player, uncutLevel),
            _         => []
        };

        if (eligible.Count == 0)
            return GemRecommendation.None(
                $"No valid {uncutType} gem matches your build — " +
                $"either all are obtained or the uncut level ({uncutLevel}) is too low.");

        var best      = SortByBuildOrder(eligible, build.Gems).First();
        var typeLabel = best.Type.ToLowerInvariant();

        var reason = typeLabel switch
        {
            "support" => $"Highest-priority missing support. " +
                         $"Attaches to '{best.TargetSkill}'. " +
                         $"Requires level {best.RequiredGemLevel} — you have {uncutLevel}.",

            "spirit"  => $"Highest-priority missing spirit gem. " +
                         $"Requires level {best.RequiredGemLevel} — you have {uncutLevel}.",

            _         => $"Highest-priority missing skill. " +
                         $"Requires level {best.RequiredGemLevel} — you have {uncutLevel}."
        };

        return new GemRecommendation(typeLabel, best.Id, reason);
    }

    public static List<GemEntry> GetAllEligible(BuildDefinition build, PlayerState player)
    {
        if (build?.Gems == null || player.UncutGem == null)
            return [];

        var uncutType  = player.UncutGem.Type.ToLowerInvariant();
        var uncutLevel = player.UncutGem.Level;

        List<GemEntry> eligible = uncutType switch
        {
            "skill"   => GetEligibleSkills(build, player, uncutLevel),
            "support" => GetEligibleSupports(build, player, uncutLevel),
            "spirit"  => GetEligibleSpirits(build, player, uncutLevel),
            _         => []
        };

        return SortByBuildOrder(eligible, build.Gems).ToList();
    }

    // ── Eligibility helpers ───────────────────────────────────────────────────

    public static List<GemEntry> GetEligibleSkills(
        BuildDefinition build, PlayerState player, int uncutLevel)
        => build.Gems
            .Where(g => IsType(g, "skill"))
            .Where(g => !HasGem(player, g.Id))
            .Where(g => IsValidByUncutLevel(g, uncutLevel))
            .Where(g => MeetsCharLevelCondition(g, player.CharacterLevel))
            .ToList();

    public static List<GemEntry> GetEligibleSupports(
        BuildDefinition build, PlayerState player, int uncutLevel)
        => build.Gems
            .Where(g => IsType(g, "support"))
            .Where(g => !HasGem(player, g.Id))
            .Where(g => IsValidByUncutLevel(g, uncutLevel))
            .Where(g => MeetsCharLevelCondition(g, player.CharacterLevel))
            .Where(g => !string.IsNullOrWhiteSpace(g.TargetSkill)
                        && player.EquippedSkillGems.Any(e =>
                            e.Name.Equals(g.TargetSkill, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    public static List<GemEntry> GetEligibleSpirits(
        BuildDefinition build, PlayerState player, int uncutLevel)
        => build.Gems
            .Where(g => IsType(g, "spirit"))
            .Where(g => !HasGem(player, g.Id))
            .Where(g => IsValidByUncutLevel(g, uncutLevel))
            .Where(g => MeetsCharLevelCondition(g, player.CharacterLevel))
            .ToList();

    // ── Predicate helpers ────────────────────────────────────────────────────

    public static bool IsType(GemEntry gem, string type)
        => gem.Type.Equals(type, StringComparison.OrdinalIgnoreCase);

    public static bool HasGem(PlayerState player, string gemId)
        => player.OwnedGems.Contains(gemId, StringComparer.OrdinalIgnoreCase);

    public static bool IsValidByUncutLevel(GemEntry gem, int uncutLevel)
        => uncutLevel >= gem.RequiredGemLevel;

    public static bool MeetsCharLevelCondition(GemEntry gem, int charLevel)
        => charLevel >= gem.MinCharLevel
        && (gem.MaxCharLevel == null || charLevel <= gem.MaxCharLevel);

    // ── Sorting ───────────────────────────────────────────────────────────────

    /// <summary>Preserves the order gems appear in build.json.</summary>
    public static IEnumerable<GemEntry> SortByBuildOrder(
        IEnumerable<GemEntry> gems, List<GemEntry> allGems)
        => gems.OrderBy(g => allGems.IndexOf(g));
}
