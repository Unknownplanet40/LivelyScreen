using System;
using System.Collections.Generic;
using Windows.UI;

namespace LivelyScreen.Models;

public static class KnowledgeMapPalette
{
    public static readonly Color BackgroundDark      = Color.FromArgb(255, 0x0B, 0x0D, 0x10); // #0B0D10
    public static readonly Color VignetteCenter       = Color.FromArgb(255, 0x14, 0x1A, 0x24); // #141A24
    public static readonly Color EdgeFilament         = Color.FromArgb(255, 150, 165, 190);    // RGB(150, 165, 190)
    public static readonly Color TextHalo             = Color.FromArgb(230, 0x0B, 0x0D, 0x10); // #0B0D10 (0.9 alpha)
    public static readonly Color TextMuted            = Color.FromArgb(255, 203, 213, 225);    // #CBD5E1
    public static readonly Color FavoriteRing         = Color.FromArgb(255, 0xF5, 0xB9, 0x42); // #F5B942

    public static readonly Color TypeTroubleshooting  = Color.FromArgb(255, 0xE2, 0x6D, 0x80); // #E26D80 Coral/Rose
    public static readonly Color TypeNote             = Color.FromArgb(255, 0x5B, 0x8D, 0xEF); // #5B8DEF Soft Blue
    public static readonly Color TypeQuote            = Color.FromArgb(255, 0xE3, 0xA8, 0x57); // #E3A857 Amber/Gold
    public static readonly Color TypeSaying           = Color.FromArgb(255, 0x4F, 0xBF, 0x9F); // #4FBF9F Teal
    public static readonly Color TypeJoke             = Color.FromArgb(255, 0x9C, 0x7B, 0xEA); // #9C7BEA Purple
    public static readonly Color TypeBook             = Color.FromArgb(255, 0x06, 0xB6, 0xD4); // #06B6D4 Cyan
    public static readonly Color TypeDiary            = Color.FromArgb(255, 0xEC, 0x48, 0x99); // #EC4899 Pink
    public static readonly Color TypeRecipe           = Color.FromArgb(255, 0x84, 0xCC, 0x16); // #84CC16 Lime
    public static readonly Color TypeFallback         = Color.FromArgb(255, 0x8B, 0x93, 0xA1); // #8B93A1 Slate
}

public sealed class KnowledgeMapNode
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Label { get; set; } = "";
    public string Type { get; set; } = "note";
    public string? Author { get; set; }
    public string? Tags { get; set; }
    public bool Favorite { get; set; }
    public int Degree { get; set; }
    public Color Color { get; set; } = KnowledgeMapPalette.TypeNote;
    public float Radius { get; set; } = 4.0f;

    // 3D coordinates in simulation space
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    // Projected 2D screen coordinates
    public float Px { get; set; }
    public float Py { get; set; }
    public float Scale { get; set; } = 1.0f;
    public float ZAlpha { get; set; } = 1.0f;
    public float ZDepth { get; set; }
}

public sealed class KnowledgeMapEdge
{
    public int SourceId { get; set; }
    public int TargetId { get; set; }
    public KnowledgeMapNode? SourceNode { get; set; }
    public KnowledgeMapNode? TargetNode { get; set; }
    public int HopDistance { get; set; } = 1;
}

public sealed class KnowledgeMapData
{
    public List<KnowledgeMapNode> Nodes { get; set; } = new();
    public List<KnowledgeMapEdge> Edges { get; set; } = new();
    public string StatusSummary { get; set; } = "";
    public bool IsConnectedToDb { get; set; }
    public int MaxHop { get; set; } = 1;
}
