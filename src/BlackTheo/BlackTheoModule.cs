using System;
using Celeste.Mod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.BlackTheo;

public sealed class BlackTheoModule : EverestModule
{
    private static readonly BlackTheoSettings DefaultSettings = new();

    public static BlackTheoModule Instance { get; private set; } = null!;

    public override Type SettingsType => typeof(BlackTheoSettings);

    public static BlackTheoSettings Settings =>
        Instance?._Settings as BlackTheoSettings ?? DefaultSettings;

    public BlackTheoModule()
    {
        Instance = this;
    }

    public override void Load()
    {
        On.Celeste.Glitch.Apply += Glitch_Apply;
        On.Celeste.TheoCrystal.ctor_Vector2 += TheoCrystal_ctor;
        On.Celeste.GameplayRenderer.Render += GameplayRenderer_Render;
        On.Celeste.Player.Render += Player_Render;
        Logger.Log(LogLevel.Info, "BlackTheo", "Loaded. Theo is now a lensing black hole.");
    }

    public override void Unload()
    {
        On.Celeste.Glitch.Apply -= Glitch_Apply;
        On.Celeste.TheoCrystal.ctor_Vector2 -= TheoCrystal_ctor;
        On.Celeste.GameplayRenderer.Render -= GameplayRenderer_Render;
        On.Celeste.Player.Render -= Player_Render;

        // If this is a live unload, put the normal Theo visuals back.
        if (Engine.Scene is Level level)
        {
            foreach (TheoCrystal theo in level.Tracker.GetEntities<TheoCrystal>())
            {
                theo.Get<BlackHoleComponent>()?.RestoreVisualState();
            }
        }

        MadelineLayer.Dispose();
        BlackHoleRenderer.Dispose();
        Logger.Log(LogLevel.Info, "BlackTheo", "Unloaded.");
    }

    private static void GameplayRenderer_Render(
        On.Celeste.GameplayRenderer.orig_Render orig,
        GameplayRenderer self,
        Scene scene)
    {
        if (!BlackTheoModule.Settings.MadelineLayerCapture)
        {
            orig(self, scene);
            return;
        }

        MadelineLayer.BeginCapture();
        try
        {
            orig(self, scene);
        }
        finally
        {
            MadelineLayer.EndCapture();
        }
    }

    private static void Player_Render(On.Celeste.Player.orig_Render orig, Player self)
    {
        if (MadelineLayer.IsCapturing && MadelineLayer.TryCapture(self))
        {
            return;
        }

        orig(self);
    }

    private static void TheoCrystal_ctor(
        On.Celeste.TheoCrystal.orig_ctor_Vector2 orig,
        TheoCrystal self,
        Vector2 position)
    {
        orig(self, position);

        // The component owns the original sprite/light and the per-Theo
        // overrides. It is also what restores the crystal if the effect is
        // disabled or fails to load.
        if (self.Get<BlackHoleComponent>() == null)
        {
            self.Add(new BlackHoleComponent());
        }
    }

    private static void Glitch_Apply(
        On.Celeste.Glitch.orig_Apply orig,
        VirtualRenderTarget source,
        float timer,
        float seed,
        float amplitude)
    {
        // Glitch.Apply is the last world-space full-screen pass in Level.Render:
        // background, gameplay, lighting, foreground and glitch have all been
        // composited into GameplayBuffers.Level by this point, but the HUD has
        // not. Post-processing here therefore warps the screen behind Theo
        // without warping the HUD.
        orig(source, timer, seed, amplitude);

        try
        {
            BlackHoleRenderer.Render(source);
        }
        catch (Exception e)
        {
            BlackHoleRenderer.DisableAfterFailure(e);
        }
    }
}
