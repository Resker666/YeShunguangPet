using System;

namespace YeShunguangPet;

public sealed class AppearanceOptions
{
    public string Theme { get; set; } = "system";
    public string Accent { get; set; } = "#A92C46";
    public bool ReduceMotion { get; set; }
    public static readonly string[] Accents = { "#A92C46", "#24796E", "#396BB8", "#667085" };

    public AppearanceOptions Clone() => new() { Theme = Theme, Accent = Accent, ReduceMotion = ReduceMotion };

    public void Normalize()
    {
        if (Theme is not ("system" or "light" or "dark")) Theme = "system";
        Accent = Accent?.ToUpperInvariant() ?? Accents[0];
        if (Array.IndexOf(Accents, Accent) < 0) Accent = Accents[0];
    }
}
