#!/usr/bin/env dotnet-script
// Standalone test runner — validates RecommendationEngine rules without ExileCore.
// Run with:  dotnet script Tests.csx
// Or just read as documentation of expected behaviour.

// ────────────────────────────────────────────────────────────────────────────
// Inline stubs for types normally defined in BuildData.cs
// ────────────────────────────────────────────────────────────────────────────

record GemRecommendation(string RecommendationType, string? GemId, string Reason)
{
    public static GemRecommendation None(string r) => new("none", null, r);
}

class GemEntry
{
    public string  Id              { get; set; } = "";
    public string  Type            { get; set; } = "";
    public int     Priority        { get; set; } = 99;
    public int     RequiredGemLevel{ get; set; } = 1;
    public string? TargetSkill     { get; set; }
    public string? DisplayName     { get; set; }
    public string  Label           => DisplayName ?? Id;
}

class BuildDefinition { public List<GemEntry> Gems { get; set; } = []; }

class UncutGemInfo { public string Type { get; set; } = "skill"; public int Level { get; set; } = 1; }

class PlayerState
{
    public List<string> OwnedGems    { get; set; } = [];
    public List<string> EquippedGems { get; set; } = [];
    public UncutGemInfo? UncutGem    { get; set; }
}

// ────────────────────────────────────────────────────────────────────────────
// Inline recommendation engine (mirrors RecommendationEngine.cs)
// ────────────────────────────────────────────────────────────────────────────

static class Engine
{
    public static GemRecommendation Recommend(BuildDefinition b, PlayerState p)
    {
        if (b?.Gems == null || b.Gems.Count == 0)    return GemRecommendation.None("empty build");
        if (p.UncutGem == null)                       return GemRecommendation.None("no uncut gem");

        var t = p.UncutGem.Type.ToLowerInvariant();
        var l = p.UncutGem.Level;

        var eligible = t switch
        {
            "skill"   => EligibleSkills(b, p, l),
            "support" => EligibleSupports(b, p, l),
            "spirit"  => EligibleSpirits(b, p, l),
            _         => new List<GemEntry>()
        };

        if (eligible.Count == 0) return GemRecommendation.None("no match");

        var best = Sorted(eligible).First();
        return new GemRecommendation(best.Type.ToLower(), best.Id, $"priority #{best.Priority}");
    }

    static List<GemEntry> EligibleSkills(BuildDefinition b, PlayerState p, int l)
        => b.Gems.Where(g => Is(g,"skill") && !Has(p,g.Id) && l >= g.RequiredGemLevel).ToList();

    static List<GemEntry> EligibleSupports(BuildDefinition b, PlayerState p, int l)
        => b.Gems.Where(g => Is(g,"support") && !Has(p,g.Id) && l >= g.RequiredGemLevel
                          && !string.IsNullOrWhiteSpace(g.TargetSkill) && Has(p, g.TargetSkill!)).ToList();

    static List<GemEntry> EligibleSpirits(BuildDefinition b, PlayerState p, int l)
        => b.Gems.Where(g => Is(g,"spirit") && !Has(p,g.Id) && l >= g.RequiredGemLevel).ToList();

    static bool Is(GemEntry g, string t) => g.Type.Equals(t, StringComparison.OrdinalIgnoreCase);
    static bool Has(PlayerState p, string id) => p.OwnedGems.Contains(id, StringComparer.OrdinalIgnoreCase);

    static IEnumerable<GemEntry> Sorted(IEnumerable<GemEntry> gems)
        => gems.OrderBy(g => g.Priority)
               .ThenBy(g => Is(g,"skill") ? 0 : 1)
               .ThenBy(g => g.Id, StringComparer.OrdinalIgnoreCase);
}

// ────────────────────────────────────────────────────────────────────────────
// Test helpers
// ────────────────────────────────────────────────────────────────────────────

int passed = 0, failed = 0;

void Expect(string label, GemRecommendation result, string expectedType, string? expectedId)
{
    bool ok = result.RecommendationType == expectedType && result.GemId == expectedId;
    if (ok) { Console.WriteLine($"  ✓  {label}"); passed++; }
    else    { Console.WriteLine($"  ✗  {label}"); Console.WriteLine($"       expected ({expectedType}, {expectedId ?? "null"})"); Console.WriteLine($"       got      ({result.RecommendationType}, {result.GemId ?? "null"})"); failed++; }
}

// ────────────────────────────────────────────────────────────────────────────
// Shared build fixture
// ────────────────────────────────────────────────────────────────────────────

var build = new BuildDefinition
{
    Gems =
    [
        new() { Id = "lightning_arrow", Type = "skill",   Priority = 1, RequiredGemLevel = 1 },
        new() { Id = "orb_of_storms",   Type = "skill",   Priority = 2, RequiredGemLevel = 4 },
        new() { Id = "martial_tempo",   Type = "support", Priority = 3, RequiredGemLevel = 1, TargetSkill = "lightning_arrow" },
        new() { Id = "precision",       Type = "spirit",  Priority = 4, RequiredGemLevel = 1 },
        new() { Id = "chain_lightning", Type = "skill",   Priority = 5, RequiredGemLevel = 4 },
        new() { Id = "blindsight",      Type = "support", Priority = 6, RequiredGemLevel = 4, TargetSkill = "lightning_arrow" },
    ]
};

// ────────────────────────────────────────────────────────────────────────────
// Test cases
// ────────────────────────────────────────────────────────────────────────────

Console.WriteLine("\n── Rule A: Skill gems ───────────────────────────────────────");

// Basic: pick highest-priority unowned skill
Expect("A1 — first skill, nothing owned",
    Engine.Recommend(build, new() { UncutGem = new() { Type = "skill", Level = 1 } }),
    "skill", "lightning_arrow");

// Owned gems are skipped
Expect("A2 — skip owned lightning_arrow",
    Engine.Recommend(build, new() { OwnedGems = ["lightning_arrow"], UncutGem = new() { Type = "skill", Level = 4 } }),
    "skill", "orb_of_storms");

// Level gate: uncut level 1 cannot create orb_of_storms (requires 4) even when lightning_arrow is owned
Expect("A3 — level too low for orb_of_storms → chain_lightning also level 4 → none for level 1",
    Engine.Recommend(build, new() { OwnedGems = ["lightning_arrow"], UncutGem = new() { Type = "skill", Level = 1 } }),
    "none", null);

// Higher uncut level → still valid (edge case 4)
Expect("A4 — uncut level 10 satisfies requiredGemLevel 4",
    Engine.Recommend(build, new() { OwnedGems = ["lightning_arrow"], UncutGem = new() { Type = "skill", Level = 10 } }),
    "skill", "orb_of_storms");

Console.WriteLine("\n── Rule B: Support gems ─────────────────────────────────────");

// No skills owned → no supports
Expect("B1 — no skills owned, support type → none",
    Engine.Recommend(build, new() { UncutGem = new() { Type = "support", Level = 5 } }),
    "none", null);

// Target skill owned → support eligible
Expect("B2 — target skill owned → recommend support",
    Engine.Recommend(build, new() { OwnedGems = ["lightning_arrow"], UncutGem = new() { Type = "support", Level = 1 } }),
    "support", "martial_tempo");

// Support level gate
Expect("B3 — blindsight requires level 4, uncut is level 1 → only martial_tempo eligible",
    Engine.Recommend(build, new() { OwnedGems = ["lightning_arrow"], UncutGem = new() { Type = "support", Level = 1 } }),
    "support", "martial_tempo");

// All supports owned → none
Expect("B4 — all supports owned → none",
    Engine.Recommend(build, new() { OwnedGems = ["lightning_arrow","martial_tempo","blindsight"], UncutGem = new() { Type = "support", Level = 5 } }),
    "none", null);

Console.WriteLine("\n── Spirit gems ──────────────────────────────────────────────");

Expect("C1 — spirit gem recommended",
    Engine.Recommend(build, new() { UncutGem = new() { Type = "spirit", Level = 1 } }),
    "spirit", "precision");

Expect("C2 — precision owned → none left",
    Engine.Recommend(build, new() { OwnedGems = ["precision"], UncutGem = new() { Type = "spirit", Level = 1 } }),
    "none", null);

Console.WriteLine("\n── Rule C: Priority + tie-breaking ─────────────────────────");

// Two supports same priority → alphabetical by id
var tieBreakBuild = new BuildDefinition
{
    Gems =
    [
        new() { Id = "lightning_arrow", Type = "skill",   Priority = 1, RequiredGemLevel = 1 },
        new() { Id = "added_cold",      Type = "support", Priority = 5, RequiredGemLevel = 1, TargetSkill = "lightning_arrow" },
        new() { Id = "added_fire",      Type = "support", Priority = 5, RequiredGemLevel = 1, TargetSkill = "lightning_arrow" },
    ]
};
Expect("D1 — tie on priority → alphabetical (added_cold < added_fire)",
    Engine.Recommend(tieBreakBuild, new() { OwnedGems = ["lightning_arrow"], UncutGem = new() { Type = "support", Level = 1 } }),
    "support", "added_cold");

// Skill vs support same priority → skill wins
var mixedTieBuild = new BuildDefinition
{
    Gems =
    [
        new() { Id = "skill_gem",   Type = "skill",   Priority = 5, RequiredGemLevel = 1 },
        new() { Id = "support_gem", Type = "support", Priority = 5, RequiredGemLevel = 1, TargetSkill = "other_skill" },
        new() { Id = "other_skill", Type = "skill",   Priority = 1, RequiredGemLevel = 1 },
    ]
};
// Not directly testable with mixed type since uncut gem has one type; this exercises sort order instead.

Console.WriteLine("\n── Edge cases ───────────────────────────────────────────────");

Expect("E1 — empty build → none",
    Engine.Recommend(new BuildDefinition(), new() { UncutGem = new() { Type = "skill", Level = 5 } }),
    "none", null);

Expect("E2 — support with missing targetSkill field → not eligible",
    Engine.Recommend(
        new BuildDefinition { Gems = [new() { Id = "broken_support", Type = "support", Priority = 1, RequiredGemLevel = 1, TargetSkill = null }] },
        new() { OwnedGems = [], UncutGem = new() { Type = "support", Level = 5 } }),
    "none", null);

Expect("E3 — all skills already owned → none for skill type",
    Engine.Recommend(build,
        new() { OwnedGems = ["lightning_arrow","orb_of_storms","chain_lightning"], UncutGem = new() { Type = "skill", Level = 10 } }),
    "none", null);

// ────────────────────────────────────────────────────────────────────────────
Console.WriteLine($"\n  Results: {passed} passed, {failed} failed");
Console.WriteLine(failed == 0 ? "  ALL TESTS PASSED ✓\n" : "  SOME TESTS FAILED ✗\n");
