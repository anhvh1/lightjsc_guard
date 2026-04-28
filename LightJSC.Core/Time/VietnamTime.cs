using System.Globalization;

namespace LightJSC.Core.Time;

public static class VietnamTime
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    public static TimeZoneInfo ZoneInfo => Zone;

    public static DateTimeOffset NowOffset()
    {
        return ConvertToVietnam(DateTimeOffset.UtcNow);
    }

    public static DateTime Now()
    {
        return NowOffset().DateTime;
    }

    public static DateTimeOffset ConvertToVietnam(DateTimeOffset input)
    {
        return TimeZoneInfo.ConvertTime(input, Zone);
    }

    public static DateTime ToLocalDateTime(DateTimeOffset input)
    {
        return ConvertToVietnam(input).DateTime;
    }

    public static DateTimeOffset FromLocal(DateTime localTime)
    {
        var unspecified = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        var offset = Zone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }

    private static TimeZoneInfo ResolveZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "Asia/Ho_Chi_Minh",
            TimeSpan.FromHours(7),
            "Vietnam",
            "Vietnam");
    }
}
