using System;
using System.Windows.Threading;
using MusicBar.Models;

namespace MusicBar.Services;

public enum FocusPhase { Off, Focus, Break }

/// <summary>
/// A pomodoro timer the face keeps: focus for a while, then a short break, and a longer one after
/// every fourth round. One for the whole app, whichever face started it.
/// </summary>
public sealed class FocusTimer
{
    const int RoundsToLongBreak = 4;

    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    AppSettings? _s;

    FocusTimer() => _tick.Tick += (_, _) => Tick();

    public static FocusTimer Current { get; } = new();

    public FocusPhase Phase { get; private set; }

    /// <summary>Focus rounds finished since it was started.</summary>
    public int Rounds { get; private set; }

    public DateTime Ends { get; private set; }

    public TimeSpan Length { get; private set; }

    /// <summary>How much of the current stretch is still to go, 1 to 0.</summary>
    public double Left => Phase == FocusPhase.Off || Length <= TimeSpan.Zero
        ? 0 : Math.Clamp((Ends - DateTime.Now).TotalSeconds / Length.TotalSeconds, 0, 1);

    public TimeSpan Remaining => Phase == FocusPhase.Off ? TimeSpan.Zero : Ends - DateTime.Now > TimeSpan.Zero ? Ends - DateTime.Now : TimeSpan.Zero;

    /// <summary>The phase changed: (the one before, the new one). The break after every fourth round is long.</summary>
    public event Action<FocusPhase, FocusPhase>? Changed;

    public bool LongBreak => Phase == FocusPhase.Break && Rounds % RoundsToLongBreak == 0;

    public void Start(AppSettings settings)
    {
        _s = settings;
        Rounds = 0;
        Enter(FocusPhase.Focus);
        _tick.Start();
    }

    public void Stop()
    {
        _tick.Stop();
        Enter(FocusPhase.Off);
    }

    /// <summary>Ends the current stretch now, as if its time were up.</summary>
    public void Skip()
    {
        if (Phase != FocusPhase.Off) Advance();
    }

    void Tick()
    {
        if (Phase != FocusPhase.Off && DateTime.Now >= Ends) Advance();
    }

    void Advance()
    {
        if (Phase == FocusPhase.Focus) Rounds++;
        Enter(Phase == FocusPhase.Focus ? FocusPhase.Break : FocusPhase.Focus);
    }

    void Enter(FocusPhase phase)
    {
        var before = Phase;
        Phase = phase;
        int minutes = phase switch
        {
            FocusPhase.Focus => _s?.FocusMinutes ?? 25,
            FocusPhase.Break => (_s?.BreakMinutes ?? 5) * (Rounds % RoundsToLongBreak == 0 ? 3 : 1),
            _ => 0,
        };
        Length = TimeSpan.FromMinutes(minutes);
        Ends = DateTime.Now + Length;
        Changed?.Invoke(before, phase);
    }
}
