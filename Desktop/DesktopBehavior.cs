using System;

namespace YeShunguangPet;

public static class DesktopBehavior
{
    public static bool IsQuiet(PetSettings settings, DateTime localTime)
    {
        if (settings.DoNotDisturb) return true;
        if (!settings.QuietHoursEnabled) return false;
        var minute = localTime.Hour * 60 + localTime.Minute;
        var start = settings.QuietStartMinute;
        var end = settings.QuietEndMinute;
        if (start == end) return true;
        return start < end ? minute >= start && minute < end : minute >= start || minute < end;
    }

    public static bool ExceedsDragThreshold(double dx, double dy, double horizontal, double vertical)
        => Math.Abs(dx) >= horizontal || Math.Abs(dy) >= vertical;

    public static bool IsNear(double x, double y, double width, double height, double radius)
    {
        var dx = Math.Max(0, Math.Max(-x, x - width));
        var dy = Math.Max(0, Math.Max(-y, y - height));
        return dx * dx + dy * dy <= radius * radius;
    }

    public static RoamStep Move(double left, int direction, double remaining, double distance, double minimum, double maximum)
    {
        left = Math.Clamp(left, minimum, Math.Max(minimum, maximum));
        if (maximum <= minimum || remaining <= 0) return new RoamStep(left, direction, 0);
        var step = Math.Min(Math.Max(0, distance), remaining);
        remaining -= step;
        while (step > 0)
        {
            var available = direction > 0 ? maximum - left : left - minimum;
            var travel = Math.Min(available, step);
            left += direction * travel;
            step -= travel;
            if (travel >= available) direction = -direction;
        }
        return new RoamStep(left, direction, remaining);
    }
}

public readonly record struct RoamStep(double Left, int Direction, double Remaining);
