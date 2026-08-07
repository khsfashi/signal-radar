namespace SignalRadar.Worker.Runtime;

internal static class WorkerEnvironment
{
    public static string GetRequired(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);

        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Required environment variable '{name}' is missing.");
    }

    public static ulong[] ParseSnowflakes(string name, bool required)
    {
        string? value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            return required
                ? throw new InvalidOperationException(
                    $"Required environment variable '{name}' is missing.")
                : [];
        }

        string[] segments = value.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        ulong[] ids = new ulong[segments.Length];

        for (int index = 0; index < segments.Length; index++)
        {
            if (!ulong.TryParse(segments[index], out ulong id) || id == 0)
            {
                throw new InvalidOperationException(
                    $"Environment variable '{name}' contains an invalid Discord snowflake.");
            }

            ids[index] = id;
        }

        return ids;
    }

    public static bool ParseBoolean(string name, bool defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return bool.TryParse(value, out bool parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Environment variable '{name}' must be 'true' or 'false'.");
    }

    public static int ParseInteger(
        string name,
        int defaultValue,
        int minimum,
        int maximum)
    {
        string? value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out int parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            throw new InvalidOperationException(
                $"Environment variable '{name}' must be between {minimum} and {maximum}.");
        }

        return parsed;
    }
}
