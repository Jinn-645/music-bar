using System;
using System.Collections.Generic;
using System.Windows;

namespace MusicBar.Views.Face;

/// <summary>What the face knows about the world on each tick.</summary>
/// <param name="Level">Music loudness 0 to 1, or -1 when there is no live audio to listen to.</param>
/// <param name="Pointer">Where the mouse pointer is from the face's center, in face widths; null when it isn't watched or is far away.</param>
public readonly record struct FaceSenses(
    DateTime Clock, bool HasSession, bool Playing, double Level, TimeSpan UserIdle, Vector? Pointer, bool Hovered)
{
    /// <summary>The computer (processor, network, charger), when the face is allowed to notice it.</summary>
    public Services.SystemState? System { get; init; }

    /// <summary>The focus timer's phase.</summary>
    public Services.FocusPhase Focus { get; init; }
}

/// <summary>
/// Decides what the face does. A mood sets its resting expression from what is going on (music
/// playing or paused, the time of day, the user away); small acts play over it now and then
/// (glances, yawns, humming) or in answer to something (a new song, a poke, a rub, the scroll
/// wheel). Every target goes to the <see cref="FaceRig"/>, which makes the motion smooth.
/// </summary>
public sealed class FaceBrain
{
    enum Mood { Idle, Bored, Vibing, Waiting, Asleep }

    /// <summary>A short performance over the mood; a busier one is not cut short by a calmer one.</summary>
    sealed class Act
    {
        public required string Name;
        public required double Length;
        public int Priority;
        public Action<double, double>? Start;
        /// <summary>(progress 0 to 1, seconds in) → targets on the rig.</summary>
        public required Action<double, double> Pose;
        public Func<Act?>? Then;
        public double StartedAt;
    }

    readonly FaceRig _rig;
    readonly Random _rng;
    Mood _mood = Mood.Idle;
    Act? _act;

    double _nextBlink, _nextGlance, _glanceUntil, _nextFidget;
    Vector _glance;
    bool _wasPlaying, _hadSession;
    double _playingSince = -1;
    DateTime _greeted = DateTime.MinValue;
    int _chimedHour = -1;

    // Music.
    double _fast, _slow, _lastBeat = -10;
    int _beats;
    bool _nodLeft, _vibeHappy;

    // Hands-on.
    readonly List<double> _pokes = [];
    double _rub, _dizzy;
    bool _laughing;

    double _demoNext;
    int _demoStep;

    // The computer.
    bool? _online, _charging;
    double _cpu, _hotSince = -1, _nextSteam;
    bool _hot;
    int _battery = -1;

    // Strolling along the taskbar.
    double _walk, _nextStroll = 60, _stepAt;
    Services.FocusPhase _focus;

    public FaceBrain(FaceRig rig, int seed)
    {
        _rig = rig;
        _rng = new Random(seed);
        _demoStep = seed % 7;
    }

    /// <summary>Sleepy at night, a stretch in the morning, a yawn after lunch.</summary>
    public bool FollowsClock { get; set; } = true;

    public bool ReactsToMusic { get; set; } = true;

    /// <summary>Picks up the microphone while it sings along, as in the promo film.</summary>
    public bool HoldsMic { get; set; } = true;

    /// <summary>Singing along to the music right now.</summary>
    public bool Singing => _mood == Mood.Vibing;

    /// <summary>
    /// Bored enough for a stroll along the taskbar: nothing playing, nothing going on, nobody
    /// touching it, and a while since the last one. The window does the walking.
    /// </summary>
    public bool WantsStroll(double now) => !Demo && now >= _nextStroll && _act == null && _walk == 0
        && _mood is Mood.Idle or Mood.Bored or Mood.Waiting && _rub < 0.05 && _dizzy < 0.05 && _focus != Services.FocusPhase.Focus;

    /// <summary>The window is carrying it along: -1 toward the start of the taskbar, 1 toward the end, 0 standing.</summary>
    public void Walking(double now, double direction)
    {
        if (direction == 0 && _walk != 0) _nextStroll = now + 90 + _rng.NextDouble() * 150;
        _walk = direction;
    }

    /// <summary>Its stroll was cut short (music, a poke): no new one for a good while.</summary>
    public void StrollOver(double now) => _nextStroll = Math.Max(_nextStroll, now + 120);

    /// <summary>The focus timer moved on: a determined look, a stretch and a drink, or back to work.</summary>
    public void FocusChanged(double now, Services.FocusPhase from, Services.FocusPhase to)
    {
        _focus = to;
        if (Demo) return;
        if (_mood == Mood.Asleep) _mood = Mood.Idle;
        switch (to)
        {
            case Services.FocusPhase.Focus: Play(from == Services.FocusPhase.Break ? Chime() : GetToWork(), now); break;
            case Services.FocusPhase.Break: Play(Stretch(then: Drink), now); break;
        }
    }

    /// <summary>A preview (the settings page): runs through its expressions on its own, ignoring the world.</summary>
    public bool Demo { get; set; }

    /// <summary>Asleep and left alone: nothing moves fast, so the face can be drawn less often.</summary>
    public bool Calm => _mood == Mood.Asleep && _act == null && _rub < 0.05 && _dizzy < 0.05;

    // ------------------------------------------------------------------ the tick

    public void Tick(double now, double dt, FaceSenses s)
    {
        if (Demo) DemoTick(now, ref s);
        else Notice(now, s);

        _rub = Math.Max(0, _rub - dt * 0.8);
        _dizzy = Math.Max(0, _dizzy - dt * 0.55);
        _pokes.RemoveAll(t => now - t > 3);

        if (_act != null && now - _act.StartedAt >= _act.Length)
        {
            var next = _act.Then?.Invoke();
            _act = null;
            if (next != null) Play(next, now);
        }

        Rest(now, s);
        Listen(now, dt, s);
        if (_act != null)
        {
            double t = now - _act.StartedAt;
            _act.Pose(Math.Clamp(t / _act.Length, 0, 1), t);
        }
        else
        {
            Fidget(now, s);
        }
        HandsOn(now);
        Blinks(now);
        _rig.Tick(now, dt);
    }

    /// <summary>Changes in the world: music starting or stopping, the user leaving or coming back, the clock.</summary>
    void Notice(double now, FaceSenses s)
    {
        _focus = s.Focus;
        NoticeComputer(now, s);
        bool playing = s.Playing && s.HasSession;
        if (playing && !_wasPlaying)
        {
            _playingSince = now;
            if (_mood == Mood.Asleep) Play(Wake(), now);
            else if (ReactsToMusic) Play(MusicOn(), now);
        }
        else if (!playing && _wasPlaying)
        {
            // A long session ending is a little sad.
            bool afterLong = _playingSince >= 0 && now - _playingSince > 20 * 60;
            Play(s.HasSession ? MusicOff(afterLong) : Puzzled(), now);
            _playingSince = -1;
        }
        else if (!s.HasSession && _hadSession && !playing)
        {
            Play(Puzzled(), now);
        }
        _wasPlaying = playing;
        _hadSession = s.HasSession;

        bool night = FollowsClock && (s.Clock.Hour >= 23 || s.Clock.Hour < 5);
        if (_mood == Mood.Asleep)
        {
            // The user coming back, or music, wakes it.
            if (playing || s.UserIdle < TimeSpan.FromSeconds(1.5)) { _mood = Mood.Idle; Play(Wake(), now); }
            return;
        }
        bool drowsy = !playing && (s.UserIdle > TimeSpan.FromMinutes(5) || (night && s.UserIdle > TimeSpan.FromMinutes(1)));
        if (drowsy && (_act == null || _act.Priority < 2))
        {
            _mood = Mood.Asleep;
            Play(FallAsleep(), now);
            return;
        }
        _mood = playing && ReactsToMusic ? Mood.Vibing
            : s.HasSession && !playing ? Mood.Waiting
            : s.UserIdle > TimeSpan.FromSeconds(90) ? Mood.Bored
            : Mood.Idle;

        if (!FollowsClock || _act != null) return;
        // A good morning, once a day, the first time it sees the user after five.
        if (s.Clock.Hour is >= 5 and < 11 && _greeted.Date != s.Clock.Date && s.UserIdle < TimeSpan.FromSeconds(30))
        {
            _greeted = s.Clock;
            Play(Yawn(then: Stretch), now);
            return;
        }
        // A small chime on the hour while the day is on.
        if (s.Clock.Minute == 0 && s.Clock.Second < 3 && s.Clock.Hour is >= 8 and <= 22 && _chimedHour != s.Clock.Hour)
        {
            _chimedHour = s.Clock.Hour;
            Play(Chime(), now);
        }
    }

    /// <summary>The mood's resting face.</summary>
    void Rest(double now, FaceSenses s)
    {
        var r = _rig;
        double sleepy = Sleepiness(s.Clock);
        r.Eyes = EyeShape.Open;
        r.Mouth = MouthShape.Smile;
        r.Extras = FaceExtras.None;
        r.Brows = 0;
        r.Open = 0;
        r.Pupil = 1;
        r.Blush = 0.12;
        r.Lean = 0;
        r.Mic = HoldsMic && _mood == Mood.Vibing;
        double lid = 0.05 + sleepy * 0.42;

        // Eyes follow the pointer when it is near; otherwise they wander now and then.
        if (now > _glanceUntil) _glance = new Vector(0, 0.08);
        Vector look = _glance;
        if (s.Pointer is { } p && _mood != Mood.Asleep)
        {
            look = new Vector(Math.Clamp(p.X * 2.2, -1, 1), Math.Clamp(p.Y * 2.6, -1, 1));
            if (s.Hovered) { r.Blush = 0.3; r.Mouth = MouthShape.Grin; }
        }
        r.Look = look;

        switch (_mood)
        {
            case Mood.Asleep:
                lid = 1;
                r.Mouth = MouthShape.O;
                r.Open = 0.16 + 0.08 * Math.Sin(now * 1.4);
                r.Extras = FaceExtras.Zzz;
                r.Look = new Vector(0, 0.4);
                r.Lean = 4 * Math.Sin(now * 0.45);
                break;
            case Mood.Vibing:
                lid = 0.1 + sleepy * 0.25;
                r.Blush = 0.25;
                r.Eyes = _vibeHappy ? EyeShape.Happy : EyeShape.Open;
                r.Mouth = _fast > 0.5 ? MouthShape.Open : MouthShape.O;
                r.Open = 0.2 + _fast * 0.75;
                if (_fast > 0.22) r.Extras |= FaceExtras.Notes;
                r.Lean = _nodLeft ? -5 : 5;
                break;
            case Mood.Waiting:
                r.Mouth = MouthShape.Flat;
                r.Lean = 6;
                if (s.Pointer == null) r.Look = new Vector(0.55, 0.25);
                // Every so often it wonders where the music went.
                if (now % 11 < 2.2) r.Extras |= FaceExtras.Question;
                break;
            case Mood.Bored:
                lid += 0.18;
                r.Mouth = MouthShape.Flat;
                if (s.Pointer == null) r.Look = new Vector(_glance.X, 0.5);
                break;
        }
        r.LidLeft = r.LidRight = Math.Clamp(lid, 0, 1);
        RestBusy(now);
        RestComputer(now);
    }

    /// <summary>0 awake to 1 dead tired, by the clock.</summary>
    double Sleepiness(DateTime clock)
    {
        if (!FollowsClock) return 0;
        double h = clock.Hour + clock.Minute / 60.0;
        if (h >= 23 || h < 5) return 0.9;
        if (h >= 22) return 0.5;
        if (h < 7) return 0.5;
        if (h is >= 13 and < 14.5) return 0.35;
        return 0;
    }

    // ------------------------------------------------------------------ music

    /// <summary>Bounces on the beat while the music plays (a steady pretend beat when there is no live audio).</summary>
    void Listen(double now, double dt, FaceSenses s)
    {
        double level = s.Level;
        if (_mood != Mood.Vibing)
        {
            _fast = _slow = 0;
            return;
        }
        bool pretend = level < 0;
        if (pretend) level = 0.35 + 0.25 * Math.Max(0, Math.Sin(now * Math.PI * 2 * 1.85));
        _fast += (level - _fast) * (1 - Math.Exp(-dt * 20));
        _slow += (level - _slow) * (1 - Math.Exp(-dt * 1.3));

        bool beat = pretend
            ? now - _lastBeat > 60.0 / 112
            : _fast > _slow * 1.2 + 0.03 && _fast > 0.12 && now - _lastBeat > 0.3;
        if (!beat) return;
        _lastBeat = now;
        _beats++;
        _nodLeft = !_nodLeft;
        if (_act == null || _act.Priority < 2)
        {
            _rig.Hop(0.45 + _fast * 1.1);
            _rig.Squash(-0.8 - _fast);
            _rig.Swing(_nodLeft ? -30 : 30);
        }
        if (_beats % 8 == 0) _vibeHappy = _rng.NextDouble() < 0.55;
    }

    // ------------------------------------------------------------------ idle life

    void Blinks(double now)
    {
        if (now < _nextBlink) return;
        _nextBlink = now + 2.2 + _rng.NextDouble() * 4.2;
        if (_mood == Mood.Asleep || _rig.Eyes is not (EyeShape.Open or EyeShape.Teary or EyeShape.Wide)) return;
        _rig.Blink(now);
        // Now and then a double blink.
        if (_rng.NextDouble() < 0.2) _nextBlink = now + 0.28;
    }

    /// <summary>Small things it does on its own: glances, and every few seconds a little act.</summary>
    void Fidget(double now, FaceSenses s)
    {
        if (_mood == Mood.Asleep) return;
        if (now > _nextGlance)
        {
            _nextGlance = now + 2.5 + _rng.NextDouble() * 4;
            _glance = new Vector(_rng.NextDouble() * 1.6 - 0.8, _rng.NextDouble() * 0.9 - 0.45);
            _glanceUntil = now + 0.6 + _rng.NextDouble();
        }
        if (now < _nextFidget) return;
        _nextFidget = now + 7 + _rng.NextDouble() * 9;
        if (_mood == Mood.Vibing)
        {
            if (_rng.NextDouble() < 0.4) Play(Hum(), now);
            return;
        }

        double sleepy = Sleepiness(s.Clock);
        double h = s.Clock.Hour + s.Clock.Minute / 60.0;
        var options = new List<(double Weight, Func<Act> Make)>
        {
            (0.4 + sleepy * 2.5 + (_mood == Mood.Bored ? 1 : 0), () => Yawn()),
            (1.0, LookAround),
            (_mood == Mood.Bored ? 0.3 : 0.9, Smile),
            (0.6, Hum),
            (0.5, Wink),
            (0.3, Cat),
            (0.3, Cheeky),
        };
        if (FollowsClock && h is >= 11.5 and < 12.75) options.Add((1.2, Hungry));
        if (FollowsClock && h is >= 5 and < 10) options.Add((0.8, Stretch));
        if (FollowsClock && s.Clock.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) options.Add((0.6, Smile));

        double total = 0;
        foreach (var o in options) total += o.Weight;
        double pick = _rng.NextDouble() * total;
        foreach (var o in options)
        {
            pick -= o.Weight;
            if (pick > 0) continue;
            Play(o.Make(), now);
            return;
        }
    }

    // ------------------------------------------------------------------ hands on

    /// <summary>A click.</summary>
    public void Poke(double now)
    {
        _pokes.Add(now);
        if (_mood == Mood.Asleep)
        {
            _mood = Mood.Idle;
            Play(Startled(then: Groggy), now);
            return;
        }
        int n = _pokes.Count;
        if (n >= 6) Play(Angry(), now);
        else if (n >= 3) Play(Annoyed(), now);
        else Play(Giggle(), now);
    }

    public void DoubleTap(double now)
    {
        if (_mood == Mood.Asleep) return;
        // Fast clicking counts every second click as a double click: in a burst it is just more poking.
        if (_pokes.Count > 1)
        {
            Poke(now);
            return;
        }
        Play(Delight(), now);
    }

    /// <summary>Rubbing (dragging back and forth, or wiggling the pointer over it), in face widths travelled.</summary>
    public void Rub(double amount) => _rub = Math.Min(4, _rub + amount);

    /// <summary>The scroll wheel over the face spins it round.</summary>
    public void Scroll(double now, int delta)
    {
        _dizzy = Math.Min(4, _dizzy + Math.Abs(delta) / 120.0 * 0.55);
        _rig.Swing(Math.Sign(delta) * 60);
    }

    public void TrackChanged(double now)
    {
        if (_mood == Mood.Asleep || !ReactsToMusic) return;
        Play(NewSong(), now);
    }

    /// <summary>Rubbing and dizziness last a while after the hand stops, on top of anything but anger.</summary>
    void HandsOn(double now)
    {
        var r = _rig;
        bool busy = _act is { Priority: >= 3 };
        if (_dizzy > 0.8 && !busy)
        {
            r.Eyes = EyeShape.Spiral;
            r.Mouth = MouthShape.Wavy;
            r.Brows = 0.4;
            r.Lean = Math.Sin(now * 4) * 10 * Math.Min(1, _dizzy - 0.6);
            if (_dizzy > 1.6) r.Extras |= FaceExtras.Sweat;
            return;
        }
        if (_rub < 0.25 || busy) { _laughing = false; return; }

        if (_mood == Mood.Asleep) _mood = Mood.Idle;
        r.Blush = Math.Min(1, 0.4 + _rub * 0.3);
        if (_rub < 1)
        {
            // Content: eyes half shut, a soft smile.
            r.Eyes = _rub > 0.6 ? EyeShape.Happy : EyeShape.Open;
            r.LidLeft = r.LidRight = 0.45;
            r.Mouth = MouthShape.Grin;
            _laughing = false;
            return;
        }
        // Tickled: laughing, shaking, then tears and hearts.
        if (!_laughing)
        {
            _laughing = true;
            r.Hop(1.2);
            r.Shake(now, 1.2, 0.03);
        }
        r.Eyes = (int)(now * 4) % 2 == 0 ? EyeShape.Squeeze : EyeShape.Happy;
        r.Mouth = MouthShape.Open;
        r.Open = 0.65 + 0.3 * Math.Abs(Math.Sin(now * 16));
        if (_rub > 2) r.Extras |= FaceExtras.Hearts;
        if (_rub > 2.8) r.Extras |= FaceExtras.Tears;
        if ((int)(now * 2) % 3 == 0) r.Shake(now, 0.4, 0.02);
    }

    void Play(Act act, double now)
    {
        if (_act != null && _act.Priority > act.Priority && now - _act.StartedAt < _act.Length) return;
        act.StartedAt = now;
        _act = act;
        act.Start?.Invoke(now, 0);
    }

    // ------------------------------------------------------------------ the acts

    static double Bell(double p) => Math.Sin(Math.Clamp(p, 0, 1) * Math.PI);

    Act Giggle() => new()
    {
        Name = "giggle", Length = 0.9, Priority = 2,
        Start = (now, _) => { _rig.Hop(1.3); _rig.Squash(-1.6); },
        Pose = (p, _) =>
        {
            _rig.Eyes = EyeShape.Happy;
            _rig.Mouth = p < 0.4 ? MouthShape.Open : MouthShape.Grin;
            _rig.Open = 0.5;
            _rig.Blush = 0.6;
        },
    };

    Act Annoyed() => new()
    {
        Name = "annoyed", Length = 1.6, Priority = 2,
        Start = (now, _) => _rig.Shake(now, 0.3, 0.015),
        Pose = (p, _) =>
        {
            _rig.LidLeft = _rig.LidRight = 0.35;
            _rig.Brows = -0.6;
            _rig.Mouth = p < 0.5 ? MouthShape.Flat : MouthShape.Frown;
            _rig.Look = new Vector(0.7, 0);
            _rig.Extras |= FaceExtras.Sweat;
        },
    };

    Act Angry() => new()
    {
        Name = "angry", Length = 2.6, Priority = 3,
        Start = (now, _) => { _rig.Shake(now, 0.9, 0.04); _rig.Squash(-2.2); _rig.Hop(0.6); },
        Pose = (p, _) =>
        {
            _rig.LidLeft = _rig.LidRight = 0.25;
            _rig.Pupil = 0.8;
            _rig.Brows = -1;
            _rig.Mouth = MouthShape.Teeth;
            _rig.Look = new Vector(0, 0);
            _rig.Blush = 0;
            _rig.Extras |= FaceExtras.Anger;
        },
        Then = Sulk,
    };

    /// <summary>After being angry: turned away, not talking.</summary>
    Act Sulk() => new()
    {
        Name = "sulk", Length = 3, Priority = 2,
        Pose = (p, _) =>
        {
            _rig.LidLeft = _rig.LidRight = 0.3;
            _rig.Brows = -0.4;
            _rig.Mouth = MouthShape.Flat;
            _rig.Look = new Vector(-0.95, 0.1);
            _rig.Lean = -7;
            _rig.Blush = 0;
        },
    };

    Act Startled(Func<Act?>? then = null) => new()
    {
        Name = "startled", Length = 1.0, Priority = 3,
        Start = (now, _) => { _rig.Hop(2.4); _rig.Squash(1.6); },
        Pose = (p, _) =>
        {
            _rig.Eyes = EyeShape.Wide;
            _rig.Pupil = 0.65;
            _rig.Mouth = MouthShape.O;
            _rig.Open = 0.9;
            _rig.Brows = 0.6;
            _rig.Extras |= FaceExtras.Exclaim;
        },
        Then = then,
    };

    /// <summary>Woken by a poke: still half asleep and not amused.</summary>
    Act Groggy() => new()
    {
        Name = "groggy", Length = 2.6, Priority = 2,
        Pose = (p, _) =>
        {
            _rig.LidLeft = 0.6;
            _rig.LidRight = 0.5;
            _rig.Brows = -0.3;
            _rig.Mouth = MouthShape.Flat;
            _rig.Extras |= FaceExtras.Sweat;
        },
    };

    Act Delight() => new()
    {
        Name = "delight", Length = 1.4, Priority = 2,
        Start = (now, _) => { _rig.Hop(2.6); _rig.Squash(-2); },
        Pose = (p, _) =>
        {
            _rig.Eyes = EyeShape.Star;
            _rig.Mouth = MouthShape.Open;
            _rig.Open = 0.8;
            _rig.Blush = 0.5;
            _rig.Extras |= FaceExtras.Sparkle;
        },
    };

    Act NewSong() => new()
    {
        Name = "new song", Length = 1.6, Priority = 1,
        Start = (now, _) => _rig.Hop(1.4),
        Pose = (p, _) =>
        {
            if (p < 0.22)
            {
                _rig.Eyes = EyeShape.Wide;
                _rig.Mouth = MouthShape.O;
                _rig.Open = 0.6;
                return;
            }
            _rig.Eyes = EyeShape.Star;
            _rig.Mouth = MouthShape.Grin;
            _rig.Extras |= FaceExtras.Sparkle;
        },
    };

    Act MusicOn() => new()
    {
        Name = "music on", Length = 1.2, Priority = 1,
        Start = (now, _) => _rig.Hop(1.5),
        Pose = (p, _) =>
        {
            _rig.Eyes = EyeShape.Happy;
            _rig.Mouth = MouthShape.Open;
            _rig.Open = 0.6;
            _rig.Extras |= FaceExtras.Notes;
        },
    };

    Act MusicOff(bool afterLong) => new()
    {
        Name = "music off", Length = afterLong ? 2.2 : 1.1, Priority = 1,
        Pose = (p, _) =>
        {
            _rig.Eyes = afterLong ? EyeShape.Teary : EyeShape.Open;
            _rig.LidLeft = _rig.LidRight = 0.2;
            _rig.Brows = 0.6;
            _rig.Mouth = MouthShape.Frown;
            if (afterLong) _rig.Extras |= FaceExtras.Tears;
        },
    };

    Act Puzzled() => new()
    {
        Name = "puzzled", Length = 1.6, Priority = 1,
        Pose = (p, _) =>
        {
            _rig.Mouth = MouthShape.Flat;
            _rig.Brows = 0.3;
            _rig.Look = new Vector(0.3, -0.7);
            _rig.Lean = 8;
            _rig.Extras |= FaceExtras.Question;
        },
    };

    Act Yawn(Func<Act?>? then = null) => new()
    {
        Name = "yawn", Length = 2.2, Priority = 1,
        Start = (now, _) => _rig.Squash(1.3),
        Pose = (p, _) =>
        {
            _rig.Mouth = MouthShape.Yawn;
            _rig.Open = Bell(p * 1.1);
            _rig.LidLeft = _rig.LidRight = 0.45 + 0.45 * Bell(p);
            _rig.Brows = 0.35 * Bell(p);
            _rig.Lean = -5 * Bell(p);
        },
        Then = then,
    };

    Act Stretch() => Stretch(null);

    Act Stretch(Func<Act?>? then) => new()
    {
        Name = "stretch", Length = 1.5, Priority = 1,
        Start = (now, _) => { _rig.Squash(2.2); _rig.Hop(0.8); },
        Pose = (p, _) =>
        {
            _rig.Eyes = EyeShape.Happy;
            _rig.Mouth = MouthShape.Grin;
            _rig.Extras |= FaceExtras.Sparkle;
        },
        Then = then,
    };

    // ------------------------------------------------------------------ focus timer

    /// <summary>A focus round begins: eyes narrowed, a confident grin, a little hop.</summary>
    Act GetToWork() => new()
    {
        Name = "get to work", Length = 1.6, Priority = 2,
        Start = (now, _) => { _rig.Hop(1.2); _rig.Squash(-1.2); },
        Pose = (p, _) =>
        {
            _rig.LidLeft = _rig.LidRight = 0.3;
            _rig.Brows = -0.5;
            _rig.Mouth = MouthShape.Grin;
            _rig.Extras |= FaceExtras.Sparkle;
        },
    };

    /// <summary>On a break: a long, happy drink of water.</summary>
    Act Drink() => new()
    {
        Name = "drink", Length = 3.2, Priority = 1,
        Pose = (p, t) =>
        {
            _rig.Eyes = p < 0.75 ? EyeShape.Happy : EyeShape.Open;
            _rig.Mouth = p < 0.75 ? MouthShape.O : MouthShape.Smile;
            _rig.Open = 0.25 + 0.1 * Math.Sin(t * 9);
            _rig.Lean = -6;
            _rig.Blush = 0.4;
            _rig.Extras |= FaceExtras.Water;
        },
    };

    // ------------------------------------------------------------------ the computer

    /// <summary>The network, the charger and the processor, as they change.</summary>
    void NoticeComputer(double now, FaceSenses s)
    {
        if (s.System is not { } sys)
        {
            _online = _charging = null;
            _hot = false;
            _hotSince = -1;
            return;
        }
        if (_online is { } wasOnline && sys.Online is { } online && online != wasOnline)
            Play(online ? Reconnected() : Disconnected(), now);
        _online = sys.Online;
        if (_charging == false && sys.Charging == true) Play(Charged(), now);
        _charging = sys.Charging;
        _battery = sys.Battery;

        // Busy for a while before it counts, and calm for a while before it's over.
        _cpu = sys.Cpu;
        if (_cpu >= 0.85) { if (_hotSince < 0) _hotSince = now; }
        else if (_cpu < 0.6) _hotSince = -1;
        bool hot = _hotSince >= 0 && now - _hotSince > 8;
        if (hot && !_hot) Play(Overheat(), now);
        _hot = hot;
    }

    Act Disconnected() => new()
    {
        Name = "offline", Length = 2.6, Priority = 2,
        Start = (now, _) => { _rig.Hop(1.8); _rig.Shake(now, 0.5, 0.025); },
        Pose = (p, _) =>
        {
            _rig.Eyes = p < 0.35 ? EyeShape.Wide : EyeShape.Teary;
            _rig.Pupil = 0.7;
            _rig.Brows = 0.8;
            _rig.Mouth = p < 0.35 ? MouthShape.O : MouthShape.Wavy;
            _rig.Open = 0.7;
            _rig.Extras |= FaceExtras.NoSignal | (p < 0.35 ? FaceExtras.Exclaim : FaceExtras.Sweat);
        },
    };

    Act Reconnected() => new()
    {
        Name = "online", Length = 1.4, Priority = 2,
        Start = (now, _) => { _rig.Hop(2); _rig.Squash(-1.5); },
        Pose = (p, _) =>
        {
            _rig.Eyes = EyeShape.Happy;
            _rig.Mouth = MouthShape.Open;
            _rig.Open = 0.6;
            _rig.Extras |= FaceExtras.Sparkle;
        },
    };

    Act Charged() => new()
    {
        Name = "charging", Length = 1.8, Priority = 2,
        Start = (now, _) => { _rig.Hop(2.2); _rig.Shake(now, 0.3, 0.02); },
        Pose = (p, _) =>
        {
            _rig.Eyes = EyeShape.Star;
            _rig.Mouth = MouthShape.Open;
            _rig.Open = 0.7;
            _rig.Blush = 0.5;
            _rig.Extras |= FaceExtras.Bolt;
        },
    };

    Act Overheat() => new()
    {
        Name = "overheat", Length = 2.4, Priority = 2,
        Start = (now, _) => _rig.Shake(now, 0.8, 0.02),
        Pose = (p, _) =>
        {
            _rig.Eyes = EyeShape.Squeeze;
            _rig.Mouth = MouthShape.Wavy;
            _rig.Brows = 0.6;
            _rig.Blush = 0.9;
            _rig.Extras |= FaceExtras.Steam | FaceExtras.Sweat;
        },
    };

    /// <summary>The computer's state on the resting face: worried while offline, panting while the processor is busy, droopy on a low battery.</summary>
    void RestComputer(double now)
    {
        var r = _rig;
        if (_online == false && _mood != Mood.Asleep)
        {
            r.Brows = Math.Max(r.Brows, 0.45);
            if (now % 12 < 3) r.Extras |= FaceExtras.NoSignal;
        }
        if (_hot && _mood != Mood.Asleep)
        {
            r.Blush = Math.Max(r.Blush, 0.6);
            r.Extras |= FaceExtras.Sweat;
            if (now >= _nextSteam - 2.5) r.Extras |= FaceExtras.Steam;
            if (now >= _nextSteam) _nextSteam = now + 7 + _rng.NextDouble() * 5;
            if (_mood != Mood.Vibing)
            {
                r.Mouth = MouthShape.Tongue;
                r.LidLeft = r.LidRight = Math.Max(r.LidLeft, 0.3);
            }
        }
        if (_charging == false && _battery is >= 0 and <= 15 && _mood != Mood.Asleep)
        {
            r.LidLeft = r.LidRight = Math.Max(r.LidLeft, 0.45);
            r.Brows = Math.Max(r.Brows, 0.3);
            if (_mood != Mood.Vibing) r.Mouth = MouthShape.Flat;
        }
    }

    /// <summary>The focus timer and strolling on the resting face.</summary>
    void RestBusy(double now)
    {
        var r = _rig;
        if (_focus == Services.FocusPhase.Focus && _mood is Mood.Idle or Mood.Bored)
        {
            // Concentrating: lids a little down, brows set, a straight mouth, eyes on the work.
            r.LidLeft = r.LidRight = 0.22;
            r.Brows = -0.25;
            r.Mouth = MouthShape.Flat;
            r.Look = new Vector(r.Look.X * 0.4, 0.45);
        }
        if (_walk == 0) return;
        r.Lean = _walk * 7;
        r.Look = new Vector(_walk * 0.9, 0.05);
        r.Mouth = MouthShape.O;
        r.Open = 0.18;
        if (now % 6 < 3) r.Extras |= FaceExtras.Notes;
        // A little hop for every step.
        if (now >= _stepAt)
        {
            _stepAt = now + 0.34;
            r.Hop(0.55);
            r.Squash(-0.5);
        }
    }

    Act Smile() => new()
    {
        Name = "smile", Length = 1.3, Priority = 0,
        Start = (now, _) => _rig.Hop(0.6),
        Pose = (p, _) =>
        {
            _rig.Eyes = EyeShape.Happy;
            _rig.Mouth = MouthShape.Grin;
            _rig.Blush = 0.4;
        },
    };

    Act Hum() => new()
    {
        Name = "hum", Length = 2.4, Priority = 0,
        Pose = (p, t) =>
        {
            _rig.Eyes = EyeShape.Happy;
            _rig.Mouth = MouthShape.O;
            _rig.Open = 0.3 + 0.15 * Math.Sin(t * 7);
            _rig.Lean = 6 * Math.Sin(t * 3.2);
            _rig.Extras |= FaceExtras.Notes;
        },
    };

    Act Wink() => new()
    {
        Name = "wink", Length = 0.7, Priority = 0,
        Pose = (p, _) =>
        {
            _rig.LidLeft = Bell(p);
            _rig.Mouth = MouthShape.Grin;
            _rig.Blush = 0.4;
        },
    };

    Act LookAround() => new()
    {
        Name = "look around", Length = 2.0, Priority = 0,
        Pose = (p, t) =>
        {
            _rig.Look = new Vector(Math.Sin(t * 3.2) * 0.9, -0.15);
            _rig.Brows = 0.25;
            _rig.Mouth = MouthShape.Flat;
        },
    };

    /// <summary>Around lunchtime: licks its lips and looks up, thinking about food.</summary>
    Act Hungry() => new()
    {
        Name = "hungry", Length = 1.8, Priority = 0,
        Pose = (p, _) =>
        {
            _rig.Mouth = MouthShape.Tongue;
            _rig.Look = new Vector(0.4, -0.8);
            _rig.Blush = 0.25;
        },
    };

    /// <summary>On the hour: "oh! it's three o'clock".</summary>
    Act Chime() => new()
    {
        Name = "chime", Length = 1.4, Priority = 1,
        Start = (now, _) => _rig.Hop(1.2),
        Pose = (p, _) =>
        {
            if (p < 0.3)
            {
                _rig.Eyes = EyeShape.Wide;
                _rig.Mouth = MouthShape.O;
                _rig.Open = 0.5;
                _rig.Extras |= FaceExtras.Exclaim;
                return;
            }
            _rig.Mouth = MouthShape.Smile;
            _rig.Extras |= FaceExtras.Sparkle;
        },
    };

    Act FallAsleep() => new()
    {
        Name = "fall asleep", Length = 2.4, Priority = 1,
        Pose = (p, _) =>
        {
            _rig.Mouth = MouthShape.Yawn;
            _rig.Open = Bell(p * 1.3);
            _rig.LidLeft = _rig.LidRight = 0.5 + 0.5 * p;
            _rig.Extras = p > 0.7 ? FaceExtras.Zzz : FaceExtras.None;
        },
    };

    Act Wake() => new()
    {
        Name = "wake", Length = 1.8, Priority = 1,
        Pose = (p, _) =>
        {
            _rig.LidLeft = _rig.LidRight = 1 - p * 0.9;
            _rig.Mouth = p < 0.6 ? MouthShape.Yawn : MouthShape.Smile;
            _rig.Open = p < 0.6 ? Bell(p / 0.6) : 0;
        },
        Then = Stretch,
    };

    Act Love() => new()
    {
        Name = "love", Length = 2.0, Priority = 1,
        Pose = (p, _) =>
        {
            _rig.Eyes = EyeShape.Heart;
            _rig.Mouth = MouthShape.Smile;
            _rig.Blush = 0.9;
            _rig.Extras |= FaceExtras.Hearts;
        },
    };

    Act Cry() => new()
    {
        Name = "cry", Length = 2.0, Priority = 1,
        Pose = (p, _) =>
        {
            _rig.Eyes = EyeShape.Teary;
            _rig.Brows = 0.9;
            _rig.Mouth = MouthShape.Frown;
            _rig.Extras |= FaceExtras.Tears;
        },
    };

    Act Cheeky() => new()
    {
        Name = "cheeky", Length = 1.6, Priority = 1,
        Pose = (p, _) =>
        {
            _rig.LidLeft = 1;
            _rig.Mouth = MouthShape.Tongue;
            _rig.Blush = 0.4;
        },
    };

    Act Cat() => new()
    {
        Name = "cat", Length = 1.6, Priority = 1,
        Pose = (p, _) =>
        {
            _rig.Eyes = EyeShape.Happy;
            _rig.Mouth = MouthShape.Cat;
            _rig.Blush = 0.5;
        },
    };

    // ------------------------------------------------------------------ preview

    /// <summary>The settings preview: a new expression every couple of seconds, with music to dance to.</summary>
    void DemoTick(double now, ref FaceSenses s)
    {
        s = s with { HasSession = true, Playing = false, Level = -1, UserIdle = TimeSpan.Zero, Clock = new DateTime(2026, 1, 5, 15, 0, 0) };
        _mood = Mood.Idle;
        if (_demoStep % 9 == 1) { _mood = Mood.Vibing; s = s with { Playing = true }; }
        if (now < _demoNext) return;
        _demoNext = now + 2.4;
        _demoStep++;
        _act = null;
        _rub = _dizzy = 0;
        switch (_demoStep % 9)
        {
            case 0: Play(Giggle(), now); break;
            case 1: break;
            case 2: Play(Angry(), now); _act!.Then = null; break;
            case 3: _rub = 2.6; break;
            case 4: Play(Love(), now); break;
            case 5: _dizzy = 2.2; break;
            case 6: Play(Delight(), now); break;
            case 7: Play(_demoStep % 2 == 0 ? Cry() : Cheeky(), now); break;
            case 8: Play(_demoStep % 2 == 0 ? Yawn() : Cat(), now); break;
        }
    }
}
