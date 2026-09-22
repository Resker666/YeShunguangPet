using System;

namespace YeShunguangPet;

public static class QuietHours
{
    public static bool IsActive(PetSettings settings, DateTime localTime)
    {
        if (settings.DoNotDisturb) return true;
        if (!settings.QuietHoursEnabled) return false;
        var minute = localTime.Hour * 60 + localTime.Minute;
        var start = settings.QuietStartMinute;
        var end = settings.QuietEndMinute;
        if (start == end) return true;
        return start < end ? minute >= start && minute < end : minute >= start || minute < end;
    }
}
