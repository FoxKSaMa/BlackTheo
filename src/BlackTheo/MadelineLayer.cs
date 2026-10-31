using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.BlackTheo;

/// <summary>
/// Experimental Madeline extraction.
///
/// Player.Render draws normally into Gameplay, but first writes a silhouette
/// into a mask RT. After GameplayRenderer.Render, the masked pixels are copied
/// out of Gameplay into PlayerLayer and then removed from Gameplay.
/// </summary>
internal static class MadelineLayer
{
    private static RenderTarget2D? mask;
    private static RenderTarget2D? playerLayer;
    private static RenderTarget2D? litLayer;
    private static RenderTarget2D? distortedLayer;
    private static RenderTarget2D? distortMask;
    private static RenderTarget2D? occlusionRT;
    private static RenderTarget2D? backdropLayer;
    private static RenderTarget2D? frontMask;
    private static RenderTarget2D? restoreLayer;
    private static VirtualRenderTarget? playerBloom;
    private static int captureDepth;
    private static bool hasMask;
    private static bool hasLit;
    private static bool hasDistorted;
    private static bool occlusionCaptured;
    private static bool hasBackdropLayer;
    private static bool applyingDistort;
    private static bool applyingPlayerBloom;

    private static readonly BlendState PremultipliedOver = new()
    {
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.InverseSourceAlpha,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.InverseSourceAlpha,
        ColorBlendFunction = BlendFunction.Add,
        AlphaBlendFunction = BlendFunction.Add
    };

    // Keep destination alpha where the mask is zero, and clear it where the
    // mask is one. This is the inverse of MaskMultiply.
    private static readonly BlendState MaskMultiply = new()
    {
        ColorSourceBlend = Blend.Zero,
        ColorDestinationBlend = Blend.SourceAlpha,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.SourceAlpha,
        ColorBlendFunction = BlendFunction.Add,
        AlphaBlendFunction = BlendFunction.Add
    };

    private static readonly BlendState ClearMasked = new()
    {
        ColorSourceBlend = Blend.Zero,
        ColorDestinationBlend = Blend.InverseSourceAlpha,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.InverseSourceAlpha,
        ColorBlendFunction = BlendFunction.Add,
        AlphaBlendFunction = BlendFunction.Add
    };

    public static RenderTarget2D? Mask => mask;

    public static RenderTarget2D? PlayerLayer => playerLayer;

    public static RenderTarget2D? LitLayer => litLayer;

    public static RenderTarget2D? DistortedLayer => distortedLayer;

    public static RenderTarget2D? DistortMask => distortMask;

    public static bool IsApplyingDistort => applyingDistort;

    public static bool IsApplyingPlayerBloom => applyingPlayerBloom;

    public static bool IsCapturing => captureDepth > 0;

    public static void BeginFrame()
    {
        hasMask = false;
        hasLit = false;
        hasDistorted = false;
        occlusionCaptured = false;
        hasBackdropLayer = false;
        applyingDistort = false;
        applyingPlayerBloom = false;
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

    /// <summary>
    /// Called before the normal Player.Render. It only captures the alpha
    /// silhouette; the actual player is still rendered by orig(self).
    /// </summary>
    public static void CaptureMask(Player player)
    {
        if (captureDepth <= 0 ||
            player.Scene is not Level level ||
            !player.Visible ||
            player.Dead ||
            GameplayBuffers.Gameplay == null)
        {
            return;
        }

        EnsureTargets();
        if (mask == null || mask.IsDisposed)
        {
            return;
        }

        GraphicsDevice device = Engine.Instance.GraphicsDevice;

        // Flush everything drawn before Madeline in the current entity order.
        Draw.SpriteBatch.End();

        try
        {
            device.SetRenderTarget(mask);
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
        }
        finally
        {
            // Resume the normal entity-order batch. orig(self) draws the
            // visible player into Gameplay immediately after this method.
            device.SetRenderTarget(GameplayBuffers.Gameplay);
            Draw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.PointWrap,
                DepthStencilState.None,
                RasterizerState.CullNone,
                effect: null,
                transformMatrix: level.Camera.Matrix);
        }

        hasMask = true;
    }

    /// <summary>
    /// Runs after GameplayRenderer.Render. Copies the masked player pixels to
    /// PlayerLayer, then clears those pixels from Gameplay.
    /// </summary>
    public static void CaptureOcclusionBeforeEntity()
    {
        if (occlusionCaptured || !IsCapturing ||
            GameplayBuffers.Gameplay == null)
        {
            return;
        }

        EnsureTargets();
        if (occlusionRT == null || occlusionRT.IsDisposed)
        {
            return;
        }

        Level? level = Engine.Scene as Level;
        GraphicsDevice device = Engine.Instance.GraphicsDevice;

        Draw.SpriteBatch.End();
        try
        {
            device.SetRenderTarget(occlusionRT);
            device.Clear(Color.Transparent);
            Draw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.Opaque,
                SamplerState.PointClamp,
                DepthStencilState.None,
                RasterizerState.CullNone);
            Draw.SpriteBatch.Draw(GameplayBuffers.Gameplay, Vector2.Zero, Color.White);
            Draw.SpriteBatch.End();
        }
        finally
        {
            device.SetRenderTarget(GameplayBuffers.Gameplay);
            Draw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.PointWrap,
                DepthStencilState.None,
                RasterizerState.CullNone,
                effect: null,
                transformMatrix: level?.Camera.Matrix ?? Matrix.Identity);
        }

        occlusionCaptured = true;
    }

    public static void ExtractPlayerLayer()
    {
        if (!hasMask || mask == null || mask.IsDisposed ||
            GameplayBuffers.Gameplay == null)
        {
            return;
        }

        EnsureTargets();
        if (playerLayer == null || playerLayer.IsDisposed)
        {
            return;
        }

        GraphicsDevice device = Engine.Instance.GraphicsDevice;

        // Player.Render left the entity SpriteBatch active; close it before
        // switching render targets for the extraction passes.
        Draw.SpriteBatch.End();

        // 1. Copy Gameplay, then keep only the masked player pixels.
        device.SetRenderTarget(playerLayer);
        device.Clear(Color.Transparent);
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.Opaque,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(GameplayBuffers.Gameplay, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            MaskMultiply,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(mask, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        // 2. Remove those pixels from Gameplay so the player exists only in
        //    the separate layer until it is composited again.
        device.SetRenderTarget(GameplayBuffers.Gameplay);
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            ClearMasked,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(mask, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        // Build the masked pre-player snapshot. It is not drawn back yet;
        // ClipPlayerLayerAgainstFront restores it only where no lower-depth
        // entity was drawn afterwards.
        if (occlusionCaptured && occlusionRT != null && !occlusionRT.IsDisposed &&
            backdropLayer != null && !backdropLayer.IsDisposed)
        {
            device.SetRenderTarget(backdropLayer);
            device.Clear(Color.Transparent);
            Draw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.Opaque,
                SamplerState.PointClamp,
                DepthStencilState.None,
                RasterizerState.CullNone);
            Draw.SpriteBatch.Draw(occlusionRT, Vector2.Zero, Color.White);
            Draw.SpriteBatch.End();

            Draw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                MaskMultiply,
                SamplerState.PointClamp,
                DepthStencilState.None,
                RasterizerState.CullNone);
            Draw.SpriteBatch.Draw(mask, Vector2.Zero, Color.White);
            Draw.SpriteBatch.End();

            occlusionCaptured = false;
            hasBackdropLayer = true;
        }

        // Resume the entity batch for the remaining entities.
        Level? level = Engine.Scene as Level;
        device.SetRenderTarget(GameplayBuffers.Gameplay);
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointWrap,
            DepthStencilState.None,
            RasterizerState.CullNone,
            effect: null,
            transformMatrix: level?.Camera.Matrix ?? Matrix.Identity);
    }

    public static void ClipPlayerLayerAgainstFront()
    {
        if (!hasMask || mask == null || mask.IsDisposed ||
            playerLayer == null || playerLayer.IsDisposed ||
            frontMask == null || frontMask.IsDisposed ||
            GameplayBuffers.Gameplay == null)
        {
            return;
        }

        GraphicsDevice device = Engine.Instance.GraphicsDevice;

        // Gameplay currently contains only the entities rendered after the
        // player inside the player silhouette, because the player was cleared
        // and no backdrop was restored. Copy that alpha as the front mask.
        device.SetRenderTarget(frontMask);
        device.Clear(Color.Transparent);
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.Opaque,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(GameplayBuffers.Gameplay, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            MaskMultiply,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(mask, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        // Clip only the separated player RT; Gameplay is not modified here.
        device.SetRenderTarget(playerLayer);
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            ClearMasked,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(frontMask, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        // Restore the masked pre-player background only where no lower-depth
        // entity is present. This fills the transparent hole without
        // overwriting entities that were drawn after the player.
        if (hasBackdropLayer && backdropLayer != null && !backdropLayer.IsDisposed &&
            restoreLayer != null && !restoreLayer.IsDisposed)
        {
            device.SetRenderTarget(restoreLayer);
            device.Clear(Color.Transparent);
            Draw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.Opaque,
                SamplerState.PointClamp,
                DepthStencilState.None,
                RasterizerState.CullNone);
            Draw.SpriteBatch.Draw(backdropLayer, Vector2.Zero, Color.White);
            Draw.SpriteBatch.End();

            Draw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                ClearMasked,
                SamplerState.PointClamp,
                DepthStencilState.None,
                RasterizerState.CullNone);
            Draw.SpriteBatch.Draw(frontMask, Vector2.Zero, Color.White);
            Draw.SpriteBatch.End();

            device.SetRenderTarget(GameplayBuffers.Gameplay);
            Draw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                PremultipliedOver,
                SamplerState.PointWrap,
                DepthStencilState.None,
                RasterizerState.CullNone,
                effect: null,
                transformMatrix: Matrix.Identity);
            Draw.SpriteBatch.Draw(restoreLayer, Vector2.Zero, Color.White);
            Draw.SpriteBatch.End();
        }

        device.SetRenderTarget(GameplayBuffers.Gameplay);
    }

    public static void ApplyLighting(LightingRenderer lighting)
    {
        if (!hasMask || playerLayer == null || playerLayer.IsDisposed ||
            mask == null || mask.IsDisposed ||
            litLayer == null || litLayer.IsDisposed ||
            GameplayBuffers.Light == null ||
            GFX.FxDither == null)
        {
            return;
        }

        GraphicsDevice device = Engine.Instance.GraphicsDevice;

        // Apply the same lighting pass to the extracted player.
        device.SetRenderTarget(playerLayer);
        GFX.FxDither.CurrentTechnique = GFX.FxDither.Techniques["InvertDither"];
        GFX.FxDither.Parameters["size"].SetValue(
            new Vector2(GameplayBuffers.Light.Width, GameplayBuffers.Light.Height));
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            GFX.DestinationTransparencySubtract,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone,
            GFX.FxDither,
            Matrix.Identity);
        Draw.SpriteBatch.Draw(
            GameplayBuffers.Light,
            Vector2.Zero,
            Color.White * MathHelper.Clamp(lighting.Alpha, 0f, 1f));
        Draw.SpriteBatch.End();

        // Copy the lit player and re-apply the mask.
        device.SetRenderTarget(litLayer);
        device.Clear(Color.Transparent);
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.Opaque,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(playerLayer, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            MaskMultiply,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(mask, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        // Do not composite back here. The actual Distort.Render will be the
        // insertion point for the distorted player.
        hasLit = true;
        device.SetRenderTarget(GameplayBuffers.Gameplay);
    }

    public static void ApplyDistort()
    {
        if (!hasMask || litLayer == null || litLayer.IsDisposed ||
            distortedLayer == null || distortedLayer.IsDisposed ||
            distortMask == null || distortMask.IsDisposed ||
            GameplayBuffers.Displacement == null)
        {
            return;
        }

        Level? level = Engine.Scene as Level;
        GraphicsDevice device = Engine.Instance.GraphicsDevice;

        device.SetRenderTarget(distortedLayer);
        device.Clear(Color.Transparent);
        applyingDistort = true;
        try
        {
            Distort.Render(
                litLayer,
                GameplayBuffers.Displacement,
                level != null && level.Displacement.HasDisplacement(level));
        }
        finally
        {
            applyingDistort = false;
        }

        // The distorted alpha becomes the new screen-space mask.
        device.SetRenderTarget(distortMask);
        device.Clear(Color.Transparent);
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.Opaque,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(distortedLayer, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        hasDistorted = true;
        device.SetRenderTarget(GameplayBuffers.Gameplay);
    }

    public static void CompositeDistortedLayer()
    {
        if (!hasMask || distortedLayer == null || distortedLayer.IsDisposed ||
            GameplayBuffers.Level == null)
        {
            return;
        }

        GraphicsDevice device = Engine.Instance.GraphicsDevice;
        device.SetRenderTarget(GameplayBuffers.Level);
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            PremultipliedOver,
            SamplerState.PointWrap,
            DepthStencilState.None,
            RasterizerState.CullNone,
            effect: null,
            transformMatrix: Matrix.Identity);
        Draw.SpriteBatch.Draw(distortedLayer, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();
    }

    public static void ApplyBloomToPlayer(BloomRenderer bloom, Scene scene)
    {
        RenderTarget2D? source = null;
        if (hasDistorted && distortedLayer != null && !distortedLayer.IsDisposed)
        {
            source = distortedLayer;
        }
        else if (hasLit && litLayer != null && !litLayer.IsDisposed)
        {
            source = litLayer;
        }
        else if (playerLayer != null && !playerLayer.IsDisposed)
        {
            source = playerLayer;
        }

        if (!hasMask || source == null ||
            mask == null || mask.IsDisposed ||
            playerBloom == null || playerBloom.IsDisposed)
        {
            return;
        }

        GraphicsDevice device = Engine.Instance.GraphicsDevice;

        // Copy the distorted player into a VirtualRenderTarget so that the
        // original BloomRenderer.Apply can process it directly.
        device.SetRenderTarget(playerBloom.Target);
        device.Clear(Color.Transparent);
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.Opaque,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(source, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        applyingPlayerBloom = true;
        try
        {
            bloom.Apply(playerBloom, scene);
        }
        finally
        {
            applyingPlayerBloom = false;
        }

        // Bloom keeps the destination alpha, but re-apply the mask so only
        // player pixels remain in the final player bloom layer.
        device.SetRenderTarget(playerBloom.Target);
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            MaskMultiply,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(mask, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        device.SetRenderTarget(GameplayBuffers.Level);
    }

    public static bool CompositePlayerBloom()
    {
        if (!hasMask || playerBloom == null || playerBloom.IsDisposed ||
            GameplayBuffers.Level == null)
        {
            return false;
        }

        GraphicsDevice device = Engine.Instance.GraphicsDevice;
        device.SetRenderTarget(GameplayBuffers.Level);
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            PremultipliedOver,
            SamplerState.PointWrap,
            DepthStencilState.None,
            RasterizerState.CullNone,
            effect: null,
            transformMatrix: Matrix.Identity);
        Draw.SpriteBatch.Draw(playerBloom.Target, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();
        return true;
    }

    public static void CompositePlayerFallback()
    {
        if (!hasMask || GameplayBuffers.Level == null)
        {
            return;
        }

        RenderTarget2D? source = null;
        if (hasDistorted && distortedLayer != null && !distortedLayer.IsDisposed)
        {
            source = distortedLayer;
        }
        else if (hasLit && litLayer != null && !litLayer.IsDisposed)
        {
            source = litLayer;
        }
        else if (playerLayer != null && !playerLayer.IsDisposed)
        {
            source = playerLayer;
        }

        if (source == null)
        {
            return;
        }

        GraphicsDevice device = Engine.Instance.GraphicsDevice;
        device.SetRenderTarget(GameplayBuffers.Level);
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            PremultipliedOver,
            SamplerState.PointWrap,
            DepthStencilState.None,
            RasterizerState.CullNone,
            effect: null,
            transformMatrix: Matrix.Identity);
        Draw.SpriteBatch.Draw(source, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();
    }

    private static void EnsureTargets()
    {
        VirtualRenderTarget? gameplay = GameplayBuffers.Gameplay;
        if (gameplay == null)
        {
            return;
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
                RenderTargetUsage.PreserveContents);
        }

        if (playerLayer == null || playerLayer.IsDisposed ||
            playerLayer.Width != gameplay.Width || playerLayer.Height != gameplay.Height)
        {
            playerLayer?.Dispose();
            playerLayer = new RenderTarget2D(
                Engine.Instance.GraphicsDevice,
                gameplay.Width,
                gameplay.Height,
                mipMap: false,
                SurfaceFormat.Color,
                DepthFormat.None,
                0,
                RenderTargetUsage.PreserveContents);
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
                RenderTargetUsage.PreserveContents);
        }

        if (distortedLayer == null || distortedLayer.IsDisposed ||
            distortedLayer.Width != gameplay.Width || distortedLayer.Height != gameplay.Height)
        {
            distortedLayer?.Dispose();
            distortedLayer = new RenderTarget2D(
                Engine.Instance.GraphicsDevice,
                gameplay.Width,
                gameplay.Height,
                mipMap: false,
                SurfaceFormat.Color,
                DepthFormat.None,
                0,
                RenderTargetUsage.PreserveContents);
        }

        if (distortMask == null || distortMask.IsDisposed ||
            distortMask.Width != gameplay.Width || distortMask.Height != gameplay.Height)
        {
            distortMask?.Dispose();
            distortMask = new RenderTarget2D(
                Engine.Instance.GraphicsDevice,
                gameplay.Width,
                gameplay.Height,
                mipMap: false,
                SurfaceFormat.Color,
                DepthFormat.None,
                0,
                RenderTargetUsage.PreserveContents);
        }

        if (occlusionRT == null || occlusionRT.IsDisposed ||
            occlusionRT.Width != gameplay.Width || occlusionRT.Height != gameplay.Height)
        {
            occlusionRT?.Dispose();
            occlusionRT = new RenderTarget2D(
                Engine.Instance.GraphicsDevice,
                gameplay.Width,
                gameplay.Height,
                mipMap: false,
                SurfaceFormat.Color,
                DepthFormat.None,
                0,
                RenderTargetUsage.PreserveContents);
        }

        if (backdropLayer == null || backdropLayer.IsDisposed ||
            backdropLayer.Width != gameplay.Width || backdropLayer.Height != gameplay.Height)
        {
            backdropLayer?.Dispose();
            backdropLayer = new RenderTarget2D(
                Engine.Instance.GraphicsDevice,
                gameplay.Width,
                gameplay.Height,
                mipMap: false,
                SurfaceFormat.Color,
                DepthFormat.None,
                0,
                RenderTargetUsage.PreserveContents);
        }

        if (frontMask == null || frontMask.IsDisposed ||
            frontMask.Width != gameplay.Width || frontMask.Height != gameplay.Height)
        {
            frontMask?.Dispose();
            frontMask = new RenderTarget2D(
                Engine.Instance.GraphicsDevice,
                gameplay.Width,
                gameplay.Height,
                mipMap: false,
                SurfaceFormat.Color,
                DepthFormat.None,
                0,
                RenderTargetUsage.PreserveContents);
        }

        if (restoreLayer == null || restoreLayer.IsDisposed ||
            restoreLayer.Width != gameplay.Width || restoreLayer.Height != gameplay.Height)
        {
            restoreLayer?.Dispose();
            restoreLayer = new RenderTarget2D(
                Engine.Instance.GraphicsDevice,
                gameplay.Width,
                gameplay.Height,
                mipMap: false,
                SurfaceFormat.Color,
                DepthFormat.None,
                0,
                RenderTargetUsage.PreserveContents);
        }

        if (playerBloom == null || playerBloom.IsDisposed ||
            playerBloom.Width != gameplay.Width || playerBloom.Height != gameplay.Height)
        {
            playerBloom?.Dispose();
            playerBloom = VirtualContent.CreateRenderTarget(
                "BlackTheo/PlayerBloom",
                gameplay.Width,
                gameplay.Height,
                depth: false,
                preserve: true);
        }
    }

    public static void Dispose()
    {
        mask?.Dispose();
        mask = null;
        playerLayer?.Dispose();
        playerLayer = null;
        litLayer?.Dispose();
        litLayer = null;
        distortedLayer?.Dispose();
        distortedLayer = null;
        distortMask?.Dispose();
        distortMask = null;
        occlusionRT?.Dispose();
        occlusionRT = null;
        backdropLayer?.Dispose();
        backdropLayer = null;
        frontMask?.Dispose();
        frontMask = null;
        restoreLayer?.Dispose();
        restoreLayer = null;
        playerBloom?.Dispose();
        playerBloom = null;
        captureDepth = 0;
        hasMask = false;
        applyingDistort = false;
        applyingPlayerBloom = false;
    }
}
