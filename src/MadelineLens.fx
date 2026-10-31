// MadelineLens.fx
// 只实现屏幕空间引力透镜：不画事件视界、吸积盘、光子环或星尘。
// 输入是一张已经合成好的屏幕纹理，输出只有径向折射后的画面。

sampler ScreenSampler : register(s0);

float4x4 MatrixTransform;
float2 ScreenSize;

float2 LensCenter;   // 屏幕像素坐标
float LensRadius;    // 透镜影响半径（像素）
float LensStrength;  // 0..1
float LensFalloff;   // 径向衰减指数

void SpriteVertexShader(
    inout float4 color    : COLOR0,
    inout float2 texCoord : TEXCOORD0,
    inout float4 position : SV_Position)
{
    position = mul(position, MatrixTransform);
}

float4 PS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 pixel = uv * ScreenSize;
    float2 d = pixel - LensCenter;
    float r = length(d);
    float radius = max(LensRadius, 1.0);

    float4 color = tex2D(ScreenSampler, uv);

    if (r < radius)
    {
        float t = saturate(r / radius);
        float falloff = pow(1.0 - t, max(LensFalloff, 0.01));
        float2 dir = d / max(r, 0.0001);
        float strength = saturate(LensStrength);

        float2 offset = -dir * (radius * 0.42 * strength) * falloff;

        float offsetMag = length(offset);
        if (offsetMag > 36.0)
        {
            offset *= 36.0 / offsetMag;
        }

        color.rgb = tex2D(
            ScreenSampler,
            saturate((pixel + offset) / ScreenSize)).rgb;
    }

    color.a = 1.0;
    return color;
}

technique MadelineLens
{
    pass Pass0
    {
        VertexShader = compile vs_3_0 SpriteVertexShader();
        PixelShader  = compile ps_3_0 PS();
    }
}
