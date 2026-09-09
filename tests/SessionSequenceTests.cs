using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using YeShunguangPet;

internal static class SessionSequenceTests
{
    public static object Run(Action<bool, string> check, string output, int? replaySeed = null)
    {
        const int steps = 4000;
        var seeds = replaySeed.HasValue ? new[] { replaySeed.Value } : CreateSeeds();
        var completed = 0;
        foreach (var seed in seeds)
        {
            var random = new Random(seed);
            var clock = new QualityClock();
            var session = new CompanionSession(clock);
            var model = new Model();
            var phaseEvents = new List<SessionPhase>();
            var records = new List<FocusCompletion>();
            session.Completed += phaseEvents.Add; session.FocusCompleted += records.Add;
            var trace = new Queue<string>();
            for (var step = 0; step < steps; step++)
            {
                var op = random.Next(10);
                var focus = random.Next(-10, 140); var rest = random.Next(-10, 80);
                var phase = random.Next(2) == 0 ? SessionPhase.Focus : SessionPhase.Break;
                var milliseconds = random.Next(4) == 0 ? 28_800_000 : random.Next(0, 180_001);
                var shift = TimeSpan.FromMinutes(random.Next(-1440, 1441));
                var command = op switch
                {
                    0 => $"Configure({focus},{rest})", 1 => "StartOrResume", 2 => "Pause", 3 => "Reset", 4 => "SkipBreak",
                    5 => $"PreparePhase({phase})", 6 => "Tick", 7 => $"Advance({milliseconds}ms)",
                    8 => $"ShiftWall({shift.TotalMinutes}min)", _ => "AdvanceToDeadlineAndPause"
                };
                trace.Enqueue($"{step}: {command}"); if (trace.Count > 32) trace.Dequeue();
                try
                {
                    switch (op)
                    {
                        case 0: session.Configure(focus, rest); model.Configure(focus, rest); break;
                        case 1: session.StartOrResume(); model.Start(); break;
                        case 2: session.Pause(); model.Pause(); break;
                        case 3: session.Reset(); model.Reset(); break;
                        case 4: session.SkipBreak(); model.Skip(); break;
                        case 5: session.PreparePhase(phase); model.Prepare(phase); break;
                        case 6: session.Tick(); model.Tick(); break;
                        case 7: clock.Advance(milliseconds); model.Advance(milliseconds); break;
                        case 8: clock.ShiftWall(shift); break;
                        case 9:
                            var until = model.Remaining;
                            clock.Advance(until); model.Advance(until);
                            session.Pause(); model.Pause(); break;
                    }
                    if (session.Phase != model.Phase || session.Status != model.Status ||
                        session.Duration.TotalMilliseconds != model.Duration || session.Remaining.TotalMilliseconds != model.Remaining ||
                        session.CompletedFocusSessions != model.FocusCount || phaseEvents.Count != model.Completions.Count || records.Count != model.FocusCount)
                        throw new InvalidOperationException($"Actual {session.Phase}/{session.Status}/{session.Remaining.TotalMilliseconds}ms/{session.CompletedFocusSessions}; expected {model.Phase}/{model.Status}/{model.Remaining}ms/{model.FocusCount}");
                    if (phaseEvents.Count > 0 && phaseEvents[^1] != model.Completions[^1]) throw new InvalidOperationException("Wrong completion phase");
                    if (records.Count > 0 && records[^1].DurationSeconds != model.LastFocusSeconds) throw new InvalidOperationException("Wrong recorded duration");
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(output, "sequence-failure.json"), JsonSerializer.Serialize(new { seed, step, command, trace, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
                    throw new InvalidOperationException($"Sequence failed at seed={seed}, step={step}. Replay with --quality 0 {seed}.", ex);
                }
            }
            completed += model.FocusCount;
            check(true, $"seed {seed}: {steps} commands agree with independent elapsed-time model");
        }
        return new { seeds, stepsPerSeed = steps, commands = seeds.Length * steps, completedFocusSessions = completed };
    }

    private static int[] CreateSeeds()
    {
        var seeds = new int[32]; for (var i = 0; i < seeds.Length; i++) seeds[i] = 250_000 + i * 7919; return seeds;
    }

    // Reference model subtracts elapsed milliseconds on clock advances. Production uses timestamps.
    private sealed class Model
    {
        private int _focus = 25, _rest = 5;
        public SessionPhase Phase = SessionPhase.Focus;
        public SessionStatus Status = SessionStatus.Ready;
        public double Duration = 1_500_000, Remaining = 1_500_000;
        public int FocusCount, LastFocusSeconds;
        public readonly List<SessionPhase> Completions = new();
        private void SetDuration() => Duration = Remaining = (Phase == SessionPhase.Focus ? _focus : _rest) * 60_000;
        public void Configure(int focus, int rest) { _focus = Math.Clamp(focus, 1, 120); _rest = Math.Clamp(rest, 1, 60); if (Status == SessionStatus.Ready) SetDuration(); }
        public void Prepare(SessionPhase phase)
        {
            if (Status is SessionStatus.Running or SessionStatus.Paused) return;
            Phase = phase; Status = SessionStatus.Ready; SetDuration();
        }
        public void Start()
        {
            if (Status == SessionStatus.Running) return;
            if (Status == SessionStatus.Completed) Phase = Phase == SessionPhase.Focus ? SessionPhase.Break : SessionPhase.Focus;
            if (Status != SessionStatus.Paused) SetDuration();
            Status = SessionStatus.Running;
        }
        public void Advance(double milliseconds) { if (Status == SessionStatus.Running) Remaining = Math.Max(0, Remaining - milliseconds); }
        public void Tick()
        {
            if (Status != SessionStatus.Running || Remaining != 0) return;
            Status = SessionStatus.Completed; Completions.Add(Phase);
            if (Phase == SessionPhase.Focus) { FocusCount++; LastFocusSeconds = (int)(Duration / 1000); }
        }
        public void Pause() { Tick(); if (Status == SessionStatus.Running) Status = SessionStatus.Paused; }
        public void Reset() { Status = SessionStatus.Ready; Phase = SessionPhase.Focus; SetDuration(); }
        public void Skip() { if (Phase == SessionPhase.Break || Status == SessionStatus.Completed) Reset(); }
    }
}
