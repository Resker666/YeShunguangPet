using System;

namespace YeShunguangPet;

public readonly record struct SpritePlaybackSample(int Frame, bool Completed, TimeSpan UntilNextFrame);

public sealed class SpritePlayback
{
    private readonly TimeProvider _clock;
    private long[] _ends = Array.Empty<long>();
    private long _startedAt, _carriedTicks;
    private bool _loop;
    public bool IsRunning { get; private set; }
    public SpritePlayback(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public void Start(int[] durationsMs, bool loop)
    {
        if (durationsMs.Length is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(durationsMs));
        var ends = new long[durationsMs.Length]; long total = 0;
        for (var i = 0; i < ends.Length; i++)
        {
            if (durationsMs[i] is < 20 or > 10000) throw new ArgumentOutOfRangeException(nameof(durationsMs));
            ends[i] = total += TimeSpan.FromMilliseconds(durationsMs[i]).Ticks;
        }
        _ends = ends; _loop = loop; _carriedTicks = 0;
        _startedAt = _clock.GetTimestamp(); IsRunning = true;
    }

    public void Pause()
    {
        if (!IsRunning) return;
        _carriedTicks = ElapsedTicks(); IsRunning = false;
    }
    public void Resume()
    {
        if (IsRunning || _ends.Length == 0) return;
        _startedAt = _clock.GetTimestamp(); IsRunning = true;
    }
    public SpritePlaybackSample Sample()
    {
        if (_ends.Length == 0) return new(0, true, TimeSpan.Zero);
        var elapsed = ElapsedTicks(); var total = _ends[^1];
        if (!_loop && elapsed >= total) return new(_ends.Length - 1, true, TimeSpan.Zero);
        var position = _loop ? elapsed % total : elapsed;
        var found = Array.BinarySearch(_ends, position);
        var frame = found < 0 ? ~found : found + 1;
        return new(frame, false, TimeSpan.FromTicks(_ends[frame] - position));
    }
    private long ElapsedTicks() => _carriedTicks + (IsRunning ? Math.Max(0, _clock.GetElapsedTime(_startedAt).Ticks) : 0);
}
