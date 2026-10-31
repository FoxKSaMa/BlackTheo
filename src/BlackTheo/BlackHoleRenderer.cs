using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.BlackTheo;

/// <summary>
/// Owns the captured gameplay buffer and the lensing shader, and turns the
/// current scene's TheoCrystal components into shader parameters every frame.
/// </summary>
internal static class BlackHoleRenderer
{
    private const string LogTag = "BlackTheo";
    private const int MaxHoles = 4;

    // Visual tuning. Settings and per-Theo component overrides control the
    // lens strength, disk color, Doppler toggle, specks and disk brightness.
    private const float EventHorizonRadius = 8.0f;
    private const float MaxLensRadius = 80f;
    private const float MadelineMaxSpeed = 3000f;
    private const float MadelineMinLensRadius = 20f;
    private const float MadelineMaxLensRadius = 96f;

    private static readonly List<Hole> Holes = new(MaxHoles);
    private static readonly FieldInfo? DeadField =
        typeof(TheoCrystal).GetField("dead", BindingFlags.Instance | BindingFlags.NonPublic);

    private static float smoothedMadelineLensAmount;
    private static bool hasSmoothedMadelineLens;

    private static Effect? effect;
    private static Effect? madelineLensEffect;
    private static RenderTarget2D? capture;
    private static bool effectFailed;
    private static bool madelineLensFailed;
    private static bool renderFailed;

    public static bool IsOperational => !renderFailed && !effectFailed;

    private readonly struct Hole
    {
        public readonly Vector2 Center;
        public readonly float DistanceToViewCenter;
        public readonly LensStrengthMode LensStrength;
        public readonly DiskPalette Palette;
        public readonly bool Doppler;
        public readonly bool Specks;
        public readonly float Brightness;
        public readonly int Seed;

        public Hole(
            Vector2 center,
            LensStrengthMode lensStrength,
            DiskPalette palette,
            bool doppler,
            bool specks,
            float brightness,
            int seed)
        {
            Center = center;
            DistanceToViewCenter = Vector2.DistanceSquared(center, new Vector2(160f, 90f));
            LensStrength = lensStrength;
            Palette = palette;
            Doppler = doppler;
            Specks = specks;
            Brightness = brightness;
            Seed = seed;
        }
    }

    private readonly struct MadelineLens
    {
        public readonly Vector2 Center;
        public readonly float Radius;
        public readonly float Strength;
        public readonly float CoreRadius;

        public MadelineLens(Vector2 center, float radius, float strength, float coreRadius)
        {
            Center = center;
            Radius = radius;
            Strength = strength;
            CoreRadius = coreRadius;
        }
    }

    public static void Render(VirtualRenderTarget source)
    {
        BlackTheoSettings settings = BlackTheoModule.Settings;
        if (renderFailed || effectFailed || !settings.Enabled)
        {
            return;
        }

        Level? level = Engine.Scene as Level;
        if (level == null)
        {
            return;
        }

        CollectHoles(level);
        MadelineLens? madelineLens = CollectMadelineLens(level, settings);
        Vector4 madelineMask = GetMadelineMask(level, settings);
        if (Holes.Count == 0 && madelineLens == null)
        {
            return;
        }

        RenderTarget2D sourceTarget = source;
        if (sourceTarget == null || sourceTarget.IsDisposed)
        {
            return;
        }

        if (!EnsureResources(sourceTarget))
        {
            return;
        }

        GraphicsDevice device = Engine.Instance.GraphicsDevice;
        int width = sourceTarget.Width;
        int height = sourceTarget.Height;
        float time = level.TimeActive % 1000f;

        SetGlobalParameters(width, height);

        // One hole per pass so overlapping holes build up naturally; this
        // also avoids asking MojoShader to inline four copies of the ray loop
        // in one ps_3_0 shader.
        for (int i = 0; i < Holes.Count; i++)
        {
            ApplyHole(device, sourceTarget, Holes[i], time, madelineMask);
        }

        if (madelineLens != null)
        {
            ApplyMadelineLens(device, sourceTarget, madelineLens.Value, time);
        }
    }

    private static void ApplyHole(
        GraphicsDevice device,
        RenderTarget2D sourceTarget,
        Hole hole,
        float time,
        Vector4 madelineMask)
    {
        // Capture the current level target. We cannot sample and write the
        // same render target in one pass, so this is the shader source.
        device.SetRenderTarget(capture);
        device.Clear(Color.Black);
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(sourceTarget, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        // Replace the level target with the result of the pass.
        device.SetRenderTarget(sourceTarget);
        device.Clear(Color.Black);

        LensStrengthSettings lens = LensStrengthSettings.For(hole.LensStrength);

        effect!.Parameters["Hole"].SetValue(
            new Vector4(hole.Center.X, hole.Center.Y, EventHorizonRadius, lens.Radius));
        effect.Parameters["HoleParams"].SetValue(
            new Vector4(hole.Brightness, lens.Scale, lens.Cap, time));
        effect.Parameters["LensFalloff"].SetValue(lens.Falloff);
        effect.Parameters["LensEdge"].SetValue(lens.EdgeStart);
        effect.Parameters["LensOnly"].SetValue(0f);
        effect.Parameters["MadelineMask"].SetValue(madelineMask);
        effect.Parameters["ProtectMadeline"].SetValue(madelineMask.Z > 0f ? 1f : 0f);
        effect.Parameters["DopplerEnabled"].SetValue(hole.Doppler ? 1f : 0f);
        effect.Parameters["SpecksEnabled"].SetValue(hole.Specks ? 1f : 0f);
        effect.Parameters["NoiseSeed"].SetValue((float)hole.Seed);
        effect.Parameters["DiskInnerColor"].SetValue(hole.Palette.Inner);
        effect.Parameters["DiskMidColor"].SetValue(hole.Palette.Mid);
        effect.Parameters["DiskOuterColor"].SetValue(hole.Palette.Outer);
        effect.Parameters["PhotonColor"].SetValue(hole.Palette.Photon);
        effect.Parameters["RingColor"].SetValue(hole.Palette.Halo);
        effect.Parameters["DiskAlpha"].SetValue(hole.Palette.Alpha);
        effect.CurrentTechnique = effect.Techniques["BlackHole"];

        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone, effect);
        Draw.SpriteBatch.Draw(capture, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();
    }

    private static void CollectHoles(Level level)
    {
        Holes.Clear();

        Vector2 camera = level.Camera.Position;
        foreach (TheoCrystal theo in level.Tracker.GetEntities<TheoCrystal>())
        {
            if (theo == null || !theo.Active || !theo.Visible)
            {
                continue;
            }

            BlackHoleComponent? component = theo.Get<BlackHoleComponent>();
            if (component == null || !component.Enabled)
            {
                continue;
            }

            // Die() sets this before the entity is removed. Skip a dead Theo
            // immediately so the black hole does not linger over the death
            // effect or the respawn transition.
            if (DeadField != null && DeadField.GetValue(theo) is bool dead && dead)
            {
                continue;
            }

            Vector2 screenCenter = theo.Center - camera;

            // Float the visual above the held crystal so it does not sit on
            // Madeline's head, and keep a small lift on the ground.
            screenCenter.Y -= theo.Hold != null && theo.Hold.IsHeld ? 5f : 1.5f;

            // Keep holes that are at least partially on screen. The margin is
            // the largest lens radius, because a hole just off-screen can still
            // bend pixels at the screen edge.
            if (screenCenter.X < -MaxLensRadius || screenCenter.X > 320f + MaxLensRadius ||
                screenCenter.Y < -MaxLensRadius || screenCenter.Y > 180f + MaxLensRadius)
            {
                continue;
            }

            LensStrengthMode lens = component.LensStrength;
            DiskColorMode color = component.DiskColor;
            DiskPalette palette = color == DiskColorMode.Custom
                ? DiskPalette.FromCustom(component.CustomColor)
                : DiskPalette.For(color);
            bool doppler = component.Doppler;
            bool specks = component.Specks;
            float brightness = component.Brightness;
            int seed = component.Seed;

            Holes.Add(new Hole(
                screenCenter,
                lens,
                palette,
                doppler,
                specks,
                brightness,
                seed));

            // Normal levels contain one Theo. If a modded level contains more,
            // prioritise the ones nearest the middle of the screen; four holes
            // are the shader's limit.
            if (Holes.Count > MaxHoles)
            {
                int farthest = 0;
                for (int i = 1; i < Holes.Count; i++)
                {
                    if (Holes[i].DistanceToViewCenter > Holes[farthest].DistanceToViewCenter)
                    {
                        farthest = i;
                    }
                }
                Holes.RemoveAt(farthest);
            }
        }
    }

    private static Vector4 GetMadelineMask(Level level, BlackTheoSettings settings)
    {
        if (settings.TheoLensAffectsMadeline)
        {
            return Vector4.Zero;
        }

        Player? player = level.Tracker.GetEntity<Player>();
        if (player == null || !player.Active || !player.Visible || player.Dead)
        {
            return Vector4.Zero;
        }

        Vector2 center = player.Center - level.Camera.Position;
        return new Vector4(center.X, center.Y, 16f, 28f);
    }

    private static MadelineLens? CollectMadelineLens(Level level, BlackTheoSettings settings)
    {
        if (settings.MadelineLens == MadelineLensMode.Off)
        {
            hasSmoothedMadelineLens = false;
            smoothedMadelineLensAmount = 0f;
            return null;
        }

        Player? player = level.Tracker.GetEntity<Player>();
        if (player == null || !player.Active || !player.Visible || player.Dead)
        {
            hasSmoothedMadelineLens = false;
            smoothedMadelineLensAmount = 0f;
            return null;
        }

        Vector2 screenCenter = player.Center - level.Camera.Position;
        if (screenCenter.X < -MadelineMaxLensRadius || screenCenter.X > 320f + MadelineMaxLensRadius ||
            screenCenter.Y < -MadelineMaxLensRadius || screenCenter.Y > 180f + MadelineMaxLensRadius)
        {
            hasSmoothedMadelineLens = false;
            smoothedMadelineLensAmount = 0f;
            return null;
        }

        // Speed can briefly read 0 during dash freezes, jelly/Theo grabs, or
        // wall impacts even though the player keeps velocity. Smoothing with a
        // fast attack and a slow release keeps the lens alive across those
        // short zeroes. During an actual freeze frame, hold the previous value.
        float target;
        if (settings.MadelineLens == MadelineLensMode.Speed)
        {
            target = MadelineSpeedLevel(player.Speed.Length()) / 10f;
            if (Engine.FreezeTimer > 0f)
            {
                target = hasSmoothedMadelineLens ? smoothedMadelineLensAmount : target;
            }
        }
        else
        {
            target = MathHelper.Clamp(settings.MadelineLensLevel, 0, 10) / 10f;
        }

        if (settings.MadelineLens == MadelineLensMode.Speed)
        {
            if (!hasSmoothedMadelineLens)
            {
                smoothedMadelineLensAmount = target;
                hasSmoothedMadelineLens = true;
            }
            else if (Engine.FreezeTimer > 0f)
            {
                // Hold the pre-freeze amount.
            }
            else
            {
                float dt = MathHelper.Clamp(Engine.DeltaTime, 0f, 1f / 30f);
                float rate = target > smoothedMadelineLensAmount ? 18f : 3f;
                float blend = 1f - MathF.Exp(-rate * dt);
                smoothedMadelineLensAmount = MathHelper.Lerp(smoothedMadelineLensAmount, target, blend);
            }
        }
        else
        {
            smoothedMadelineLensAmount = target;
            hasSmoothedMadelineLens = true;
        }

        float lensAmount = smoothedMadelineLensAmount;
        if (lensAmount <= 0.005f)
        {
            return null;
        }

        // Radius grows faster than before; the exponent and the flatter
        // shader falloff make the central high-strength area much larger.
        float radius = MathHelper.Lerp(MadelineMinLensRadius, MadelineMaxLensRadius, lensAmount);
        float strength = MathF.Pow(lensAmount, 0.70f);
        // Grow the core continuously with the lens amount; 16 px at max.
        float coreRadius = 16f * lensAmount;
        return new MadelineLens(screenCenter, radius, strength, coreRadius);
    }

    private static float MadelineSpeedLevel(float speed)
    {
        if (speed <= 200f)
        {
            return 0f;
        }

        if (speed <= 800f)
        {
            return MathHelper.Lerp(0f, 4f, (speed - 200f) / 600f);
        }

        if (speed <= 1300f)
        {
            return MathHelper.Lerp(4f, 6f, (speed - 800f) / 500f);
        }

        if (speed <= MadelineMaxSpeed)
        {
            return MathHelper.Lerp(6f, 10f, (speed - 1300f) / (MadelineMaxSpeed - 1300f));
        }

        return 10f;
    }

    private static void ApplyMadelineLens(
        GraphicsDevice device,
        RenderTarget2D sourceTarget,
        MadelineLens lens,
        float time)
    {
        if (madelineLensEffect == null || madelineLensFailed)
        {
            return;
        }

        device.SetRenderTarget(capture);
        device.Clear(Color.Black);
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(sourceTarget, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        device.SetRenderTarget(sourceTarget);
        device.Clear(Color.Black);

        madelineLensEffect.Parameters["LensCenter"].SetValue(lens.Center);
        madelineLensEffect.Parameters["LensRadius"].SetValue(lens.Radius);
        madelineLensEffect.Parameters["LensStrength"].SetValue(lens.Strength);
        madelineLensEffect.Parameters["LensFalloff"].SetValue(1.25f);
        madelineLensEffect.CurrentTechnique = madelineLensEffect.Techniques["MadelineLens"];

        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone, madelineLensEffect);
        Draw.SpriteBatch.Draw(capture, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();
    }

    private static bool EnsureResources(RenderTarget2D sourceTarget)
    {
        if (capture == null || capture.IsDisposed ||
            capture.Width != sourceTarget.Width || capture.Height != sourceTarget.Height)
        {
            capture?.Dispose();
            capture = new RenderTarget2D(
                Engine.Instance.GraphicsDevice,
                sourceTarget.Width,
                sourceTarget.Height,
                mipMap: false,
                SurfaceFormat.Color,
                DepthFormat.None,
                0,
                RenderTargetUsage.DiscardContents);
        }

        if (effect == null && !effectFailed)
        {
            effect = LoadEffect("Celeste.Mod.BlackTheo.BlackHole.cso", "BlackHole.cso", ref effectFailed);
        }

        if (madelineLensEffect == null && !madelineLensFailed)
        {
            madelineLensEffect = LoadEffect(
                "Celeste.Mod.BlackTheo.MadelineLens.cso",
                "MadelineLens.cso",
                ref madelineLensFailed);
        }

        return effect != null;
    }

    private static Effect? LoadEffect(string resourceName, string displayName, ref bool failed)
    {
        try
        {
            Assembly assembly = typeof(BlackHoleRenderer).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidOperationException($"Embedded {displayName} was not found.");
            }

            using MemoryStream memory = new MemoryStream();
            stream.CopyTo(memory);
            Effect loaded = new Effect(Engine.Instance.GraphicsDevice, memory.ToArray());
            Logger.Log(LogLevel.Info, LogTag, $"{displayName} shader loaded.");
            return loaded;
        }
        catch (Exception e)
        {
            failed = true;
            Logger.Log(LogLevel.Error, LogTag, $"Failed to load {displayName}.\n{e}");
            return null;
        }
    }

    private static void SetGlobalParameters(int width, int height)
    {
        SetGlobalParameters(effect, width, height);
        SetGlobalParameters(madelineLensEffect, width, height);
    }

    private static void SetGlobalParameters(Effect? shader, int width, int height)
    {
        if (shader == null)
        {
            return;
        }

        shader.Parameters["MatrixTransform"].SetValue(
            Matrix.CreateScale(2f / width, -2f / height, 1f) *
            Matrix.CreateTranslation(-1f, 1f, 0f));
        shader.Parameters["ScreenSize"].SetValue(new Vector2(width, height));
    }

    public static void DisableAfterFailure(Exception exception)
    {
        renderFailed = true;
        Logger.Log(LogLevel.Error, LogTag,
            "Screen-space black hole disabled after a rendering error; gameplay is unaffected.\n" + exception);
    }

    public static void Dispose()
    {
        effect?.Dispose();
        effect = null;
        madelineLensEffect?.Dispose();
        madelineLensEffect = null;
        capture?.Dispose();
        capture = null;
        Holes.Clear();
        hasSmoothedMadelineLens = false;
        smoothedMadelineLensAmount = 0f;
        effectFailed = false;
        madelineLensFailed = false;
        renderFailed = false;
    }
}
