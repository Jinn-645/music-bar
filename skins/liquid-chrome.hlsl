//! name: Liquid Chrome
//! author: MusicBar
//! kind: progress
//! description: Kim loại nóng chảy dâng tới chỗ đang phát, gợn sóng theo nhạc

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float px = uv.x * size.x;
    float head = progress * size.x;
    float h = rise(uv);

    // The metal's surface: a slow swell, and ripples that grow with the music.
    float ripple = (0.05 + 0.16 * level) * sin(px * 0.06 + time * 3.1) * (0.4 + spectrum(frac(uv.x * 2.0)));
    float surface = 0.46 + 0.1 * sin(px * 0.017 - time * 1.2) + ripple;
    float inside = smoothstep(surface + 0.05, surface - 0.05, h);

    // Chrome: bright bands across the height that slide as the surface moves.
    float bands = pow(abs(sin(h * 8.0 - surface * 7.0 + time * 0.6)), 8.0);
    float3 metal = lerp(colorB.rgb, colorA.rgb, saturate(h / max(surface, 0.05)));
    metal += bands * lerp(colorC.rgb, float3(1, 1, 1), 0.6) * 0.75;

    // Only the played part, with a bright meniscus at the head that flares on the beat.
    float played = smoothstep(head + 2.0, head - 8.0, px);
    float glow = exp(-abs(px - head) / 16.0) * (0.55 + 0.45 * beat);
    float alpha = inside * played * (0.5 + 0.45 * level) + glow * 0.6 * inside;
    return paint(metal + glow * colorC.rgb, alpha);
}
