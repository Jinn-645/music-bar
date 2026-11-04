using System;
using System.Windows;
using System.Windows.Media;

namespace MusicBar.Views.Face;

/// <summary>The look of the taskbar face. Every set draws the same expressions in its own way.</summary>
public enum FaceStyle { Classic, ClassicGirl, AnimeGirl, AnimeBoy, Alien, Robot, Cat, Derp }

/// <summary>What the eyes do. How far the lids are down is separate (<see cref="FaceFrame.LidLeft"/>).</summary>
public enum EyeShape
{
    /// <summary>Ordinary eyes: whites, pupils looking along <see cref="FaceFrame.Look"/>, lids per eye.</summary>
    Open,
    /// <summary>Smiling shut, "^ ^".</summary>
    Happy,
    /// <summary>Startled: fully open, small pupils.</summary>
    Wide,
    /// <summary>In love.</summary>
    Heart,
    /// <summary>Starstruck, thrilled.</summary>
    Star,
    /// <summary>Dizzy spirals that turn with time.</summary>
    Spiral,
    /// <summary>Squeezed shut, "&gt; &lt;": laughing hard, or cross.</summary>
    Squeeze,
    /// <summary>Open and glossy with welling tears: sad, or moved.</summary>
    Teary,
}

public enum MouthShape
{
    Flat,
    Smile,
    /// <summary>A wide closed smile: cheeky, pleased with itself.</summary>
    Grin,
    Frown,
    /// <summary>A small round mouth (surprise, whistling, singing "oo"), sized by <see cref="FaceFrame.Open"/>.</summary>
    O,
    /// <summary>A big open laugh with the tongue showing, as tall as <see cref="FaceFrame.Open"/> says.</summary>
    Open,
    /// <summary>A tall yawn with the tongue at the bottom, sized by <see cref="FaceFrame.Open"/>.</summary>
    Yawn,
    /// <summary>A smile with the tongue out: cheeky, or hungry.</summary>
    Tongue,
    /// <summary>A squiggle: dizzy, nervous.</summary>
    Wavy,
    /// <summary>":3".</summary>
    Cat,
    /// <summary>Gritted teeth: angry.</summary>
    Teeth,
}

/// <summary>Little signs drawn around the face, any number at once.</summary>
[Flags]
public enum FaceExtras
{
    None = 0,
    /// <summary>Z's floating up from the top right: asleep.</summary>
    Zzz = 1,
    /// <summary>Music notes floating up: enjoying the song.</summary>
    Notes = 2,
    /// <summary>The cross-shaped "popping vein" mark: angry.</summary>
    Anger = 4,
    /// <summary>A sweat drop at the side: awkward, nervous, annoyed.</summary>
    Sweat = 8,
    /// <summary>Small hearts floating up.</summary>
    Hearts = 16,
    /// <summary>Twinkles: fresh, delighted, a new song.</summary>
    Sparkle = 32,
    /// <summary>A question mark: puzzled, waiting.</summary>
    Question = 64,
    /// <summary>An exclamation mark: startled.</summary>
    Exclaim = 128,
    /// <summary>Tears running down from the eyes: crying, or laughing to tears.</summary>
    Tears = 256,
}

/// <summary>
/// One frame of the face, as the painters see it. Painters draw in design units: a box 100 wide and
/// 72 tall, origin top left, the face roughly where the promo film has it (eyes about x 27–45 and
/// 53–71, y 17–41; mouth about x 29–69, y 45–63). They may reach up to 8 units outside the box for
/// ears, antennae, hair or signs. Squash, bounce and tilt are applied before the painter runs.
/// </summary>
public readonly record struct FaceFrame
{
    /// <summary>Seconds, always running: for anything that moves on its own (spirals, floating signs, LEDs).</summary>
    public required double Time { get; init; }

    public required EyeShape Eyes { get; init; }

    /// <summary>How far each upper lid is down: 0 open, 1 shut (a shut eye is a curved line). Only for <see cref="EyeShape.Open"/> and <see cref="EyeShape.Teary"/>.</summary>
    public required double LidLeft { get; init; }

    public required double LidRight { get; init; }

    /// <summary>Where the pupils look, each axis -1 to 1 (x right, y down).</summary>
    public required Vector Look { get; init; }

    /// <summary>Pupil size: 1 normal, down to 0.6 (scared, startled), up to 1.4 (cute, pleading).</summary>
    public required double Pupil { get; init; }

    /// <summary>-1 angry (inner ends of the brows pulled down), 0 relaxed (no brows needed), 1 worried or sad (inner ends up).</summary>
    public required double Brows { get; init; }

    public required MouthShape Mouth { get; init; }

    /// <summary>0 to 1: how open the mouth is, for O, Open and Yawn (singing and talking move it).</summary>
    public required double Open { get; init; }

    /// <summary>0 to 1: pink cheeks.</summary>
    public required double Blush { get; init; }

    public required FaceExtras Extras { get; init; }

    /// <summary>The song's color (from the cover art), for anything that should follow the music.</summary>
    public required Color Accent { get; init; }

    /// <summary>
    /// The color for lines drawn straight on the taskbar (mouth, brows, "^ ^" eyes, signs): near-white on
    /// a dark taskbar, near-black on a light one. Eye whites keep their own dark outline either way.
    /// </summary>
    public required Color Ink { get; init; }

    /// <summary>Music loudness right now, 0 to 1 (0 when nothing plays).</summary>
    public required double Level { get; init; }
}

/// <summary>Draws one face set. Instances are reused for every frame and should keep their brushes and pens frozen.</summary>
public abstract class FacePainter
{
    /// <summary>Draws the face (eyes, mouth, brows, cheeks, the set's own features) in design units.</summary>
    public abstract void Draw(DrawingContext dc, in FaceFrame f);
}
