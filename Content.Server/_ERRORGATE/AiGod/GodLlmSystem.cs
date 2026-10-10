using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._ERRORGATE.CCVar;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._ERRORGATE.AiGod;

/// <summary>
///     The line to the model behind MACHINATION OF RUIN: an OpenAI-compatible chat completions client. Any failure
///     (no network, a bad key, a timeout, junk in the reply) gives null and the caller simply skips that turn, she
///     never blocks the round. Requests are rate limited and capped per round (see the <c>errorgate.god.llm_*</c> CVars).
/// </summary>
/// <remarks>
///     Awaiting a request resumes on the game thread (Robust installs its own synchronization context), so the caller
///     may touch entities afterwards. Do not add <c>ConfigureAwait(false)</c> anywhere on that path.
/// </remarks>
public sealed class GodLlmSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly HttpClient Http = new();

    // Reasoning models put their thinking in front of the answer
    private static readonly Regex ThinkBlock = new(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int MaxErrorBodyLogged = 200;
    private const float BackoffBaseSeconds = 15f;
    private const float BackoffMaxSeconds = 600f;

    /// <summary>
    ///     Sends the HTTP request. Replaceable so tests need no network.
    /// </summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Sender = (request, token) => Http.SendAsync(request, token);

    private TimeSpan _nextAllowed;
    private int _callsThisRound;
    private int _failures;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
    }

    /// <summary>
    ///     Forgets the round's call count and any backoff.
    /// </summary>
    public void Reset()
    {
        _callsThisRound = 0;
        _failures = 0;
        _nextAllowed = TimeSpan.Zero;
    }

    /// <summary>
    ///     Asks the model. Null when the system is off, the request was refused by a limit, or anything went wrong.
    /// </summary>
    /// <param name="chained">A second call of the same turn (a summary before the main call): it may follow at once, the
    /// minimum interval does not apply, but the cap and the backoff still do.</param>
    public async Task<string?> Complete(IReadOnlyList<LlmMessage> messages, bool chained = false)
    {
        if (!_cfg.GetCVar(ErrorgateCVars.GodEnabled))
            return null;

        var url = _cfg.GetCVar(ErrorgateCVars.GodLlmApiUrl);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            Log.Warning("errorgate.god.llm_api_url is not an http(s) address, not sending anything.");
            return null;
        }

        var now = _timing.RealTime;
        if (now < _nextAllowed && !(chained && _failures == 0))
        {
            Log.Debug("LLM request refused: inside the minimum interval or the failure backoff.");
            return null;
        }

        var maxCalls = _cfg.GetCVar(ErrorgateCVars.GodLlmMaxCallsPerRound);
        if (maxCalls > 0 && _callsThisRound >= maxCalls)
        {
            Log.Debug($"LLM request refused: {maxCalls} calls this round already.");
            return null;
        }

        _callsThisRound++;
        _nextAllowed = now + TimeSpan.FromSeconds(Math.Max(0f, _cfg.GetCVar(ErrorgateCVars.GodLlmMinInterval)));

        var body = new LlmRequest
        {
            Model = _cfg.GetCVar(ErrorgateCVars.GodLlmModel),
            Messages = messages.ToList(),
            Temperature = _cfg.GetCVar(ErrorgateCVars.GodLlmTemperature),
            MaxTokens = _cfg.GetCVar(ErrorgateCVars.GodLlmMaxTokens),
            TopP = _cfg.GetCVar(ErrorgateCVars.GodLlmTopP),
            FrequencyPenalty = _cfg.GetCVar(ErrorgateCVars.GodLlmFrequencyPenalty),
            PresencePenalty = _cfg.GetCVar(ErrorgateCVars.GodLlmPresencePenalty),
            Stop = ParseStop(_cfg.GetCVar(ErrorgateCVars.GodLlmStop)),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };

        var key = _cfg.GetCVar(ErrorgateCVars.GodLlmApiKey);
        if (!string.IsNullOrWhiteSpace(key))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var timeout = TimeSpan.FromSeconds(Math.Max(1, _cfg.GetCVar(ErrorgateCVars.GodLlmTimeoutSeconds)));
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            using var response = await Sender(request, cts.Token);
            var text = await response.Content.ReadAsStringAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var shown = text.Length > MaxErrorBodyLogged ? text[..MaxErrorBodyLogged] + "..." : text;
                Fail($"LLM API answered {(int) response.StatusCode}: {shown}");
                return null;
            }

            var content = ExtractContent(text);
            if (content == null)
            {
                Fail("LLM API reply had no usable content.");
                return null;
            }

            _failures = 0;
            return content;
        }
        catch (OperationCanceledException)
        {
            Fail($"LLM API request timed out after {timeout.TotalSeconds:0}s.");
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or InvalidOperationException)
        {
            // The message of an HttpRequestException does not carry the request headers, so the key stays out of the log
            Fail($"LLM API request failed: {e.Message}");
            return null;
        }
    }

    private void Fail(string message)
    {
        Log.Warning(message);

        // Back off, so a dead endpoint is not hammered: 15 s, 30 s, 60 s ... up to 10 minutes
        _failures++;
        var seconds = Math.Min(BackoffBaseSeconds * MathF.Pow(2f, _failures - 1), BackoffMaxSeconds);
        var until = _timing.RealTime + TimeSpan.FromSeconds(seconds);
        if (until > _nextAllowed)
            _nextAllowed = until;
    }

    /// <summary>
    ///     The text of the first choice with any reasoning block removed. Null when the reply is not a chat completion.
    /// </summary>
    public static string? ExtractContent(string json)
    {
        try
        {
            var reply = JsonSerializer.Deserialize<LlmResponse>(json);
            var content = reply?.Choices.FirstOrDefault()?.Message?.Content;
            if (content == null)
                return null;

            content = ThinkBlock.Replace(content, string.Empty).Trim();
            return content.Length == 0 ? null : content;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string>? ParseStop(string stop)
    {
        if (string.IsNullOrWhiteSpace(stop))
            return null;

        return stop.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}

/// <summary>
///     One message of the conversation. Role is "system", "user" or "assistant".
/// </summary>
public sealed class LlmMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    public LlmMessage()
    {
    }

    public LlmMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }
}

public sealed class LlmRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<LlmMessage> Messages { get; set; } = new();

    [JsonPropertyName("temperature")]
    public float Temperature { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("top_p")]
    public float TopP { get; set; }

    [JsonPropertyName("frequency_penalty")]
    public float FrequencyPenalty { get; set; }

    [JsonPropertyName("presence_penalty")]
    public float PresencePenalty { get; set; }

    [JsonPropertyName("stop")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Stop { get; set; }
}

public sealed class LlmResponse
{
    [JsonPropertyName("choices")]
    public List<LlmChoice> Choices { get; set; } = new();
}

public sealed class LlmChoice
{
    [JsonPropertyName("message")]
    public LlmMessage? Message { get; set; }
}
