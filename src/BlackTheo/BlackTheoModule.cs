using Celeste.Mod;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using MonoMod.RuntimeDetour;
using System;
using Utils;

namespace Celeste.Mod.BlackTheo;

public sealed class BlackTheoModule : EverestModule
{
    private static readonly BlackTheoSettings DefaultSettings = new();

    public static BlackTheoModule Instance { get; private set; } = null!;

    public override Type SettingsType => typeof(BlackTheoSettings);

    public static BlackTheoSettings Settings =>
        Instance?._Settings as BlackTheoSettings ?? DefaultSettings;

    private Hook? LogHook;
    private static void Logger_Log_Hook(Action<LogLevel, string, string> orig, LogLevel level, string tag, string message)
    {
        var lvl = level switch
        {
            LogLevel.Debug => LycarionLogLevel.Debug,
            LogLevel.Info => LycarionLogLevel.Info,
            LogLevel.Warn => LycarionLogLevel.Warn,
            LogLevel.Error => LycarionLogLevel.Error,
            _ => LycarionLogLevel.Info
        };
        //if (tag == "BlackTheo")
            LycarionLogger.Log(lvl, tag, message);

        orig(level, tag, message);
    }

    public BlackTheoModule()
    {
        Instance = this;
    }

    public override void Load()
    {
        LycarionLogger.Initialize(18080);
        LogHook = new Hook(
            typeof(Logger).GetMethod("Log", [typeof(LogLevel), typeof(string), typeof(string)])!,
            Logger_Log_Hook
        );

        On.Celeste.Glitch.Apply += Glitch_Apply;
        On.Celeste.TheoCrystal.ctor_Vector2 += TheoCrystal_ctor;
        On.Celeste.GameplayRenderer.Render += GameplayRenderer_Render;
        On.Celeste.LightingRenderer.Render += LightingRenderer_Render;
        On.Celeste.BloomRenderer.Apply += BloomRenderer_Apply;
        On.Celeste.Player.Render += Player_Render;
        Logger.Log(LogLevel.Info, "BlackTheo", "Loaded. Theo is now a lensing black hole.");
    }

    public override void Unload()
    {
        On.Celeste.Glitch.Apply -= Glitch_Apply;
        On.Celeste.TheoCrystal.ctor_Vector2 -= TheoCrystal_ctor;
        On.Celeste.GameplayRenderer.Render -= GameplayRenderer_Render;
        On.Celeste.LightingRenderer.Render -= LightingRenderer_Render;
        On.Celeste.BloomRenderer.Apply -= BloomRenderer_Apply;
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

        LogHook?.Dispose();
        LycarionLogger.Shutdown();
    }

    private static void GameplayRenderer_Render(
        On.Celeste.GameplayRenderer.orig_Render orig,
        GameplayRenderer self,
        Scene scene)
    {
        MadelineLayer.BeginFrame();

        if (!BlackTheoModule.Settings.HookGameplayRenderer ||
            !BlackTheoModule.Settings.MadelineLayerCapture)
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
            MadelineLayer.ExtractPlayerLayer();
        }
    }

    private static void Player_Render(On.Celeste.Player.orig_Render orig, Player self)
    {
        if (BlackTheoModule.Settings.HookPlayerRender &&
            MadelineLayer.IsCapturing)
        {
            MadelineLayer.CaptureMask(self);
        }

        orig(self);
    }

    private static void LightingRenderer_Render(
        On.Celeste.LightingRenderer.orig_Render orig,
        LightingRenderer self,
        Scene scene)
    {
        orig(self, scene);

        if (!BlackTheoModule.Settings.HookLighting)
        {
            return;
        }

        MadelineLayer.ApplyLighting(self);
        MadelineLayer.ApplyDistort();
    }

    private static void BloomRenderer_Apply(
        On.Celeste.BloomRenderer.orig_Apply orig,
        BloomRenderer self,
        VirtualRenderTarget target,
        Scene scene)
    {
        orig(self, target, scene);

        if (!BlackTheoModule.Settings.HookBloom ||
            target != GameplayBuffers.Level ||
            MadelineLayer.IsApplyingPlayerBloom)
        {
            return;
        }

        MadelineLayer.ApplyBloomToPlayer(self, scene);
        MadelineLayer.CompositePlayerBloom();
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

        if (!BlackTheoModule.Settings.HookGlitch)
        {
            return;
        }

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
