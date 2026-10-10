using Robust.Shared.Configuration;

namespace Content.Shared._ERRORGATE.CCVar;

[CVarDefs]
public sealed class ErrorgateCVars
{
    /// <summary>
    ///     Immersive interactions: worn items cannot be used unless they allow it, and items inside containers cannot be
    ///     interacted with directly. Replicated because the checks are predicted by the client.
    /// </summary>
    public static readonly CVarDef<bool> ImmersiveInteractions =
        CVarDef.Create("errorgate.immersive_interactions", true, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    ///     Seconds a dead player has to wait in the death void before they can respawn. 0 means immediately.
    ///     Admins using <c>forcerespawn</c> ignore it.
    /// </summary>
    public static readonly CVarDef<float> RespawnCooldown =
        CVarDef.Create("errorgate.respawn_cooldown", 0f, CVar.SERVER | CVar.ARCHIVE);

    /// <summary>
    ///     Players beyond normal hearing range hear a muffled, echoing copy of gunshots on the same grid.
    /// </summary>
    public static readonly CVarDef<bool> DistantGunfireEnabled =
        CVarDef.Create("errorgate.distant_gunfire_enabled", true, CVar.SERVER | CVar.ARCHIVE);

    /// <summary>
    ///     Multiplier for how far distant gunfire carries beyond the normal hearing range (twice the PVS range).
    ///     1 uses the per-caliber ranges as authored, the default 3 suits Kuznetsk.
    /// </summary>
    public static readonly CVarDef<float> DistantGunfireRangeScale =
        CVarDef.Create("errorgate.distant_gunfire_range_scale", 3f, CVar.SERVER | CVar.ARCHIVE);

    #region AI GOD (MACHINATION OF RUIN)

    /// <summary>
    ///     Master switch. Off by default: nothing is observed, buffered or sent anywhere unless a host turns it on.
    /// </summary>
    public static readonly CVarDef<bool> GodEnabled =
        CVarDef.Create("errorgate.god.enabled", false, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Chat completions endpoint of an OpenAI-compatible API (a cloud provider, or Ollama / LM Studio locally).
    /// </summary>
    public static readonly CVarDef<string> GodLlmApiUrl =
        CVarDef.Create("errorgate.god.llm_api_url", "http://localhost:11434/v1/chat/completions", CVar.SERVERONLY);

    /// <summary>
    ///     API key, sent as a bearer token. Keep it in the server config, never in the repository. Confidential: it is
    ///     hidden from the CVar listing and never logged.
    /// </summary>
    public static readonly CVarDef<string> GodLlmApiKey =
        CVarDef.Create("errorgate.god.llm_api_key", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    public static readonly CVarDef<string> GodLlmModel =
        CVarDef.Create("errorgate.god.llm_model", "llama3.1", CVar.SERVERONLY);

    public static readonly CVarDef<float> GodLlmTemperature =
        CVarDef.Create("errorgate.god.llm_temperature", 0.9f, CVar.SERVERONLY);

    public static readonly CVarDef<int> GodLlmMaxTokens =
        CVarDef.Create("errorgate.god.llm_max_tokens", 600, CVar.SERVERONLY);

    public static readonly CVarDef<float> GodLlmTopP =
        CVarDef.Create("errorgate.god.llm_top_p", 0.9f, CVar.SERVERONLY);

    public static readonly CVarDef<float> GodLlmFrequencyPenalty =
        CVarDef.Create("errorgate.god.llm_frequency_penalty", 0f, CVar.SERVERONLY);

    public static readonly CVarDef<float> GodLlmPresencePenalty =
        CVarDef.Create("errorgate.god.llm_presence_penalty", 0f, CVar.SERVERONLY);

    /// <summary>
    ///     Comma separated stop sequences. Empty sends no stop parameter, the backend default applies.
    /// </summary>
    public static readonly CVarDef<string> GodLlmStop =
        CVarDef.Create("errorgate.god.llm_stop", "", CVar.SERVERONLY);

    /// <summary>
    ///     Seconds before a request is given up on. Raise it for slow local models.
    /// </summary>
    public static readonly CVarDef<int> GodLlmTimeoutSeconds =
        CVarDef.Create("errorgate.god.llm_timeout_seconds", 60, CVar.SERVERONLY);

    /// <summary>
    ///     Least seconds between two requests. Requests inside the window are refused without being sent (cost control).
    /// </summary>
    public static readonly CVarDef<float> GodLlmMinInterval =
        CVarDef.Create("errorgate.god.llm_min_interval", 20f, CVar.SERVERONLY);

    /// <summary>
    ///     Most requests per round, 0 for no limit (cost control). Resets when the round restarts.
    /// </summary>
    public static readonly CVarDef<int> GodLlmMaxCallsPerRound =
        CVarDef.Create("errorgate.god.llm_max_calls_per_round", 0, CVar.SERVERONLY);

    /// <summary>
    ///     Newest observed events kept for the next request, older ones are dropped.
    /// </summary>
    public static readonly CVarDef<int> GodMaxEvents =
        CVarDef.Create("errorgate.god.max_events", 60, CVar.SERVERONLY);

    /// <summary>
    ///     Whether in-character speech is observed.
    /// </summary>
    public static readonly CVarDef<bool> GodMonitorChat =
        CVarDef.Create("errorgate.god.monitor_chat", true, CVar.SERVERONLY);

    /// <summary>
    ///     Chat lines kept between two observations, the oldest are dropped. The lot is summarised into one event.
    /// </summary>
    public static readonly CVarDef<int> GodChatBufferSize =
        CVarDef.Create("errorgate.god.chat_buffer_size", 80, CVar.SERVERONLY);

    /// <summary>
    ///     Messages of conversation history sent with each request (the system prompt is always kept).
    /// </summary>
    public static readonly CVarDef<int> GodMaxConversationMessages =
        CVarDef.Create("errorgate.god.max_conversation_messages", 12, CVar.SERVERONLY);

    /// <summary>
    ///     Side of a sector in tiles (a sector is a named square of the map, like C4). A SectorGrid component on a grid overrides it.
    /// </summary>
    public static readonly CVarDef<int> GodSectorSize =
        CVarDef.Create("errorgate.god.sector_size", 64, CVar.SERVERONLY);

    /// <summary>
    ///     Ordinary speech lines kept as context for the model (besides the lines that mention her), newest ones.
    /// </summary>
    public static readonly CVarDef<int> GodContextLines =
        CVarDef.Create("errorgate.god.context_lines", 12, CVar.SERVERONLY);

    /// <summary>
    ///     Most prayers and mention lines held between two calls.
    /// </summary>
    public static readonly CVarDef<int> GodMaxQuotes =
        CVarDef.Create("errorgate.god.max_quotes", 40, CVar.SERVERONLY);

    /// <summary>
    ///     How she decides: "scripted" (fixed rules and a line bank, no model), "assisted" (the model picks, scripted rules fill the gaps)
    ///     or "full" (the model alone). Anything else counts as scripted.
    /// </summary>
    public static readonly CVarDef<string> GodMode =
        CVarDef.Create("errorgate.god.mode", "scripted", CVar.SERVERONLY);

    /// <summary>
    ///     Decide and log, but do nothing. On by default so that a new server never acts without an admin choosing it.
    /// </summary>
    public static readonly CVarDef<bool> GodDryRun =
        CVarDef.Create("errorgate.god.dry_run", true, CVar.SERVERONLY);

    /// <summary>
    ///     An admin confirms every action (godapprove, goddeny). Unanswered ones expire as denied.
    /// </summary>
    public static readonly CVarDef<bool> GodApproval =
        CVarDef.Create("errorgate.god.approval", true, CVar.SERVERONLY);

    public static readonly CVarDef<int> GodApprovalTimeoutSeconds =
        CVarDef.Create("errorgate.god.approval_timeout", 120, CVar.SERVERONLY);

    /// <summary>
    ///     Wrath points gained per minute, and the most that can be saved up. Every action costs points.
    /// </summary>
    public static readonly CVarDef<float> GodBudgetPerMinute =
        CVarDef.Create("errorgate.god.budget_per_minute", 3f, CVar.SERVERONLY);

    public static readonly CVarDef<float> GodBudgetCap =
        CVarDef.Create("errorgate.god.budget_cap", 20f, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds before the same subject can be the target of another action.
    /// </summary>
    public static readonly CVarDef<float> GodTargetCooldown =
        CVarDef.Create("errorgate.god.target_cooldown", 90f, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds between two global announcements, and the most per hour.
    /// </summary>
    public static readonly CVarDef<float> GodAnnounceCooldown =
        CVarDef.Create("errorgate.god.announce_cooldown", 600f, CVar.SERVERONLY);

    public static readonly CVarDef<int> GodAnnouncePerHour =
        CVarDef.Create("errorgate.god.announce_per_hour", 3, CVar.SERVERONLY);

    /// <summary>
    ///     Players needed before she does anything, and seconds after the round starts in which she stays quiet.
    /// </summary>
    public static readonly CVarDef<int> GodMinPlayers =
        CVarDef.Create("errorgate.god.min_players", 1, CVar.SERVERONLY);

    public static readonly CVarDef<float> GodQuietSeconds =
        CVarDef.Create("errorgate.god.quiet_seconds", 120f, CVar.SERVERONLY);

    public static readonly CVarDef<int> GodSubtleMaxLength =
        CVarDef.Create("errorgate.god.subtle_max_length", 140, CVar.SERVERONLY);

    public static readonly CVarDef<int> GodAnnounceMaxLength =
        CVarDef.Create("errorgate.god.announce_max_length", 200, CVar.SERVERONLY);

    /// <summary>
    ///     Her messages are shown in capitals, like a machine log.
    /// </summary>
    public static readonly CVarDef<bool> GodUppercase =
        CVarDef.Create("errorgate.god.uppercase", true, CVar.SERVERONLY);

    /// <summary>
    ///     Average seconds between her small signs (a line or a glitch for one subject) when nothing else happens.
    /// </summary>
    public static readonly CVarDef<float> GodWhisperInterval =
        CVarDef.Create("errorgate.god.whisper_interval", 150f, CVar.SERVERONLY);

    /// <summary>
    ///     Seconds after a prayer at an altar before the answer line.
    /// </summary>
    public static readonly CVarDef<float> GodPrayerDelay =
        CVarDef.Create("errorgate.god.prayer_delay", 8f, CVar.SERVERONLY);

    #endregion
}
