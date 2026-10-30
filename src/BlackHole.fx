// BlackTheo —— 为 Celeste 制作的小型、具有物理形态的黑洞。
//
// 吸积盘不是屏幕空间的贴图椭圆，而是位于 y=0 平面的薄盘；
// 我们围绕一个类史瓦西质量积分零测地线，并记录光线与盘面的交点。
// 由此得到类似《星际穿越》/卡冈图雅的形态：
//   * 近侧盘面从阴影前方穿过，形成一条细带；
//   * 远侧盘面被弯折到阴影的上下方，形成明亮弧线；
//   * 阴影边缘贴着一圈很细的光子环。
//
// 同一批积分光线也用于屏幕空间透镜：逃逸光线会按照它实际离开
// 场景的方向，去采样已经合成好的游戏画面，并用一段较短的衰减
// 让附近的物体仍然可以辨认。
//
// 参考：
//   - sayed1145/glsl-black-hole（MIT）：盘面亮度与域扭曲丝状纹理。
//   - Gitee“黑洞回收站”：基于偏折的屏幕透镜与紧凑构图。
//   - Luminet (1979)、James et al. (2015)：薄盘透镜几何。

sampler ScreenSampler : register(s0);

float4x4 MatrixTransform;
float2 ScreenSize;
float4 Hole;         // xy = 游戏像素中的中心，z = 阴影半径，w = 透镜半径（px）
float4 HoleParams;   // Theo：x = 盘亮度，y = 透镜位移缩放，z = 偏移上限（px），w = 时间
                     // 玛德琳：y = 强度，z = 黑核半径，w = 时间
float DopplerEnabled;
float SpecksEnabled;
float NoiseSeed;
float LensFalloff;
float LensEdge;
float DiskAlpha;
float LensOnly;      // 1 = 玛德琳专用透镜分支（不画盘/阴影/星尘）
float4 DiskInnerColor;
float4 DiskMidColor;
float4 DiskOuterColor;
float4 PhotonColor;
float4 RingColor;    // 外围散射 / 光晕颜色

#define TAU 6.28318530718
#define PI 3.14159265

// 二维哈希：为噪声、星尘和抖动提供稳定的伪随机值。
float hash21(float2 p)
{
    p = fmod(p, 128.0);
    float3 q = frac(float3(p.x, p.y, p.x) * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

// 带角向周期的二维值噪声；period 让盘面在 -pi/pi 接缝处保持连续。
float noiseP(float2 p, float period)
{
    p = fmod(p, 128.0);
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float2 i0 = float2(i.x, fmod(i.y, period));
    float2 i1 = float2(i.x, fmod(i.y + 1.0, period));
    float a = hash21(i0);
    float b = hash21(i0 + float2(1.0, 0.0));
    float c = hash21(i1);
    float d = hash21(i1 + float2(1.0, 0.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// 三层分形噪声，用来生成盘面的丝状细节。
float fbmP(float2 p, float period)
{
    float sum = 0.0;
    float amp = 0.52;
    for (int i = 0; i < 3; i++)
    {
        sum += amp * noiseP(p, period);
        p *= 2.13;
        period *= 2.0;
        amp *= 0.49;
    }
    return sum;
}

float3 diskShade(float radius, float angle, float time)
{
    float t = saturate((radius - 2.0) / 8.5);
    float3 c = lerp(DiskInnerColor.rgb, DiskMidColor.rgb, smoothstep(0.02, 0.45, t));
    c = lerp(c, DiskOuterColor.rgb, smoothstep(0.48, 1.0, t));

    // 无缝的域扭曲丝状纹理。角坐标使用整数周期，
    // 因此 -pi/pi 接缝在数学上闭合。
    // 负向流动会反转盘面动画：多普勒相位下更亮的左侧
    // 就成为朝向观察者的一侧。
    float a = angle / TAU + 0.5;
    float flow = -time * 0.35;
    float warp = fbmP(float2(radius * 0.75 + 3.1, a * 8.0 - flow * 1.7), 8.0);
    float filament = fbmP(float2(radius * 1.55 + 17.0, a * 16.0 - flow * 3.2 + (warp - 0.5) * 1.7), 16.0);
    float fine = fbmP(float2(radius * 3.2 + 2.0, a * 32.0 - flow * 6.0 + (filament - 0.5) * 0.9), 32.0);

    float density = saturate(0.22 + 1.35 * filament);
    density *= 0.62 + 0.85 * fine;

    // 平衡的多普勒明暗：朝向观察者的一侧更亮，远离的一侧更暗，
    // 平均值保持 1.0，避免整张盘变暗。
    float doppler = lerp(1.0, 1.0 + 0.30 * cos(angle - 0.55), DopplerEnabled);
    return c * density * doppler;
}

// 阴影内部零散漂浮的彩色像素；只在吸积盘没有覆盖的地方显示。
// 位置由每个 Theo 的 NoiseSeed 确定，并缓慢绕转，
// 因此始终有一小群稳定存在的星尘。
float3 horizonSpecks(float2 pixel, float time, float shadowRadius, float3 ca, float3 cb, float3 cc)
{
    float3 result = float3(0.0, 0.0, 0.0);

    for (int i = 0; i < 3; i++)
    {
        float fi = (float)i;
        float h = hash21(float2(fi * 7.13, NoiseSeed * 0.013 + 3.7));

        float angle = fi * 2.39996 + time * (0.12 + 0.12 * h) + h * 6.2831853;
        float radius = shadowRadius * (0.20 + 0.52 * h);
        float2 center = Hole.xy + float2(cos(angle), sin(angle)) * radius;
        center += float2(
            sin(time * 0.9 + h * 20.0),
            cos(time * 0.75 + h * 15.0)) * 0.7;

        float d = length(pixel - center);
        float dotMask = 1.0 - smoothstep(0.30, 0.95, d);
        if (dotMask <= 0.0)
        {
            continue;
        }

        float pick = hash21(float2(fi * 2.71 + 1.3, NoiseSeed * 0.007 + 9.2));
        float3 col = pick < 0.40 ? ca : (pick < 0.72 ? cb : float3(0.92, 0.96, 1.0));
        float twinkle = 0.65 + 0.25 * sin(time * 1.9 + h * 30.0);
        result += col * dotMask * twinkle * 0.9;
    }

    return result;
}

// 类史瓦西零测地线加速度；h2 是角动量平方。
float3 LensAccel(float3 p, float h2)
{
    float r2 = dot(p, p);
    return -1.5 * h2 * p / (r2 * r2 * sqrt(r2));
}

float4 PS(float2 uv : TEXCOORD0) : COLOR0
{
    float2 pixel = uv * ScreenSize;
    float2 delta = pixel - Hole.xy;
    float screenRadius = length(delta);
    float shadowRadius = max(Hole.z, 1.0);
    float lensRadius = max(Hole.w, shadowRadius + 3.0);
    float time = HoleParams.w;

    float4 color = tex2D(ScreenSampler, uv);

    // 玛德琳专用的纯透镜分支。这里刻意不画吸积盘和光子环，
    // 只弯折已经合成好的屏幕；中心由单独的黑核遮住。
    if (LensOnly > 0.5)
    {
        float2 d = pixel - Hole.xy;
        float r = length(d);
        float radius = max(Hole.w, 1.0);

        if (r < radius)
        {
            float t = saturate(r / radius);
            float falloff = pow(1.0 - t, 1.25);
            float2 dir = d / max(r, 0.0001);
            float strength = saturate(HoleParams.y);

            // 纯径向透镜：引力透镜应保持对称；旧的切向项会让它像漩涡。
            float2 offset = -dir * (radius * 0.42 * strength) * falloff;

            float offsetMag = length(offset);
            if (offsetMag > 36.0)
            {
                offset *= 36.0 / offsetMag;
            }

            color.rgb = tex2D(
                ScreenSampler,
                saturate((pixel + offset) / ScreenSize)).rgb;

            // 径向扭曲在中心附近会折叠；黑核负责遮住中间的空心环，
            // 其半径随透镜档位缩放。
            float coreRadius = max(HoleParams.z, 0.0);
            color.rgb *= smoothstep(coreRadius - 0.75, coreRadius + 0.75, r);
        }

        color.a = 1.0;
        return color;
    }

    if (screenRadius < lensRadius)
    {
        // 把期望的屏幕阴影半径映射到临界碰撞参数 b_c = 2.598。
        // 以实测 9.6px 为参考，可得到与阴影大小无关的稳定 FOV 缩放。
        float fovScale = 9.6 / shadowRadius;
        float2 uvWin = (pixel - Hole.xy) / ScreenSize.y;
        uvWin.y = -uvWin.y;
        float2 uvw = uvWin * fovScale;

        float3 ro = float3(0.0, 11.0, -59.8);
        float3 ta = float3(0.0, 0.0, 0.0);
        float3 fw = normalize(ta - ro);
        float3 rt = normalize(cross(fw, float3(0.0, 1.0, 0.0)));
        float3 up = cross(rt, fw);

        // 屏幕空间 roll：让吸积盘不再是一条水平扁带。
        float roll = 0.32;
        float cr = cos(roll);
        float sr = sin(roll);
        float3 rtRolled = rt * cr + up * sr;
        float3 upRolled = up * cr - rt * sr;
        float3 rd0 = normalize(fw * 1.2 + uvw.x * rtRolled + uvw.y * upRolled);

        float3 pos = ro;
        float3 v = rd0;
        // 抖动积分起点，打散步长量化。
        pos += v * (hash21(pixel) * 0.11);
        float3 hv = cross(pos, v);
        float h2 = dot(hv, hv);

        float4 hit0 = float4(0.0, 0.0, 0.0, 0.0);
        float4 hit1 = float4(0.0, 0.0, 0.0, 0.0);
        float4 hit2 = float4(0.0, 0.0, 0.0, 0.0);
        int hits = 0;
        float captured = 0.0;
        float3 prevP = pos;

        [loop]
        for (int i = 0; i < 220; i++)
        {
            float r2 = dot(pos, pos);
            float r = sqrt(r2);
            if (r < 1.0)
            {
                captured = 1.0;
                break;
            }
            if (r > 90.0 && dot(pos, v) > 0.0)
            {
                break;
            }

            float dt = 0.07 + 0.07 * clamp(r - 1.0, 0.0, 8.0);
            v += LensAccel(pos, h2) * dt;
            prevP = pos;
            pos += v * dt;

            if (pos.y * prevP.y < 0.0)
            {
                float f = prevP.y / (prevP.y - pos.y);
                float3 hit = lerp(prevP, pos, f);
                float hr = length(hit.xz);
                if (hr > 2.0 && hr < 10.5)
                {
                    float4 data = float4(hr, atan2(hit.z, hit.x), abs(normalize(v).y), 1.0);
                    if (hits == 0) hit0 = data;
                    else if (hits == 1) hit1 = data;
                    else if (hits == 2) hit2 = data;
                    hits++;
                    if (hits >= 3) break;
                }
            }
        }

        float3 diskCol = float3(0.0, 0.0, 0.0);
        float trans = 1.0;
        if (hits > 0)
        {
            diskCol += diskShade(hit0.x, hit0.y, time) * trans;
            trans *= 0.52;
            if (hits > 1)
            {
                diskCol += diskShade(hit1.x, hit1.y, time) * trans;
                trans *= 0.52;
            }
            if (hits > 2)
            {
                diskCol += diskShade(hit2.x, hit2.y, time) * trans;
            }
        }

        // 细光子环和调色板颜色的微弱光晕。
        float photon = 1.0 - smoothstep(0.18, 0.95, abs(screenRadius - (shadowRadius + 0.55)));
        float3 ring = PhotonColor.rgb * photon * 1.35;

        // 屏幕空间透镜：靠近光子球时更强，在更大半径上逐渐减弱；
        // 只有最内侧像素会被强烈涂抹。
        float3 background = float3(0.0, 0.0, 0.0);
        if (captured < 0.5)
        {
            float3 finalV = normalize(v);
            float3 deflection = finalV - rd0;
            float2 devScr = float2(dot(deflection, rtRolled), dot(deflection, upRolled));

            float ratio = shadowRadius / max(screenRadius, shadowRadius);
            float falloff = pow(ratio, LensFalloff);
            float edge = 1.0 - smoothstep(lensRadius * LensEdge, lensRadius, screenRadius);
            float lensScale = HoleParams.y;
            float lensCap = HoleParams.z;
            float2 offsetPixels = devScr * (ScreenSize.y * lensScale) * falloff * edge;

            float offsetMag = length(offsetPixels);
            if (lensCap > 0.0 && offsetMag > lensCap)
            {
                offsetPixels *= lensCap / offsetMag;
            }

            offsetPixels.y = -offsetPixels.y;
            background = tex2D(ScreenSampler, saturate((pixel + offsetPixels) / ScreenSize)).rgb;
        }

        float shadow = 1.0 - smoothstep(shadowRadius - 0.75, shadowRadius + 0.75, screenRadius);

        // 稀疏彩色像素填充阴影中的黑色区域。
        // 它们先于盘面叠加，因此近侧盘面会自然遮住它们。
        float3 specks = float3(0.0, 0.0, 0.0);
        if (SpecksEnabled > 0.5 && shadow > 0.01)
        {
            specks = horizonSpecks(
                pixel, time, shadowRadius,
                DiskInnerColor.rgb,
                PhotonColor.rgb,
                DiskMidColor.rgb);
        }

        float3 scene = background * (1.0 - shadow);
        float diskLum = dot(diskCol, float3(0.299, 0.587, 0.114));
        float bgLum = dot(background, float3(0.299, 0.587, 0.114));

        // 少量覆盖混合让盘面在明亮地图上仍然可见，
        // 也让它的行为更像材质而不是纯加法凝胶。
        // 亮背景增强是有意保持克制的。
        float bright = saturate(bgLum);
        float coverage = saturate(diskLum * HoleParams.x * 1.55 * DiskAlpha * (1.0 + bright * 0.45));
        float dither = hash21(pixel + 13.7) - 0.5;
        coverage = saturate(coverage + dither * 0.04);

        // 柔和的像素风量化：10 级，只部分应用，
        // 用来打散过于平滑的 fBm，同时避免硬色带。
        float3 diskTint = diskCol * HoleParams.x * DiskAlpha;
        float3 quant = floor(diskTint * 10.0 + 0.5) / 10.0;
        diskTint = lerp(diskTint, quant, 0.30);

        float coverageBlend = 0.68 + 0.12 * bright;
        float coverageDarken = 0.18 + 0.10 * bright;
        float3 base = scene * (1.0 - coverageDarken * coverage) + specks * shadow;
        color.rgb = lerp(base, diskTint, coverage * coverageBlend)
                  + diskTint * (0.32 * coverage);
        color.rgb += ring * (1.0 - shadow) * DiskAlpha;
    }

    color.a = 1.0;
    return color;
}

void SpriteVertexShader(inout float4 color    : COLOR0,
                        inout float2 texCoord : TEXCOORD0,
                        inout float4 position : SV_Position)
{
    position = mul(position, MatrixTransform);
}

technique BlackHole
{
    pass Pass0
    {
        VertexShader = compile vs_3_0 SpriteVertexShader();
        PixelShader  = compile ps_3_0 PS();
    }
}
