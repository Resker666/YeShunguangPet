using System;
using System.Collections.Generic;
using System.Linq;

namespace YeShunguangPet;

public static class AnimationTimeline
{
    public static int FrameAt(IReadOnlyList<int> durations, double milliseconds)
    {
        var elapsed = 0;
        for (var i = 0; i < durations.Count; i++)
        {
            elapsed += durations[i];
            if (milliseconds < elapsed) return i;
        }
        return Math.Max(0, durations.Count - 1);
    }
    public static int StartAt(IReadOnlyList<int> durations, int frame) => durations.Take(Math.Clamp(frame, 0, durations.Count)).Sum();
}
