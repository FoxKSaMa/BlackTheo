using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.BlackTheo;

/// <summary>
/// 实验性玛德琳提取。
///
/// Player.Render 先写入仅含 alpha 剪影的 mask，再正常画进 Gameplay；
/// 随后立刻把玩家像素提取到 PlayerLayer，并从 Gameplay 中清除。
/// GameplayRenderer 结束时再裁剪与玩家重叠的低深度像素。
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

    // mask 为 0 时保留目标 alpha，为 1 时清除目标；
    // 这是 MaskMultiply 的逆操作。
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
    /// 在正常的 Player.Render 之前调用，只捕获 alpha 剪影；
    /// 实际玩家仍由 orig(self) 渲染。
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

        // 刷新当前实体顺序中、玛德琳之前已经绘制的内容。
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
            // 恢复正常的实体批次；orig(self) 会紧接着把可见玩家画进 Gameplay。
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
    /// 在 Player.Render 之后执行：把带 mask 的玩家像素复制到 PlayerLayer，
    /// 再从 Gameplay 中清除。
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

        // Player.Render 留下了实体 SpriteBatch；切换 RenderTarget 前先结束它。
        Draw.SpriteBatch.End();

        // 1. 复制 Gameplay，只保留 mask 覆盖的玩家像素。
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

        // 2. 从 Gameplay 中移除这些像素，玩家只存在于独立图层，
        //    等待后续重新合成。
        device.SetRenderTarget(GameplayBuffers.Gameplay);
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            ClearMasked,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(mask, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        // 生成玩家渲染前的 masked 快照，但暂不画回；
        // ClipPlayerLayerAgainstFront 只会在没有更低深度实体的地方恢复它。
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

        // 恢复实体批次，让后续实体继续渲染。
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

        // 此时 Gameplay 中只剩下玩家剪影内、玩家之后渲染的实体，
        // 因为玩家已被清除且没有恢复背景。把这份 alpha 复制为 front mask。
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

        // 只裁剪分离出的玩家 RT，不修改 Gameplay。
        device.SetRenderTarget(playerLayer);
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            ClearMasked,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(frontMask, Vector2.Zero, Color.White);
        Draw.SpriteBatch.End();

        // 只恢复没有被更低深度实体覆盖的、玩家之前的背景。
        // 这样补上透明洞，又不会覆盖玩家之后绘制的实体。
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

        // 对提取出的玩家应用同一份光照。
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

        // 复制打光后的玩家，并重新应用 mask。
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

        // 这里不合成；真正的 Distort.Render 才是插入点。
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

        // Distort 后的 alpha 作为新的屏幕空间 mask。
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

        // 把 Distort 后的玩家复制到 VirtualRenderTarget，
        // 交给原版 BloomRenderer.Apply 直接处理。
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

        // Bloom 保留目标 alpha；再应用一次 mask，只留下玩家像素。
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
