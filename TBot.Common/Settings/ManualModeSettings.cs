namespace Tbot.Common.Settings {
    public static class ManualModeSettings {
        public const int DefaultTimeoutSeconds = 120;

        public static int ReadTimeoutSeconds(object generalSettings) {
            if (generalSettings is not IDictionary<string, object> general ||
                !general.TryGetValue("ManualModeTimeoutSeconds", out var value))
                return DefaultTimeoutSeconds;

            // JSON integer values are Int64; do not silently truncate fractions or
            // accept strings/bools through dynamic conversion.
            if (value is long seconds && seconds > 0 && seconds <= int.MaxValue)
                return (int)seconds;
            if (value is int integerSeconds && integerSeconds > 0)
                return integerSeconds;

            throw new ArgumentException(
                "General.ManualModeTimeoutSeconds must be a positive whole number of seconds (1 to 2147483647).");
        }
    }
}
