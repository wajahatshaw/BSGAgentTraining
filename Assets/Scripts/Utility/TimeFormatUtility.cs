using System;
using System.Globalization;
using UnityEngine;

public static class TimeFormatUtility
{
    /// <summary>
    /// Returns "Today", "Yesterday", or formatted date (e.g. "09 Jan 2026")
    /// </summary>
    public static string GetDayLabel(string timestamp)
    {
        if (!TryParseUtc(timestamp, out DateTime localTime))
            return string.Empty;

        DateTime today = DateTime.Now.Date;
        DateTime date = localTime.Date;

        if (date == today)
            return "Today";

        if (date == today.AddDays(-1))
            return "Yesterday";

        return localTime.ToString("dd MMM yyyy");
    }

    /// <summary>
    /// If today → returns time only (e.g. "12:15 PM")
    /// If not today → returns date + time
    /// </summary>
    public static string GetTimeOrDateTime(string timestamp)
    {
        if (!TryParseUtc(timestamp, out DateTime localTime))
            return string.Empty;

        if (localTime.Date == DateTime.Now.Date)
        {
            return localTime.ToString("hh:mm tt");
        }

        return localTime.ToString("dd MMM yyyy  hh:mm tt");
    }

    // ============================
    // INTERNAL PARSER
    // ============================
    private static bool TryParseUtc(string timestamp, out DateTime localTime)
    {
        localTime = DateTime.MinValue;

        if (string.IsNullOrEmpty(timestamp))
            return false;

        if (!DateTime.TryParse(
                timestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime utc))
        {
            return false;
        }

        localTime = utc.ToLocalTime();
        return true;
    }


    public static string GetTimeFormatedSecondstoString(int timeInSeconds)
    {
        
        int minutes = Mathf.FloorToInt(timeInSeconds / 60f);
        int seconds = Mathf.FloorToInt(timeInSeconds % 60f);

        string formattedTime = $"{minutes:00}:{seconds:00}";
        return formattedTime;
    }
}
