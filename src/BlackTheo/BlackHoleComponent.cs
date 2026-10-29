using System;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.BlackTheo;

/// <summary>
/// Per-Theo black-hole state and visual ownership.
///
/// All global defaults live in <see cref="BlackTheoSettings"/>; this component
/// can override them per entity for maps / other mods:
///
///     var bh = theo.Get&lt;BlackHoleComponent&gt;();
///     bh.LensStrengthOverride = LensStrengthMode.High;
///     bh.DiskColorOverride = DiskColorMode.Purple;
///     bh.DopplerOverride = false;
///
/// Set Enabled = false to return to the ordinary Theo crystal.
/// </summary>
public sealed class BlackHoleComponent : Component
{
    private Sprite? sprite;
    private VertexLight? light;
    private float originalLightAlpha = 1f;

    public bool Enabled { get; set; } = true;

    public LensStrengthMode? LensStrengthOverride { get; set; }
    public DiskColorMode? DiskColorOverride { get; set; }
    public string? CustomColorOverride { get; set; }
    public bool? DopplerOverride { get; set; }
    public bool? SpecksOverride { get; set; }

    /// <summary>Multiplier for the accretion disk brightness.</summary>
    public float Brightness
    {
        get => brightness;
        set => brightness = MathHelper.Clamp(value, 0f, 2f);
    }

    private float brightness = 1f;

    /// <summary>Stable per-instance seed used by the horizon specks.</summary>
    public int Seed { get; private set; }

    public LensStrengthMode LensStrength =>
        LensStrengthOverride ?? BlackTheoModule.Settings.LensStrength;

    public DiskColorMode DiskColor =>
        DiskColorOverride ?? BlackTheoModule.Settings.DiskColor;

    public string CustomColorHex =>
        CustomColorOverride ?? BlackTheoModule.Settings.CustomColorHex;

    public Color CustomColor =>
        CustomColorOverride != null
            ? (HexColor.TryParse(CustomColorOverride, out Color color) ? color : HexColor.Fallback)
            : BlackTheoModule.Settings.CustomColor;

    public bool Doppler =>
        DopplerOverride ?? BlackTheoModule.Settings.DopplerEnabled;

    public bool Specks =>
        SpecksOverride ?? BlackTheoModule.Settings.SpecksEnabled;

    public BlackHoleComponent() : base(active: true, visible: true)
    {
    }

    public override void Added(Entity entity)
    {
        base.Added(entity);

        sprite = entity.Get<Sprite>();
        light = entity.Get<VertexLight>();
        if (light != null)
        {
            originalLightAlpha = light.Alpha;
        }

        Seed = (entity.GetHashCode() & 0x7fffffff) % 100000;
        ApplyVisualState();
    }

    public override void Update()
    {
        base.Update();
        ApplyVisualState();
    }

    public override void Removed(Entity entity)
    {
        RestoreVisualState();
        base.Removed(entity);
    }

    public void RestoreVisualState()
    {
        if (sprite != null)
        {
            sprite.Visible = true;
        }
        if (light != null)
        {
            light.Alpha = originalLightAlpha;
        }
    }

    private void ApplyVisualState()
    {
        bool active = Enabled && BlackTheoModule.Settings.Enabled && BlackHoleRenderer.IsOperational;

        if (sprite != null)
        {
            sprite.Visible = !active;
        }
        if (light != null)
        {
            // Keep the component attached but invisible. Removing and re-adding
            // a VertexLight would require it to be re-registered with the
            // lighting renderer; alpha is safer for live settings changes.
            light.Alpha = active ? 0f : originalLightAlpha;
        }
    }
}
