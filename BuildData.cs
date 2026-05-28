using System.Collections.Generic;
using Newtonsoft.Json;

namespace GemRecommender;

// ── Build Definition ─────────────────────────────────────────────────────────

public class BuildDefinition
{
    [JsonProperty("gems")]
    public List<GemEntry> Gems { get; set; } = [];
}

public class GemEntry
{
    /// <summary>Unique identifier matching the gem's in-game base name.</summary>
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    /// <summary>"skill" | "support" | "spirit"</summary>
    [JsonProperty("type")]
    public string Type { get; set; } = "";

    /// <summary>
    /// Minimum uncut gem level needed to create this gem.
    /// Null (field absent from JSON) = auto-detected from the gem database.
    /// </summary>
    [JsonProperty("requiredGemLevel")]
    public int? RequiredGemLevel { get; set; }

    /// <summary>
    /// Skill / spirit gems only. Once the equipped gem reaches this level the
    /// gem is considered complete and will no longer be recommended.
    /// Null = no cap (any equipped copy counts as obtained).
    /// </summary>
    [JsonProperty("maxGemLevel")]
    public int? MaxGemLevel { get; set; }

    /// <summary>Only recommend this gem when character level ≥ this value.</summary>
    [JsonProperty("minCharLevel")]
    public int MinCharLevel { get; set; } = 0;

    /// <summary>
    /// Stop recommending this gem once character level exceeds this value.
    /// Null = no upper limit.
    /// </summary>
    [JsonProperty("maxCharLevel")]
    public int? MaxCharLevel { get; set; }

    /// <summary>
    /// Support gems: the id of the skill this support attaches to.
    /// A support is only eligible when its target skill is already owned.
    /// </summary>
    [JsonProperty("targetSkill")]
    public string? TargetSkill { get; set; }

    /// <summary>Optional display name (falls back to id when absent).</summary>
    [JsonProperty("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Groups mutually-exclusive tiered gems (e.g. "Elemental Armament").
    /// Within a family+targetSkill group only the highest-priority non-owned
    /// tier is ever recommended; lower tiers are suppressed.
    /// </summary>
    [JsonProperty("family")]
    public string? Family { get; set; }

    /// <summary>
    /// When true this gem is sorted above free-socket fills in the recommendation
    /// list, overriding the default rule that empty sockets are filled first.
    /// </summary>
    [JsonProperty("prioritizeOverEmptySockets")]
    public bool PrioritizeOverEmptySockets { get; set; } = false;

    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
}

// ── Player State ─────────────────────────────────────────────────────────────

public class EquippedSkillGemInfo
{
    public string       Name            { get; set; } = "";
    public int          Level           { get; set; }
    public int          MaxSockets      { get; set; }
    public List<string> CurrentSupports { get; set; } = [];
    public int FreeSockets => MaxSockets - CurrentSupports.Count;
}

public class PlayerState
{
    public List<string>               OwnedGems         { get; set; } = [];
    public List<string>               EquippedGems      { get; set; } = [];
    public List<EquippedSkillGemInfo> EquippedSkillGems { get; set; } = [];
    public List<InventoryGemInfo>     InventoryGems     { get; set; } = [];
    public int                        CharacterLevel     { get; set; }
    public UncutGemInfo?              UncutGem           { get; set; }
}

public class UncutGemInfo
{
    public string Type      { get; set; } = "skill";
    public int    Level     { get; set; } = 1;
    public int    DropLevel { get; set; } = 0;
}

// A created gem found in the player's inventory (not an uncut gem).
public class InventoryGemInfo
{
    public string Name    { get; set; } = "";
    public int    Level   { get; set; }
    public int    Sockets { get; set; }
}

// ── Gem Database Entry ────────────────────────────────────────────────────────

public class GemDatabaseEntry
{
    public string Type    { get; set; } = "";
    public string GemName { get; set; } = "";
    public int    Level   { get; set; } = 1;
}

// ── Recommendation Result ─────────────────────────────────────────────────────

public record GemRecommendation(
    string  RecommendationType,
    string? GemId,
    string  Reason
)
{
    public static GemRecommendation None(string reason) =>
        new("none", null, reason);
}
