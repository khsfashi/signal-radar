namespace SignalRadar.Worker.Operations;

public sealed class RedactingConsoleLogger
{
    private readonly string[] _secrets;

    public RedactingConsoleLogger(IEnumerable<string?> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        HashSet<string> unique = new(StringComparer.Ordinal);

        foreach (string? secret in secrets)
        {
            if (!string.IsNullOrWhiteSpace(secret) && secret.Trim().Length >= 6)
            {
                unique.Add(secret.Trim());
            }
        }

        _secrets = [.. unique.OrderByDescending(static value => value.Length)];
    }

    public void WriteLine(string message)
    {
        string safe = message ?? string.Empty;

        for (int index = 0; index < _secrets.Length; index++)
        {
            safe = safe.Replace(
                _secrets[index],
                "[REDACTED]",
                StringComparison.Ordinal);
        }

        Console.WriteLine(
            $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {safe}");
    }
}

public static class StartupSecurityValidator
{
    private static readonly string[] ExampleSecretValues =
    [
        "replace-me",
        "change-me",
        "secret-key",
        "your-token-here"
    ];

    public static void Validate(bool discordEnabled, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        string environment = Environment.GetEnvironmentVariable(
            "SIGNAL_RADAR_ENVIRONMENT") ?? "Development";
        bool production = string.Equals(
            environment.Trim(),
            "Production",
            StringComparison.OrdinalIgnoreCase);

        if (production)
        {
            RejectExampleSecret("DATABASE_CONNECTION_STRING", connectionString);

            if (discordEnabled)
            {
                RejectExampleSecret(
                    "DISCORD_BOT_TOKEN",
                    Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN"));
            }

            string? provider = Environment.GetEnvironmentVariable("SUMMARY_PROVIDER");

            if (string.Equals(provider, "openai-responses", StringComparison.OrdinalIgnoreCase))
            {
                RejectExampleSecret(
                    "OPENAI_API_KEY",
                    Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
            }
            else if (string.Equals(
                provider,
                "gemini-generate-content",
                StringComparison.OrdinalIgnoreCase))
            {
                RejectExampleSecret(
                    "GEMINI_API_KEY",
                    Environment.GetEnvironmentVariable("GEMINI_API_KEY"));
            }
        }
    }

    private static void RejectExampleSecret(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Production setting '{name}' is missing.");
        }

        for (int index = 0; index < ExampleSecretValues.Length; index++)
        {
            if (value.Contains(
                ExampleSecretValues[index],
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Production setting '{name}' still contains an example secret.");
            }
        }
    }
}
