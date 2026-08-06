using SignalRadar.Application.Articles;

namespace SignalRadar.Infrastructure.Articles;

public sealed class CanonicalUrlNormalizer : IUrlCanonicalizer
{
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid",
        "gclid",
        "mc_cid",
        "mc_eid"
    };

    public Uri Normalize(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? parsedUri))
        {
            throw new ArgumentException("The article URL is invalid.", nameof(url));
        }

        if (parsedUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Only HTTP and HTTPS article URLs are supported.", nameof(url));
        }

        UriBuilder builder = new(parsedUri)
        {
            Fragment = string.Empty,
            Host = parsedUri.IdnHost
        };

        if (parsedUri.IsDefaultPort)
        {
            builder.Port = -1;
        }

        List<QueryParameter> retainedParameters = ParseQuery(parsedUri.Query);
        retainedParameters.RemoveAll(static parameter => IsTrackingParameter(parameter.Key));
        retainedParameters.Sort(QueryParameterComparer.Instance);

        builder.Query = string.Join(
            "&",
            retainedParameters.Select(static parameter => parameter.EncodedPair));

        return builder.Uri;
    }

    private static List<QueryParameter> ParseQuery(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return [];
        }

        string queryWithoutPrefix = query[0] == '?' ? query[1..] : query;
        string[] pairs = queryWithoutPrefix.Split(
            '&',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<QueryParameter> parameters = new(pairs.Length);

        foreach (string pair in pairs)
        {
            int separatorIndex = pair.IndexOf('=');
            string encodedKey = separatorIndex >= 0 ? pair[..separatorIndex] : pair;
            string encodedValue = separatorIndex >= 0 ? pair[(separatorIndex + 1)..] : string.Empty;
            string decodedKey = Uri.UnescapeDataString(encodedKey.Replace('+', ' '));

            parameters.Add(new QueryParameter(decodedKey, encodedKey, encodedValue));
        }

        return parameters;
    }

    private static bool IsTrackingParameter(string key)
    {
        return key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
            || TrackingParameters.Contains(key);
    }

    private readonly record struct QueryParameter(
        string Key,
        string EncodedKey,
        string EncodedValue)
    {
        public string EncodedPair => string.IsNullOrEmpty(EncodedValue)
            ? EncodedKey
            : $"{EncodedKey}={EncodedValue}";
    }

    private sealed class QueryParameterComparer : IComparer<QueryParameter>
    {
        public static QueryParameterComparer Instance { get; } = new();

        public int Compare(QueryParameter x, QueryParameter y)
        {
            int keyComparison = StringComparer.Ordinal.Compare(x.EncodedKey, y.EncodedKey);

            return keyComparison != 0
                ? keyComparison
                : StringComparer.Ordinal.Compare(x.EncodedValue, y.EncodedValue);
        }
    }
}
