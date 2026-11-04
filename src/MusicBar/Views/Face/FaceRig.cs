using System;
using System.Windows;
using System.Windows.Media;

namespace MusicBar.Views.Face;

/// <summary>
/// The face's moving parts. The brain sets where they should be (the public fields); each tick
/// eases the drawn values toward them, plays blinks, and runs the springs that make it hop,
/// squash, tilt and shake. Shapes (eyes, mouth, signs) switch at once; the springs hide the cut.
/// </summary>
public sealed class FaceRig
{
    // ---- targets, set by the brain every tick
    public EyeShape Eyes = EyeShape.Open;
    public MouthShape Mouth = MouthShape.Smile;
    public FaceExtras Extras;
    public double LidLeft, LidRight;
    public Vector Look;
    public double Pupil = 1;
    public double Brows;
    public double Open;
    public double Blush;
    /// <summary>The head's resting tilt in degrees (the springs swing around it).</summary>
    public double Lean;

    // ---- drawn values
    double _lidL, _lidR, _brows, _open, _blush, _pupil = 1;
    Vector _look;
    double _blinkAt = -10;
    readonly Spring _squash = new(1, 260, 14);
    readonly Spring _hop = new(0, 220, 13);
    readonly Spring _tilt = new(0, 160, 10);
    double _shakeUntil, _shakeAmount;

    const double BlinkDown = 0.07, BlinkHold = 0.04, BlinkUp = 0.11;

    /// <summary>A blink, over whatever the lids are doing.</summary>
    public void Blink(double now) => _blinkAt = now;

    /// <summary>A jump: positive goes up. In face heights per second.</summary>
    public void Hop(double speed) => _hop.Kick(-speed);

    /// <summary>A squash (negative) or a stretch (positive), as a kick to the vertical scale.</summary>
    public void Squash(double amount) => _squash.Kick(amount);

    /// <summary>A swing of the head, in degrees per second.</summary>
    public void Swing(double speed) => _tilt.Kick(speed);

    /// <summary>A tremble from side to side for a while (cross, laughing, dizzy).</summary>
    public void Shake(double now, double seconds, double amount)
    {
        _shakeUntil = now + seconds;
        _shakeAmount = amount;
    }

    public void Tick(double now, double dt)
    {
        dt = Math.Clamp(dt, 0, 0.1);
        // Lids and brows move fast, the mouth a little slower, color slowest.
        _lidL = Ease(_lidL, LidLeft, dt, 18);
        _lidR = Ease(_lidR, LidRight, dt, 18);
        _brows = Ease(_brows, Brows, dt, 12);
        _open = Ease(_open, Open, dt, 16);
        _blush = Ease(_blush, Blush, dt, 4);
        _pupil = Ease(_pupil, Pupil, dt, 10);
        _look = new Vector(Ease(_look.X, Look.X, dt, 9), Ease(_look.Y, Look.Y, dt, 9));
        _tilt.Rest = Lean;
        _squash.Step(dt);
        _hop.Step(dt);
        _tilt.Step(dt);
    }

    static double Ease(double from, double to, double dt, double rate) => from + (to - from) * (1 - Math.Exp(-dt * rate));

    /// <summary>How far a blink has the lids down right now (0 to 1).</summary>
    double BlinkAmount(double now)
    {
        double t = now - _blinkAt;
        if (t < 0 || t > BlinkDown + BlinkHold + BlinkUp) return 0;
        if (t < BlinkDown) return t / BlinkDown;
        if (t < BlinkDown + BlinkHold) return 1;
        return 1 - (t - BlinkDown - BlinkHold) / BlinkUp;
    }

    public FaceFrame Frame(double now, Color accent, Color ink, double level)
    {
        double blink = BlinkAmount(now);
        return new FaceFrame
        {
            Time = now,
            Eyes = Eyes,
            LidLeft = Math.Max(_lidL, blink),
            LidRight = Math.Max(_lidR, blink),
            Look = _look,
            Pupil = _pupil,
            Brows = _brows,
            Mouth = Mouth,
            Open = Math.Clamp(_open, 0, 1),
            Blush = Math.Clamp(_blush, 0, 1),
            Extras = Extras,
            Accent = accent,
            Ink = ink,
            Level = level,
        };
    }

    /// <summary>
    /// Where the head is: vertical scale (squash), horizontal scale (it bulges as it squashes),
    /// lift in face heights (up is negative), tilt in degrees, and the sideways tremble in face widths.
    /// </summary>
    public (double ScaleX, double ScaleY, double Lift, double Angle, double Sway) Pose(double now)
    {
        double sy = Math.Clamp(_squash.Value, 0.7, 1.3);
        double sx = 1 + (1 - sy) * 0.6;
        double sway = now < _shakeUntil ? Math.Sin(now * 55) * _shakeAmount * Math.Min(1, (_shakeUntil - now) * 4) : 0;
        return (sx, sy, Math.Clamp(_hop.Value, -0.35, 0.2), Math.Clamp(_tilt.Value, -25, 25), sway);
    }

    /// <summary>A damped spring toward a resting value, nudged by kicks.</summary>
    sealed class Spring(double rest, double stiffness, double damping)
    {
        public double Rest = rest;
        public double Value = rest;
        double _speed;

        public void Kick(double speed) => _speed += speed;

        public void Step(double dt)
        {
            // Small substeps keep a stiff spring stable at 15 frames per second.
            for (double left = dt; left > 0; left -= 0.008)
            {
                double h = Math.Min(0.008, left);
                _speed += (-stiffness * (Value - Rest) - damping * _speed) * h;
                Value += _speed * h;
            }
        }
    }
}
