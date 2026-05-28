using System.Collections.Generic;
using System.Drawing;
using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;

namespace GemRecommender;

public class GemRecommenderSettings : ISettings
{
    // ── Master toggle ────────────────────────────────────────────────────────
    public ToggleNode Enable { get; set; } = new(true);

    // ── Overlay appearance ────────────────────────────────────────────────────
    public RangeNode<int> WindowX         { get; set; } = new(10,  0, 4000);
    public RangeNode<int> WindowY         { get; set; } = new(300, 0, 4000);
    public RangeNode<int> WindowWidth     { get; set; } = new(340, 120, 900);
    public RangeNode<int> BackgroundPadding { get; set; } = new(8,  0, 40);

    public ColorNode BackgroundColor  { get; set; } = new(Color.FromArgb(215, 12, 12, 12));
    public ColorNode DefaultTextColor { get; set; } = new(Color.FromArgb(220, 220, 220));

    // ── Data file paths ───────────────────────────────────────────────────────
    public TextNode BuildsFolderPath   { get; set; } = new(@"Builds");
    public ListNode SelectedBuild      { get; set; } = new();
    public TextNode SkillGemsCsvPath   { get; set; } = new(@"Data\skill_gems.csv");
    public TextNode SpiritGemsCsvPath  { get; set; } = new(@"Data\spirit_gems.csv");
    public TextNode SupportGemsCsvPath { get; set; } = new(@"Data\support_gems.csv");

    // ── Action buttons ────────────────────────────────────────────────────────
    public ButtonNode ReloadData { get; set; } = new();

    // ── Debug ─────────────────────────────────────────────────────────────────
    // DebugGemMode: bypass inventory scan and use the manual uncut gem below.
    public ToggleNode DebugGemMode { get; set; } = new(false);

    public ListNode DebugUncutGemType { get; set; } = new ListNode
    {
        Values = ["skill", "support", "spirit"],
        Value  = "skill"
    };

    public RangeNode<int> DebugUncutGemLevel { get; set; } = new(1, 1, 21);

    // DebugPlayerLevelMode: bypass real character level and use the value below.
    public ToggleNode      DebugPlayerLevelMode  { get; set; } = new(false);
    public RangeNode<int>  DebugPlayerLevel      { get; set; } = new(1, 1, 100);

    // ── Inventory highlight ───────────────────────────────────────────────────
    public ColorNode      ColorBorder      { get; set; } = new(Color.DeepPink);
    public RangeNode<int> BorderThickness  { get; set; } = new(5, 1, 10);
}
