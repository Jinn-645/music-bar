//! name: Plasma
//! author: MusicBar
//! kind: visualizer
//! description: Ánh sáng lỏng trôi chậm theo bảng màu, thở theo độ to của nhạc

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 p = float2(uv.x * size.x / size.y, rise(uv));
    float t = time * 0.35;

    // Four waves folded together: the classic plasma.
    float v = sin(p.x * 1.7 + t * 1.3)
            + sin(p.y * 3.1 - t * 0.9 + sin(p.x * 0.6 + t))
            + sin(length(p - float2(sin(t * 0.7) * 6.0 + 12.0, 0.5)) * 2.2 - t * 2.0)
            + sin(p.x * 0.43 - p.y * 2.0 + t * 1.7);
    v = v * 0.25 + 0.5;

    // Three colors around the cycle; the bass pushes the light up from the screen edge.
    float3 color = lerp(lerp(colorA.rgb, colorB.rgb, smoothstep(0.0, 0.5, v)), colorC.rgb, smoothstep(0.5, 1.0, v));
    float swell = spectrum(0.04) * 0.6 + level * 0.5;
    float body = smoothstep(0.15 + swell, 0.0, p.y - 0.15) * 0.75 + 0.25;
    float alpha = (0.25 + 0.5 * level + 0.25 * beat) * body * (0.6 + 0.4 * v);
    return paint(color * (1.0 + 0.4 * beat), alpha);
}
