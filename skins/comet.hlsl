//! name: Comet
//! author: MusicBar
//! kind: progress
//! description: Sao chổi lao tới chỗ đang phát giữa bầu sao, đuôi lấp lánh theo nhạc

// A small hash: a steady random number for a cell.
float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 px = float2(uv.x * size.x, uv.y * size.y);
    float head = progress * size.x;
    float mid = size.y * 0.5;

    // Stars drifting left behind the comet: one per 9-pixel cell, twinkling.
    float2 cell = floor((px + float2(time * 18.0, 0)) / 9.0);
    float2 inCell = frac((px + float2(time * 18.0, 0)) / 9.0) - 0.5;
    float star = step(0.82, hash(cell)) * smoothstep(0.2, 0.0, length(inCell));
    star *= 0.5 + 0.5 * sin(time * 4.0 + hash(cell + 3.0) * 30.0);
    float behind = smoothstep(head + 4.0, head - 30.0, px.x);

    // The tail: wide and dim far back, narrow and bright at the head, wavering with the treble.
    float back = max(head - px.x, 0.0);
    float waver = sin(back * 0.05 - time * 9.0) * (2.0 + 6.0 * spectrum(0.7));
    float width = 2.0 + back * 0.03 * (0.6 + level);
    float tail = exp(-abs(px.y - mid - waver * saturate(back / 200.0)) / width) * exp(-back / (220.0 + 380.0 * level)) * step(px.x, head);

    // The head itself, swelling on the beat.
    float core = exp(-length(px - float2(head, mid)) / (3.0 + 4.0 * beat));

    float3 color = lerp(colorB.rgb, colorA.rgb, exp(-back / 160.0)) * tail + float3(1, 1, 1) * core + colorC.rgb * star;
    float alpha = saturate(tail * 0.9 + core + star * behind * 0.8);
    return paint(color, alpha);
}
