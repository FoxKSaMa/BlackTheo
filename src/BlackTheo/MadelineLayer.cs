using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.BlackTheo;

/// <summary>
/// Experimental player-layer capture.
///
/// While EntityList renders, Player.Render is redirected into a transparent
/// layer target, copied into a mask target, and the layer is composited back
/// into Gameplay at the same point in the entity order.
/// </summary>
internal static class MadelineLayer
{
    private static RenderTarget2D? layer;
    private static RenderTarget2D? mask;
    private static RenderTarget2D? litLayer;
    private static int captureDepth;
    private static bool hasMask;

    private static readonly BlendState PremultipliedOver = new()
    {
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.InverseSourceAlpha,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.InverseSourceAlpha,
        ColorBlendFunction = BlendFunction.Add,
        AlphaBlendFunction = BlendFunction.Add
    };

    // Incoming mask alpha multiplies the already-lit destination. This keeps
    // only the Madeline pixels and zeroes both RGB and alpha everywhere else.
    private static readonly BlendState MaskMultiply = new()
    {
        ColorSourceBlend = Blend.Zero,
        ColorDestinationBlend = Blend.SourceAlpha,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.SourceAlpha,
        ColorBlendFunction = BlendFunction.Add,
        AlphaBlendFunction = BlendFunction.Add
    };

    public static RenderTarget2D? Mask => mask;

    public static RenderTarget2D? LitLayer => litLayer;

    public static bool IsCapturing => captureDepth > 0;

    public static void BeginFrame()
    {
        hasMask = false;
    }

    public static void BeginCapture()
    {
        captureDepth++;
    }

    public static void EndCapture()
    {
        if (captureDepth > 0)
        {
            captureDepth--;
        }
    }

    public static bool TryCapture(Player player)
    {
        if (captureDepth <= 0 ||
            player.Scene is not Level level ||
            !player.Visible ||
            player.Dead ||
            GameplayBuffers.Gameplay == null)
        {
            return false;
        }

        EnsureTargets();
        if (layer == null || mask == null)
        {
            return false;
        }

        GraphicsDevice device = Engine.Instance.GraphicsDevice;

        // Flush everything drawn before Madeline in the current entity order.
        Draw.SpriteBatch.End();

        try
        {
            // 1. Render Madeline alone into a transparent layer.
            device.SetRenderTarget(layer);
            device.Clear(Color.Transparent);
            Draw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.PointClamp,
                DepthStencilState.None,
                RasterizerState.CullNone,
                effect: null,
                transformMatrix: level.Camera.Matrix);
            Vector2 oldPosition = player.Sprite.RenderPosition;
            player.Sprite.RenderPosition = player.Sprite.RenderPosition.Floor();
            player.Sprite.Scale.X *= (float)player.Facing;
            player.Hair.Render();
            player.Sprite.Render();
            player.Sprite.Scale.X *= (float)player.Facing;
            player.Sprite.RenderPosition = oldPosition;
            Draw.SpriteBatch.End();

            // 2. Copy the layer. Its alpha is the exact screen-space mask.
            device.SetRenderTarget(mask);
            device.Clear(Color.Transparent);
            Draw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.Opaque,
                SamplerState.PointClamp,
                DepthStencilState.None,
                RasterizerState.CullNone);
            Draw.SpriteBatch.Draw(layer, Vector2.Zero, Color.White);
            Draw.SpriteBatch.End();

            hasMask = true;
        }
        finally
        {
            // 3. Composite the captured layer back into Gameplay at the same
            //    entity-order position. The layer contains premultiplied color
            //    because it was rendered over a transparent target.
            device.SetRenderTarget(GameplayBuffers.Gameplay);
            Draw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                PremultipliedOver,
                SamplerState.PointWrap,
                DepthStencilState.None,
                RasterizerState.CullNone,
                effect: null,
                transformMatrix: Matrix.Identity);
            Draw.SpriteBatch.Draw(layer, Vector2.Zero, Color.White);
            Draw.SpriteBatch.End();

            // Resume the normal entity-order batch for subsequent entities.
            Draw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.PointWrap,
                DepthStencilState.None,
                RasterizerState.CullNone,
                effect: null,
                transformMatrix: level.Camera.Matrix);
        }

        return true;
    }

    public static void CaptureLitLayer()
    {
        if (!hasMask || mask == null || mask.IsDisposed ||
            GameplayBuffers.Gameplay == null)
        {
            return;
        }

        EnsureTargets();
        if (litLayer == null || litLayer.IsDisposed)
        {
            return;
        }

        GraphicsDevice device = Engine.Instance.GraphicsDevice;

        // 1. Copy the lit Gameplay target.
        device.SetRenderTarget(litLayer);
        device.Clear(Color.Transparent);
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.Opaque,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(GameplayBuffers.Gameplay, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        // 2. Keep only pixels covered by the saved Madeline mask.
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            MaskMultiply,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(mask, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        // Lighting.Render ran against Gameplay; restore that target so the
        // following Level.Render stage behaves normally.
        device.SetRenderTarget(GameplayBuffers.Gameplay);
    }

    private static void EnsureTargets()
    {
        VirtualRenderTarget? gameplay = GameplayBuffers.Gameplay;
        if (gameplay == null)
        {
            return;
        }

        if (layer == null || layer.IsDisposed ||
            layer.Width != gameplay.Width || layer.Height != gameplay.Height)
        {
            layer?.Dispose();
            layer = new RenderTarget2D(
                Engine.Instance.GraphicsDevice,
                gameplay.Width,
                gameplay.Height,
                mipMap: false,
                SurfaceFormat.Color,
                DepthFormat.None,
                0,
                RenderTargetUsage.DiscardContents);
        }

        if (mask == null || mask.IsDisposed ||
            mask.Width != gameplay.Width || mask.Height != gameplay.Height)
        {
            mask?.Dispose();
            mask = new RenderTarget2D(
                Engine.Instance.GraphicsDevice,
                gameplay.Width,
                gameplay.Height,
                mipMap: false,
                SurfaceFormat.Color,
                DepthFormat.None,
                0,
                RenderTargetUsage.DiscardContents);
        }

        if (litLayer == null || litLayer.IsDisposed ||
            litLayer.Width != gameplay.Width || litLayer.Height != gameplay.Height)
        {
            litLayer?.Dispose();
            litLayer = new RenderTarget2D(
                Engine.Instance.GraphicsDevice,
                gameplay.Width,
                gameplay.Height,
                mipMap: false,
                SurfaceFormat.Color,
                DepthFormat.None,
                0,
                RenderTargetUsage.DiscardContents);
        }
    }

    public static void Dispose()
    {
        layer?.Dispose();
        layer = null;
        mask?.Dispose();
        mask = null;
        litLayer?.Dispose();
        litLayer = null;
        captureDepth = 0;
        hasMask = false;
    }
}
