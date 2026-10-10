# >>> AI GOD <<<

## Status

**Not implemented. No code exists for MACHINATION OF RUIN yet**: no gamerule, no event director, no error logging, no LLM provider, no whisper layer. Everything here is design. When implementation starts, a good first slice that works without the LLM is a scripted log: lines such as "SUBJECT X: DOOR CLOSED, 3 INSIDE" written when players use the final bunker doors on EDGE OF ENTROPY (see [pressure.md](pressure.md)).

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

Pluggable. The server config picks the provider, endpoint, model and API key through an OpenAI-compatible API, so a local model (Ollama, LM Studio) or a cloud provider both work. API keys come from server config and are never committed.

## Open

- How an "error" is defined and counted (kills, betrayal, abandoning teammates, waste, deaths) without her ever explaining it.
- Whether anything is shown that tracks distance from the ERRORGATE, and what it is.
- Which player events are tracked and how they are summarised for the model.
- The concrete whitelist: exact event and effect types, and their parameters.
- How often it acts (call frequency) and API cost limits. Loose limits apply to action strength, not necessarily to call rate.
- Behaviour when the LLM is unavailable or returns invalid output.
- Moderation and safety of generated text shown to players.
- Voice of its messages to players (presumably the ERRORGATE style).
