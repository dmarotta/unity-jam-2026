#ifndef LUCKY_SPRITE_FX_COMMON
#define LUCKY_SPRITE_FX_COMMON

// Shared by SpriteFX passes. Expects the SpriteFX material properties to be declared by the including pass.

float LuckyHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float LuckyNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(LuckyHash(i), LuckyHash(i + float2(1, 0)), u.x),
                lerp(LuckyHash(i + float2(0, 1)), LuckyHash(i + float2(1, 1)), u.x), u.y);
}

// Sprites are pivoted at the feet, so uv.y approximates height up the body.
float3 SwayPosition(float3 pos, float2 uv)
{
    float h = saturate(uv.y);
    float t = _Time.y * _SwaySpeed + _Phase;
    pos.x += sin(t) * _Sway * h * h;
    pos.y *= 1.0 + sin(t * 2.0) * _Breath * h;
    return pos;
}

// Clips dissolved pixels and returns the glowing edge amount (0 when not dissolving).
half DissolveEdge(float2 uv)
{
    if (_Dissolve <= 0.001) return 0;
    float n = LuckyNoise(uv * 9.0) * 0.65 + LuckyNoise(uv * 27.0) * 0.35;
    float d = _Dissolve * 1.15 - 0.05;
    clip(n - d);
    return 1.0 - smoothstep(d, d + 0.07, n);
}

#endif
