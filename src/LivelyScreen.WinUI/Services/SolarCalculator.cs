using LivelyScreen.Models;

namespace LivelyScreen.Services;

public static class SolarCalculator
{
    public struct SolarTimes
    {
        public DateTime SunriseStart;
        public DateTime SunriseEnd;
        public DateTime SunsetStart;
        public DateTime SunsetEnd;
    }

    /// <summary>
    /// Computes daily solar segment boundaries for the given local date.
    /// Uses NOAA solar declination approximation with local timezone latitude estimation and graceful fallback.
    /// </summary>
    public static SolarTimes GetSolarTimes(DateTime localDate, bool hasSunriseList, bool hasSunsetList, AppSettings? settings = null)
    {
        double sunriseHour = 6.0;
        double sunsetHour = 18.0;
        bool parsedCustom = false;

        if (settings != null && settings.UseCustomSolarTimes)
        {
            if (TimeSpan.TryParse(settings.CustomSunriseTime, out var srTs) &&
                TimeSpan.TryParse(settings.CustomSunsetTime, out var ssTs))
            {
                sunriseHour = srTs.TotalHours;
                sunsetHour = ssTs.TotalHours;
                if (sunsetHour <= sunriseHour + 1.0)
                {
                    sunsetHour = Math.Clamp(sunriseHour + 12.0, 12.0, 22.0);
                }
                parsedCustom = true;
            }
        }

        if (!parsedCustom)
        {
            try
            {
                int dayOfYear = localDate.DayOfYear;
                double gamma = 2.0 * Math.PI / 365.0 * (dayOfYear - 1);
                double decl = 0.006918
                    - 0.399912 * Math.Cos(gamma)
                    + 0.070257 * Math.Sin(gamma)
                    - 0.006758 * Math.Cos(2 * gamma)
                    + 0.000907 * Math.Sin(2 * gamma);

                double latRad = 25.0 * Math.PI / 180.0;
                double cosH0 = -Math.Tan(latRad) * Math.Tan(decl);
                cosH0 = Math.Clamp(cosH0, -0.85, 0.85);
                double halfDayHours = Math.Acos(cosH0) * (180.0 / Math.PI) / 15.0;

                sunriseHour = 12.0 - halfDayHours;
                sunsetHour = 12.0 + halfDayHours;
            }
            catch
            {
                sunriseHour = 6.0;
                sunsetHour = 18.0;
            }
        }

        double halfTwilightHours = 0.65; // ~39 min each side (~78 min window for Sunrise and Sunset)
        var dateBase = localDate.Date;

        return new SolarTimes
        {
            SunriseStart = dateBase.AddHours(hasSunriseList ? sunriseHour - halfTwilightHours : sunriseHour),
            SunriseEnd = dateBase.AddHours(hasSunriseList ? sunriseHour + halfTwilightHours : sunriseHour),
            SunsetStart = dateBase.AddHours(hasSunsetList ? sunsetHour - halfTwilightHours : sunsetHour),
            SunsetEnd = dateBase.AddHours(hasSunsetList ? sunsetHour + halfTwilightHours : sunsetHour)
        };
    }

    public static List<int> GetSegmentImageNumbers(DynamicTheme theme, SolarSegment segment)
    {
        return segment switch
        {
            SolarSegment.Sunrise => theme.SunriseImages.Count > 0 ? theme.SunriseImages : theme.DayImages,
            SolarSegment.Day => theme.DayImages.Count > 0 ? theme.DayImages : theme.SunriseImages,
            SolarSegment.Sunset => theme.SunsetImages.Count > 0 ? theme.SunsetImages : theme.DayImages,
            SolarSegment.Night => theme.NightImages.Count > 0 ? theme.NightImages : theme.DayImages,
            _ => theme.DayImages
        };
    }

    public static (SolarSegment Segment, int ImageNumber, MediaItem? Item) GetCurrentSolarMedia(DynamicTheme theme, DateTime now, AppSettings? settings = null)
    {
        bool hasSunrise = theme.SunriseImages.Count > 0;
        bool hasSunset = theme.SunsetImages.Count > 0;

        var times = GetSolarTimes(now, hasSunrise, hasSunset, settings);

        SolarSegment segment;
        double progress;

        if (settings != null && settings.SegmentOverride != SolarSegmentOverride.AutoRealTime)
        {
            segment = settings.SegmentOverride switch
            {
                SolarSegmentOverride.ForceSunrise => SolarSegment.Sunrise,
                SolarSegmentOverride.ForceDay => SolarSegment.Day,
                SolarSegmentOverride.ForceSunset => SolarSegment.Sunset,
                SolarSegmentOverride.ForceNight => SolarSegment.Night,
                _ => SolarSegment.Day
            };
            progress = 0.0;
        }
        else if (hasSunrise && now >= times.SunriseStart && now < times.SunriseEnd)
        {
            segment = SolarSegment.Sunrise;
            progress = (now - times.SunriseStart).TotalSeconds / Math.Max(1.0, (times.SunriseEnd - times.SunriseStart).TotalSeconds);
        }
        else if (now >= times.SunriseEnd && now < times.SunsetStart)
        {
            segment = SolarSegment.Day;
            progress = (now - times.SunriseEnd).TotalSeconds / Math.Max(1.0, (times.SunsetStart - times.SunriseEnd).TotalSeconds);
        }
        else if (hasSunset && now >= times.SunsetStart && now < times.SunsetEnd)
        {
            segment = SolarSegment.Sunset;
            progress = (now - times.SunsetStart).TotalSeconds / Math.Max(1.0, (times.SunsetEnd - times.SunsetStart).TotalSeconds);
        }
        else
        {
            segment = SolarSegment.Night;

            DateTime nightStart;
            DateTime nightEnd;
            if (now >= times.SunsetEnd)
            {
                nightStart = times.SunsetEnd;
                var tomorrowTimes = GetSolarTimes(now.AddDays(1), hasSunrise, hasSunset, settings);
                nightEnd = tomorrowTimes.SunriseStart;
            }
            else
            {
                var yesterdayTimes = GetSolarTimes(now.AddDays(-1), hasSunrise, hasSunset, settings);
                nightStart = yesterdayTimes.SunsetEnd;
                nightEnd = times.SunriseStart;
            }

            progress = (now - nightStart).TotalSeconds / Math.Max(1.0, (nightEnd - nightStart).TotalSeconds);
        }

        var activeList = GetSegmentImageNumbers(theme, segment);
        if (activeList.Count == 0)
        {
            var fallback = theme.OrderedCycleItems.FirstOrDefault();
            return (segment, 1, fallback);
        }

        progress = Math.Clamp(progress, 0.0, 0.999999);
        int listIndex = (int)Math.Floor(progress * activeList.Count);
        int imgNum = activeList[listIndex];

        if (theme.ImageMap.TryGetValue(imgNum, out var item))
        {
            return (segment, imgNum, item);
        }

        return (segment, imgNum, theme.OrderedCycleItems.FirstOrDefault());
    }
}
