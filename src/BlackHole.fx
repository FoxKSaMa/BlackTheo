// BlackTheo - a small, physically-shaped black hole for Celeste.
//
// The accretion disk is NOT a screen-space ellipse. It is a thin disk in the
// y=0 plane; null geodesics are integrated around a Schwarzschild-like mass
// and their crossings of the disk plane are recorded. This is what produces
// the characteristic Interstellar/Gargantua shape:
//   * the near side crosses in front of the shadow as a thin band,
//   * the far side is bent over and under the shadow into bright arcs,
//   * a thin photon ring hugs the shadow.
//
// The same traced rays are used for the screen-space lens: an escaped ray is
// sampled from the gameplay target at the direction it actually left the
// scene, with a short falloff so nearby objects remain recognizable.
//
// References:
//   - sayed1145/glsl-black-hole (MIT): disk radiance / domain-warped filaments.
//   - Gitee "black hole recycling station": deflection-based screen lens and
//     compact presentation.
//   - Luminet (1979), James et al. (2015): thin-disk lensing geometry.

sampler ScreenSampler : register(s0);

float4x4 MatrixTransform;
float2 ScreenSize;
float4 Hole;         // xy = centre in gameplay pixels, z = shadow radius, w = lens radius (px)
float4 HoleParams;   // x = disk brightness, y = lens displacement scale, z = lens cap (px), w = time
float DopplerEnabled;
float SpecksEnabled;
float NoiseSeed;
float LensFalloff;
float LensEdge;
float DiskAlpha;
float LensOnly;      // 1 = Madeline lens pass (no disk/shadow/specks)
float4 DiskInnerColor;
float4 DiskMidColor;
float4 DiskOuterColor;
float4 PhotonColor;
float4 RingColor;    // outer scatter / halo tint

#define TAU 6.28318530718
#define PI 3.14159265

float hash21(float2 p)
{
    p = fmod(p, 128.0);
    float3 q = frac(float3(p.x, p.y, p.x) * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

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

    // Seamless domain-warped filaments. The angular coordinate has an
    // integer period, so the pi/-pi seam is mathematically closed.
    // Negative flow reverses the disk animation: the left side (which the
    // Doppler phase keeps bright) is then the approaching side.
    float a = angle / TAU + 0.5;
    float flow = -time * 0.35;
    float warp = fbmP(float2(radius * 0.75 + 3.1, a * 8.0 - flow * 1.7), 8.0);
    float filament = fbmP(float2(radius * 1.55 + 17.0, a * 16.0 - flow * 3.2 + (warp - 0.5) * 1.7), 16.0);
    float fine = fbmP(float2(radius * 3.2 + 2.0, a * 32.0 - flow * 6.0 + (filament - 0.5) * 0.9), 32.0);

    float density = saturate(0.22 + 1.35 * filament);
    density *= 0.62 + 0.85 * fine;

    // Balanced Doppler shading: brighter approaching side, dimmer receding
    // side, with the average kept at 1.0 so the disk does not get darker.
    float doppler = lerp(1.0, 1.0 + 0.30 * cos(angle - 0.55), DopplerEnabled);
    return c * density * doppler;
}

// A few drifting colored pixels inside the shadow, visible only where the
// accretion disk does not cover them. Positions are deterministic per Theo
// (NoiseSeed) but orbit slowly, so there is always a small, stable population.
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

    // A lens-only pass around Madeline. This deliberately does not draw an
    // event horizon or a disk; it only bends the already-composited screen.
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

            // Radial-only lens: a gravitational lens is symmetric. The old
            // tangential term made this look like a whirlpool.
            float2 offset = -dir * (radius * 0.42 * strength) * falloff;

            float offsetMag = length(offset);
            if (offsetMag > 36.0)
            {
                offset *= 36.0 / offsetMag;
            }

            color.rgb = tex2D(
                ScreenSampler,
                saturate((pixel + offset) / ScreenSize)).rgb;

            // The radial warp folds over itself near the middle; the black
            // core covers that hollow ring and scales with the lens level.
            float coreRadius = max(HoleParams.z, 0.0);
            color.rgb *= smoothstep(coreRadius - 0.75, coreRadius + 0.75, r);
        }

        color.a = 1.0;
        return color;
    }

    if (screenRadius < lensRadius)
    {
        // Map the desired screen shadow radius to the critical impact
        // parameter b_c = 2.598. The measured 9.6 px reference gives a
        // stable FOV scale independent of the requested shadow size.
        float fovScale = 9.6 / shadowRadius;
        float2 uvWin = (pixel - Hole.xy) / ScreenSize.y;
        uvWin.y = -uvWin.y;
        float2 uvw = uvWin * fovScale;

        float3 ro = float3(0.0, 11.0, -59.8);
        float3 ta = float3(0.0, 0.0, 0.0);
        float3 fw = normalize(ta - ro);
        float3 rt = normalize(cross(fw, float3(0.0, 1.0, 0.0)));
        float3 up = cross(rt, fw);

        // Screen-space roll: the disk is no longer a flat horizontal bar.
        float roll = 0.32;
        float cr = cos(roll);
        float sr = sin(roll);
        float3 rtRolled = rt * cr + up * sr;
        float3 upRolled = up * cr - rt * sr;
        float3 rd0 = normalize(fw * 1.2 + uvw.x * rtRolled + uvw.y * upRolled);

        float3 pos = ro;
        float3 v = rd0;
        // Jitter the starting point to break up step quantisation.
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

        // Thin photon ring and a small halo in the selected palette.
        float photon = 1.0 - smoothstep(0.18, 0.95, abs(screenRadius - (shadowRadius + 0.55)));
        float3 ring = PhotonColor.rgb * photon * 1.35;

        // Screen-space lens: strong close to the photon sphere, subtle over a
        // wider radius. Only the innermost pixels are heavily smeared.
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

        // Sparse colored pixels inhabit the empty black parts of the shadow.
        // They are added before the disk so front-side disk material covers
        // them naturally.
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

        // A very small amount of coverage blending keeps the disk visible on
        // bright maps, and makes it behave more like a material than a pure
        // additive gel. The bright-background boost is intentionally modest.
        float bright = saturate(bgLum);
        float coverage = saturate(diskLum * HoleParams.x * 1.55 * DiskAlpha * (1.0 + bright * 0.45));
        float dither = hash21(pixel + 13.7) - 0.5;
        coverage = saturate(coverage + dither * 0.04);

        // Soft pixel-art quantisation: 10 levels, only partially applied so it
        // breaks up the airbrushed fBm without hard banding.
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
