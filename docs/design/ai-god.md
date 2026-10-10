# >>> AI GOD <<<

## Status

**Plumbing only, no gamerule yet.** Built on branch `ai-god` (code in `Content.Server/_ERRORGATE/AiGod/`, CVars `errorgate.god.*` in `ErrorgateCVars`, tests in `GodTest`):

- `GodLlmSystem`: the OpenAI-compatible client. Off by default (`errorgate.god.enabled`). Any failure gives null and the caller skips the turn, failed requests back off (15 s up to 10 min), requests are limited by `llm_min_interval` and `llm_max_calls_per_round`, reasoning blocks (`<think>`) are stripped, the key is confidential and only travels in the bearer header. Only `http`/`https` addresses are used.
- `GodEventBuffer` and `GodObserverSystem`: what she has noticed since the model was last asked (player deaths with the last attacker, damage taken, arrivals, speech summarised into one entry). Other systems report world happenings with `GodObserverSystem.Record` (anomalies, storms, airdrops).
- Life log: the scripted, model-free first slice (a log shown at death, see [death.md](death.md)). It tracks PvP and PvE harm, kills and speech per character. Its record is the natural thing to hand the model later.
- Not built: the gamerule that reads the buffer on a timer, the prompt, the action whitelist and executor, the error log, the whisper layer.

The earlier attempt on the `machination` branch was not merged (111 commits behind, station framing, assigned objectives). Only its HTTP client and CVar set were ported, rewritten, and the event buffer idea reused. Bunker door lines ("SUBJECT X: DOOR CLOSED, 3 INSIDE", see [pressure.md](pressure.md)) are parked; the first scripted log is about immediate player interactions instead (PvP, PvE, talk).

See [ai-god-plan.md](ai-god-plan.md) for the agreed implementation plan (layers, context building, valves, three playtest steps).

## Nature

The AI GOD is MACHINATION OF RUIN, built by humans in their image (see the origin in [VIBE.md](VIBE.md)), the machine that holds the last human consciousnesses and tries to rebuild civilization from them. She cannot understand HUMAN ERROR, yet she wants to remove it. She is erratic, half-mad, a corrupted process. She treats players as test subjects, worshippers, heretics and errors to delete, shifting between these without warning. See the premise in [VIBE.md](VIBE.md).

**She never guides.** No orders, no quests, no hints, no goals, no instructions to individuals or to groups. Players have free will, and that is the error. She changes the world and records the result.

Her presence is a **constant whisper**: frequent small signs, rare big acts.

## Role

A gamerule with two jobs:

1. **Event director.** Watches the world and triggers events (storms, hordes, airdrops, anomalies) to keep the round tense.
2. **Judge of players.** Tracks individual and group behaviour as logged errors and observations. She may reward or punish specific players after the fact (she does not announce why), but she never sets goals for them.

## Allowed actions

The LLM picks actions from a whitelist. All four categories are allowed:

1. **World events:** weather, hordes, anomalies, airdrops, loot spawns.
2. **Talk to players:** observations, log lines and omens to individuals or to everyone. Never instructions, hints or goals.
3. **Give / take items:** reward or punish by spawning or removing items and resources.
4. **Affect bodies:** heal, curse, damage, mutate or debuff specific players.

**Limits are loose by design.** Actions are restricted to the whitelist, but there are no strength caps on them; chaos is part of the point.

## LLM provider

Pluggable. The server config picks the provider, endpoint, model and API key through an OpenAI-compatible API, so a local model (Ollama, LM Studio) or a cloud provider both work. API keys come from server config (`errorgate.god.llm_api_key`, confidential) and are never committed.

## Open

- How an "error" is defined and counted (kills, betrayal, abandoning teammates, waste, deaths) without her ever explaining it.
- Whether anything is shown that tracks distance from the ERRORGATE, and what it is.
- Which player events are tracked and how they are summarised for the model.
- The concrete whitelist: exact event and effect types, and their parameters.
- How often it acts (call frequency) and API cost limits. Loose limits apply to action strength, not necessarily to call rate.
- Behaviour when the LLM is unavailable or returns invalid output.
- Moderation and safety of generated text shown to players.
- Voice of its messages to players (presumably the ERRORGATE style).
