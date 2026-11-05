//! name: Synthwave
//! author: MusicBar
//! kind: visualizer
//! description: Lưới neon lao về phía bạn, dưới dải núi dựng từ chính bản nhạc

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float h = rise(uv);
    float aspect = size.x / size.y;
    float x = (uv.x - 0.5) * aspect;
    float horizon = 0.42;
    float3 color = float3(0, 0, 0);
    float alpha = 0;

    if (h < horizon)
    {
        // The floor: depth grows toward the horizon; lines rush toward the screen edge.
        float depth = 0.6 / max(horizon - h, 0.015);
        float across = abs(frac(depth - time * 1.4) - 0.5);
        float along = abs(frac(x / depth * 3.0) - 0.5);
        float fade = saturate((horizon - h) * 3.0);
        float grid = smoothstep(0.06, 0.0, across * fade) + smoothstep(0.05, 0.0, along * 0.6);
        color = lerp(colorA.rgb, colorC.rgb, saturate(depth * 0.08));
        alpha = saturate(grid) * (0.35 + 0.65 * level) * (0.6 + 0.4 * fade);
    }
    else
    {
        // The skyline: the spectrum folded around the middle, the bass in the centre.
        float d = abs(uv.x - 0.5) * 2.0;
        float sky = horizon + spectrum(d) * (1.0 - horizon) * (0.9 + 0.2 * beat) * (1.0 - d * 0.35);
        float ridge = smoothstep(sky + 0.03, sky - 0.03, h);
        float rim = exp(-abs(h - sky) * 40.0);
        color = lerp(colorB.rgb, colorA.rgb, saturate((h - horizon) / (1.0 - horizon))) + rim * colorC.rgb;
        alpha = ridge * 0.55 + rim * 0.9;
    }

    // The sun behind it all, pulsing on the beat.
    float2 p = float2(x, h - horizon);
    float sun = smoothstep(0.62, 0.55, length(p * float2(0.9, 1.6))) * step(horizon, h);
    color = lerp(color, lerp(colorC.rgb, float3(1, 0.85, 0.6), 0.5), sun * (1.0 - alpha));
    alpha = max(alpha, sun * (0.35 + 0.35 * beat));
    return paint(color, alpha);
}
