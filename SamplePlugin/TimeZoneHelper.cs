using System;

namespace SamplePlugin
{
    /// <summary>
    /// Helper class for managing timezone conversions with proper daylight savings handling
    /// </summary>
    public static class TimeZoneHelper
    {
        // Cache the TimeZoneInfo to avoid repeated lookups
        private static TimeZoneInfo _easternTimeZone;

        static TimeZoneHelper()
        {
            // Initialize Eastern Time zone with proper fallback mechanisms
            try
            {
                // Try Windows timezone ID first
                _easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
            catch
            {
                try
                {
                    // Try IANA timezone ID (for non-Windows platforms)
                    _easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
                }
                catch (Exception ex)
                {
                    // If all lookups fail, create a custom timezone with DST rules for Eastern Time
                    // This is a last resort fallback that attempts to implement basic US Eastern Time rules

                    // Create Eastern Standard Time (-5 hours)
                    var estOffset = new TimeSpan(-5, 0, 0);

                    // Create Eastern Daylight Time (-4 hours) 
                    var edtOffset = new TimeSpan(-4, 0, 0);

                    // Create basic DST rule adjustments - these are simplified and should only be used as fallback
                    // Second Sunday in March to First Sunday in November
                    var dstStart = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                        DateTime.MinValue,
                        DateTime.MaxValue,
                        new TimeSpan(1, 0, 0),  // 1 hour adjustment
                        TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                            new DateTime(1, 1, 1, 2, 0, 0), // 2 AM
                            3,  // March
                            2,  // Second week
                            DayOfWeek.Sunday),
                        TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                            new DateTime(1, 1, 1, 2, 0, 0), // 2 AM 
                            11, // November
                            1,  // First week
                            DayOfWeek.Sunday)
                    );

                    try
                    {
                        // Create custom timezone info
                        _easternTimeZone = TimeZoneInfo.CreateCustomTimeZone(
                            "Eastern Time (Custom)",
                            estOffset,
                            "Eastern Time",
                            "EST",
                            "EDT",
                            new[] { dstStart }
                        );
                    }
                    catch
                    {
                        // If everything fails, use null indicator
                        _easternTimeZone = null;
                    }
                }
            }
        }

        /// <summary>
        /// Converts a UTC time to Eastern Time, automatically handling daylight savings
        /// </summary>
        /// <param name="utcTime">The UTC time to convert</param>
        /// <returns>The time in Eastern Time (either EST or EDT based on daylight savings)</returns>
        public static DateTime ConvertUtcToEastern(DateTime utcTime)
        {
            // Ensure the time is properly marked as UTC
            DateTime utcTimeProper = DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);

            if (_easternTimeZone != null)
            {
                return TimeZoneInfo.ConvertTimeFromUtc(utcTimeProper, _easternTimeZone);
            }
            else
            {
                // Ultimate fallback - guess based on month (very imprecise)
                // DST is typically active from March to November
                int month = utcTime.Month;
                DateTime result;

                if (month >= 3 && month <= 11)
                {
                    // During probable DST months
                    result = utcTime.AddHours(-4);
                }
                else
                {
                    // During probable standard time months
                    result = utcTime.AddHours(-5);
                }

                // Ensure result is marked as Unspecified since it's not in any specific timezone
                return DateTime.SpecifyKind(result, DateTimeKind.Unspecified);
            }
        }

        /// <summary>
        /// Gets the current abbreviation (EST/EDT) for Eastern Time based on whether DST is active
        /// </summary>
        /// <param name="dateTime">The date to check (can be in any timezone)</param>
        /// <returns>Either "EST" or "EDT" based on daylight savings status</returns>
        public static string GetEasternTimeAbbreviation(DateTime dateTime)
        {
            if (_easternTimeZone == null)
            {
                // Fallback guess based on month
                int month = dateTime.Month;
                return (month >= 3 && month <= 11) ? "EDT" : "EST";
            }

            try
            {
                // The safest approach is to check what the abbreviation would be for the
                // equivalent eastern time, regardless of what timezone the input is in

                // First convert input to UTC to normalize it
                DateTime utcTime;
                if (dateTime.Kind == DateTimeKind.Utc)
                {
                    utcTime = dateTime;
                }
                else
                {
                    // For Local or Unspecified, treat as if it's already in Eastern time
                    // and convert to what that equivalent time would be in UTC
                    utcTime = TimeZoneInfo.ConvertTimeToUtc(
                        DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified),
                        _easternTimeZone);
                }

                // Now convert back to Eastern time and check DST
                var easternTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, _easternTimeZone);
                return _easternTimeZone.IsDaylightSavingTime(easternTime) ? "EDT" : "EST";
            }
            catch
            {
                // Even more basic fallback if the conversion fails
                int month = dateTime.Month;
                return (month >= 3 && month <= 11) ? "EDT" : "EST";
            }
        }
    }
}
