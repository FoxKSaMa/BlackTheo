using System;
using Celeste.Mod;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.BlackTheo;

public enum LensStrengthMode
{
    // Eleven steps. Low is kept as value 2 and High as value 10 so old
    // settings and the previous "low/high" language still make sense.
    Off = 0,
    Level1 = 1,
    Low = 2,
    Medium = 3,
    Level4 = 4,
    Level5 = 5,
    Level6 = 6,
    Level7 = 7,
    Level8 = 8,
    Level9 = 9,
    High = 10
}

public enum DiskColorMode
{
    Blue,
    IceBlue,
    Red,
    Purple,
    Green,
    Yellow,
    White,
    Pink,
    Brown,
    DarkRed,
    Gray,
    Custom
}

public enum MadelineLensMode
{
    Off,
    Manual,
    Speed
}

public readonly struct LensStrengthSettings
{
    public readonly float Scale;
    public readonly float Cap;
    public readonly float Radius;
    public readonly float Falloff;
    public readonly float EdgeStart;

    private LensStrengthSettings(float scale, float cap, float radius, float falloff, float edgeStart)
    {
        Scale = scale;
        Cap = cap;
        Radius = radius;
        Falloff = falloff;
        EdgeStart = edgeStart;
    }

    public static LensStrengthSettings For(LensStrengthMode mode)
    {
        int level = (int)mode;

        if (level <= 0)
        {
            return new LensStrengthSettings(0.0f, 0.0f, 50.0f, 2.2f, 0.72f);
        }

        if (level == 1)
        {
            return new LensStrengthSettings(1.0f, 5.0f, 46.0f, 2.3f, 0.76f);
        }

        // Levels 2..10 interpolate from the original Low (2) to High (10).
        float t = (level - 2) / 8.0f;
        return new LensStrengthSettings(
            2.0f + 2.8f * t,
            10.0f + 14.0f * t,
            50.0f + 30.0f * t,
            2.2f - 0.65f * t,
            0.72f - 0.20f * t);
    }
}

public readonly struct DiskPalette
{
    public readonly Vector4 Inner;
    public readonly Vector4 Mid;
    public readonly Vector4 Outer;
    public readonly Vector4 Photon;
    public readonly Vector4 Halo;
    public readonly float Alpha;

    private DiskPalette(Vector4 inner, Vector4 mid, Vector4 outer, Vector4 photon, Vector4 halo)
        : this(inner, mid, outer, photon, halo, 1f)
    {
    }

    private DiskPalette(Vector4 inner, Vector4 mid, Vector4 outer, Vector4 photon, Vector4 halo, float alpha)
    {
        Inner = inner;
        Mid = mid;
        Outer = outer;
        Photon = photon;
        Halo = halo;
        Alpha = alpha;
    }

    public static DiskPalette For(DiskColorMode mode)
    {
        return mode switch
        {
            // The original v1.0.0 look.
            DiskColorMode.Blue => new DiskPalette(
                V(0.55f, 0.88f, 1.00f),
                V(0.08f, 0.42f, 1.00f),
                V(0.01f, 0.07f, 0.42f),
                V(0.62f, 0.91f, 1.00f),
                V(0.30f, 0.65f, 1.00f)),

            // The intentional fallback color, now selectable as a preset.
            DiskColorMode.IceBlue => FromCustom(new Color(80, 180, 255)),

            // Red-orange, not pure red: keeps the white-hot inner rim readable.
            DiskColorMode.Red => new DiskPalette(
                V(1.00f, 0.72f, 0.42f),
                V(1.00f, 0.22f, 0.045f),
                V(0.36f, 0.018f, 0.006f),
                V(1.00f, 0.87f, 0.62f),
                V(0.66f, 0.16f, 0.055f)),

            DiskColorMode.Purple => new DiskPalette(
                V(0.92f, 0.70f, 1.00f),
                V(0.50f, 0.16f, 1.00f),
                V(0.16f, 0.015f, 0.36f),
                V(0.94f, 0.78f, 1.00f),
                V(0.48f, 0.18f, 0.92f)),

            DiskColorMode.Green => new DiskPalette(
                V(0.70f, 1.00f, 0.80f),
                V(0.08f, 0.82f, 0.32f),
                V(0.006f, 0.21f, 0.06f),
                V(0.73f, 1.00f, 0.80f),
                V(0.06f, 0.55f, 0.20f)),

            DiskColorMode.Yellow => new DiskPalette(
                V(1.00f, 0.94f, 0.64f),
                V(0.87f, 0.66f, 0.28f),
                V(0.40f, 0.24f, 0.075f),
                V(1.00f, 0.93f, 0.66f),
                V(0.64f, 0.47f, 0.15f)),

            DiskColorMode.White => new DiskPalette(
                V(1.00f, 0.99f, 0.94f),
                V(0.91f, 0.94f, 1.00f),
                V(0.52f, 0.60f, 0.74f),
                V(1.00f, 1.00f, 1.00f),
                V(0.64f, 0.74f, 0.90f)),

            DiskColorMode.Pink => new DiskPalette(
                V(1.00f, 0.72f, 0.86f),
                V(0.98f, 0.26f, 0.62f),
                V(0.36f, 0.035f, 0.19f),
                V(1.00f, 0.80f, 0.92f),
                V(0.86f, 0.24f, 0.58f)),

            DiskColorMode.Brown => new DiskPalette(
                V(1.00f, 0.83f, 0.62f),
                V(0.62f, 0.36f, 0.18f),
                V(0.20f, 0.095f, 0.040f),
                V(1.00f, 0.88f, 0.70f),
                V(0.48f, 0.28f, 0.13f)),

            DiskColorMode.DarkRed => new DiskPalette(
                V(1.00f, 0.55f, 0.40f),
                V(0.62f, 0.045f, 0.035f),
                V(0.18f, 0.005f, 0.005f),
                V(1.00f, 0.62f, 0.50f),
                V(0.55f, 0.06f, 0.05f)),

            DiskColorMode.Gray => new DiskPalette(
                V(0.90f, 0.93f, 0.97f),
                V(0.52f, 0.56f, 0.63f),
                V(0.16f, 0.17f, 0.21f),
                V(0.92f, 0.95f, 1.00f),
                V(0.40f, 0.46f, 0.55f)),

            // The renderer normally calls FromCustom() with the parsed hex
            // value; this case is only a safe fallback.
            DiskColorMode.Custom => FromCustom(HexColor.Fallback),

            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    public static DiskPalette FromCustom(Color color)
    {
        Vector3 b = color.ToVector3();
        Vector3 inner = Vector3.Lerp(b, Vector3.One, 0.45f);
        Vector3 photon = Vector3.Lerp(b, Vector3.One, 0.78f);
        Vector3 outer = b * 0.28f;
        Vector3 halo = Vector3.Lerp(b, Vector3.Zero, 0.32f);
        float alpha = color.A / 255f;

        return new DiskPalette(
            V4(inner),
            V4(b),
            V4(outer),
            V4(photon),
            V4(halo),
            alpha);
    }

    private static Vector4 V(float r, float g, float b) => new Vector4(r, g, b, 1f);

    private static Vector4 V4(Vector3 c) => new Vector4(c.X, c.Y, c.Z, 1f);
}

public sealed class BlackTheoSettings : EverestModuleSettings
{
    [SettingName("blacktheo_enabled")]
    public bool Enabled { get; set; } = true;

    [SettingName("blacktheo_lens")]
    public LensStrengthMode LensStrength { get; set; } = LensStrengthMode.Low;

    [SettingName("blacktheo_theo_lens_madeline")]
    public bool TheoLensAffectsMadeline { get; set; } = true;

    [SettingName("blacktheo_disk_color")]
    public DiskColorMode DiskColor { get; set; } = DiskColorMode.Blue;

    // Same pattern as MoreDasheline: a plain string setting, edited with the
    // game keyboard from the main-menu Mod Options. Everest disables string
    // entries in-game, so this is marked main-menu-only instead of showing a
    // greyed-out field during play.
    [SettingName("blacktheo_custom_color")]
    [SettingInGame(false)]
    [SettingMaxLength(9)]
    [SettingSubText("blacktheo_custom_color_help")]
    public string CustomColorHex { get; set; } = "FF7A45";

    internal Color CustomColor =>
        HexColor.TryParse(CustomColorHex, out Color color) ? color : HexColor.Fallback;

    [SettingName("blacktheo_doppler")]
    public bool DopplerEnabled { get; set; } = true;

    [SettingName("blacktheo_specks")]
    public bool SpecksEnabled { get; set; } = true;

    [SettingName("blacktheo_madeline_lens")]
    [SettingSubText("blacktheo_madeline_speed_help")]
    public MadelineLensMode MadelineLens { get; set; } = MadelineLensMode.Off;

    [SettingName("blacktheo_madeline_level")]
    [SettingRange(0, 10)]
    public int MadelineLensLevel
    {
        get => madelineLensLevel;
        set => madelineLensLevel = Math.Clamp(value, 0, 10);
    }

    [SettingName("blacktheo_madeline_layer")]
    [SettingSubText("blacktheo_madeline_layer_help")]
    public bool MadelineLayerCapture { get; set; } = false;

    [SettingName("blacktheo_hook_gameplay")]
    [SettingSubText("blacktheo_hook_gameplay_help")]
    public bool HookGameplayRenderer { get; set; } = true;

    [SettingName("blacktheo_hook_player")]
    [SettingSubText("blacktheo_hook_player_help")]
    public bool HookPlayerRender { get; set; } = true;

    [SettingName("blacktheo_hook_lighting")]
    [SettingSubText("blacktheo_hook_lighting_help")]
    public bool HookLighting { get; set; } = true;

    [SettingName("blacktheo_hook_bloom")]
    [SettingSubText("blacktheo_hook_bloom_help")]
    public bool HookBloom { get; set; } = true;

    [SettingName("blacktheo_hook_glitch")]
    [SettingSubText("blacktheo_hook_glitch_help")]
    public bool HookGlitch { get; set; } = true;

    private int madelineLensLevel = 2;
}
