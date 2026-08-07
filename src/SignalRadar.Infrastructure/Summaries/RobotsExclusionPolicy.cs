using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SignalRadar.Infrastructure.Summaries;

internal sealed record RobotsAccessDecision(bool Allowed, string Detail);

internal sealed class RobotsPolicyClient
{
    private const string ProductToken = "SignalRadarBot";
    private const string UserAgent =
        "SignalRadarBot/0.1 (+https://github.com/khsfashi/signal-radar)";
    private readonly HttpClient _httpClient;
    private readonly ArticleContentHttpOptions _options;
    private readonly PublicHttpTargetValidator _targetValidator;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, RobotsPolicyCacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public RobotsPolicyClient(
        HttpClient httpClient,
        ArticleContentHttpOptions options,
        PublicHttpTargetValidator targetValidator,
        TimeProvider timeProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _targetValidator = targetValidator
            ?? throw new ArgumentNullException(nameof(targetValidator));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async ValueTask<RobotsAccessDecision> CanFetchAsync(
        Uri target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        string origin = target.GetLeftPart(UriPartial.Authority);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (!_cache.TryGetValue(origin, out RobotsPolicyCacheEntry? entry)
            || entry.ExpiresAt <= now)
        {
            RobotsExclusionPolicy policy = await FetchPolicyAsync(
                target,
                cancellationToken).ConfigureAwait(false);
            entry = new RobotsPolicyCacheEntry(
                policy,
                now.Add(_options.RobotsCacheDuration));
            _cache[origin] = entry;
        }

        bool allowed = entry.Policy.IsAllowed(target, ProductToken);
        return new RobotsAccessDecision(
            allowed,
            allowed
                ? "robots.txt allows the article URL."
                : "robots.txt disallows the article URL for SignalRadarBot.");
    }

    private async ValueTask<RobotsExclusionPolicy> FetchPolicyAsync(
        Uri articleUri,
        CancellationToken cancellationToken)
    {
        Uri requestUri = new(
            articleUri.GetLeftPart(UriPartial.Authority) + "/robots.txt",
            UriKind.Absolute);

        for (int redirectCount = 0; ; redirectCount++)
        {
            try
            {
                await _targetValidator
                    .ValidateAsync(requestUri, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return RobotsExclusionPolicy.DisallowAll;
            }

            using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.ParseAdd("text/plain, text/*;q=0.8, */*;q=0.1");
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RequestTimeout);

            HttpResponseMessage response;

            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return RobotsExclusionPolicy.DisallowAll;
            }

            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    if (redirectCount >= Math.Max(5, _options.MaximumRedirects)
                        || response.Headers.Location is null)
                    {
                        return RobotsExclusionPolicy.DisallowAll;
                    }

                    requestUri = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(requestUri, response.Headers.Location);
                    continue;
                }

                int statusCode = (int)response.StatusCode;

                if (statusCode is >= 400 and <= 499)
                {
                    return RobotsExclusionPolicy.AllowAll;
                }

                if (statusCode is >= 500 and <= 599
                    || !response.IsSuccessStatusCode
                    || response.Content is null)
                {
                    return RobotsExclusionPolicy.DisallowAll;
                }

                byte[] content = await ReadPrefixAsync(
                    response.Content,
                    _options.MaximumRobotsBytes,
                    timeout.Token).ConfigureAwait(false);
                string text = Encoding.UTF8.GetString(content);
                return RobotsExclusionPolicy.Parse(text);
            }
        }
    }

    private static async ValueTask<byte[]> ReadPrefixAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using MemoryStream output = new(Math.Min(maximumBytes, 64 * 1024));
        await using Stream input = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);

        try
        {
            int totalRead = 0;

            while (totalRead < maximumBytes)
            {
                int requested = Math.Min(buffer.Length, maximumBytes - totalRead);
                int read = await input.ReadAsync(
                    buffer.AsMemory(0, requested),
                    cancellationToken).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
                totalRead += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return output.ToArray();
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private sealed record RobotsPolicyCacheEntry(
        RobotsExclusionPolicy Policy,
        DateTimeOffset ExpiresAt);
}

internal sealed class RobotsExclusionPolicy
{
    private const int MaximumRuleLength = 2048;
    private const int MaximumRules = 20_000;
    private readonly IReadOnlyList<RobotsGroup> _groups;
    private readonly bool? _fixedDecision;

    private RobotsExclusionPolicy(bool fixedDecision)
    {
        _groups = [];
        _fixedDecision = fixedDecision;
    }

    private RobotsExclusionPolicy(IReadOnlyList<RobotsGroup> groups)
    {
        _groups = groups;
    }

    public static RobotsExclusionPolicy AllowAll { get; } = new(true);

    public static RobotsExclusionPolicy DisallowAll { get; } = new(false);

    public static RobotsExclusionPolicy Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<RobotsGroup> groups = [];
        List<string> agents = [];
        List<RobotsRule> rules = [];
        bool rulesStarted = false;
        int ruleCount = 0;
        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.None);

        for (int index = 0; index < lines.Length; index++)
        {
            string line = StripComment(lines[index]).Trim();

            if (line.Length == 0)
            {
                continue;
            }

            int separatorIndex = line.IndexOf(':');

            if (separatorIndex <= 0)
            {
                continue;
            }

            string field = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();

            if (field.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
            {
                if (rulesStarted)
                {
                    AddGroup(groups, agents, rules);
                    agents = [];
                    rules = [];
                    rulesStarted = false;
                }

                if (value.Length is > 0 and <= 200)
                {
                    agents.Add(value);
                }

                continue;
            }

            bool isAllow = field.Equals("allow", StringComparison.OrdinalIgnoreCase);
            bool isDisallow = field.Equals("disallow", StringComparison.OrdinalIgnoreCase);

            if ((!isAllow && !isDisallow) || agents.Count == 0)
            {
                continue;
            }

            rulesStarted = true;

            if (value.Length == 0
                || value.Length > MaximumRuleLength
                || ruleCount >= MaximumRules)
            {
                continue;
            }

            RobotsRule? rule = RobotsRule.TryCreate(value, isAllow);

            if (rule is not null)
            {
                rules.Add(rule);
                ruleCount++;
            }
        }

        AddGroup(groups, agents, rules);
        return groups.Count == 0 ? AllowAll : new RobotsExclusionPolicy(groups);
    }

    public bool IsAllowed(Uri target, string productToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(productToken);

        if (_fixedDecision.HasValue)
        {
            return _fixedDecision.Value;
        }

        if (target.AbsolutePath.Equals("/robots.txt", StringComparison.Ordinal))
        {
            return true;
        }

        List<RobotsRule> exactRules = [];
        List<RobotsRule> wildcardRules = [];

        for (int groupIndex = 0; groupIndex < _groups.Count; groupIndex++)
        {
            RobotsGroup group = _groups[groupIndex];
            bool exactMatch = false;
            bool wildcardMatch = false;

            for (int agentIndex = 0; agentIndex < group.Agents.Count; agentIndex++)
            {
                string agent = group.Agents[agentIndex];

                if (agent.Equals(productToken, StringComparison.OrdinalIgnoreCase))
                {
                    exactMatch = true;
                }
                else if (agent == "*")
                {
                    wildcardMatch = true;
                }
            }

            if (exactMatch)
            {
                exactRules.AddRange(group.Rules);
            }
            else if (wildcardMatch)
            {
                wildcardRules.AddRange(group.Rules);
            }
        }

        IReadOnlyList<RobotsRule> applicableRules = exactRules.Count > 0
            ? exactRules
            : wildcardRules;

        if (applicableRules.Count == 0)
        {
            return true;
        }

        string pathAndQuery = target.GetComponents(
            UriComponents.PathAndQuery,
            UriFormat.UriEscaped);
        RobotsRule? selected = null;

        for (int index = 0; index < applicableRules.Count; index++)
        {
            RobotsRule rule = applicableRules[index];

            if (!rule.IsMatch(pathAndQuery))
            {
                continue;
            }

            if (selected is null
                || rule.Specificity > selected.Specificity
                || (rule.Specificity == selected.Specificity
                    && rule.Allow
                    && !selected.Allow))
            {
                selected = rule;
            }
        }

        return selected?.Allow ?? true;
    }

    private static string StripComment(string line)
    {
        int commentIndex = line.IndexOf('#');
        return commentIndex >= 0 ? line[..commentIndex] : line;
    }

    private static void AddGroup(
        List<RobotsGroup> groups,
        List<string> agents,
        List<RobotsRule> rules)
    {
        if (agents.Count == 0)
        {
            return;
        }

        groups.Add(new RobotsGroup(agents.ToArray(), rules.ToArray()));
    }

    private sealed record RobotsGroup(
        IReadOnlyList<string> Agents,
        IReadOnlyList<RobotsRule> Rules);

    private sealed class RobotsRule
    {
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);
        private readonly Regex _regex;

        private RobotsRule(bool allow, int specificity, Regex regex)
        {
            Allow = allow;
            Specificity = specificity;
            _regex = regex;
        }

        public bool Allow { get; }

        public int Specificity { get; }

        public static RobotsRule? TryCreate(string pattern, bool allow)
        {
            bool exactEnd = pattern.EndsWith('$');
            string patternBody = exactEnd ? pattern[..^1] : pattern;
            StringBuilder expression = new(patternBody.Length * 2 + 4);
            expression.Append('^');

            int segmentStart = 0;

            for (int index = 0; index <= patternBody.Length; index++)
            {
                if (index != patternBody.Length && patternBody[index] != '*')
                {
                    continue;
                }

                if (index > segmentStart)
                {
                    expression.Append(Regex.Escape(patternBody[segmentStart..index]));
                }

                if (index < patternBody.Length)
                {
                    expression.Append(".*");
                    segmentStart = index + 1;
                }
            }

            if (exactEnd)
            {
                expression.Append('$');
            }

            try
            {
                Regex regex = new(
                    expression.ToString(),
                    RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                    MatchTimeout);
                string specificityText = patternBody.Replace("*", string.Empty, StringComparison.Ordinal);
                int specificity = Encoding.UTF8.GetByteCount(specificityText);
                return new RobotsRule(allow, specificity, regex);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public bool IsMatch(string pathAndQuery)
        {
            try
            {
                return _regex.IsMatch(pathAndQuery);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }
}
