# >>> AI GOD <<<

## Nature

The AI GOD is what ravaged the world. It broke, but it is still running: erratic, half-mad, a corrupted process. It treats players as test subjects, worshippers, heretics and errors to delete, shifting between these without warning. Its presence is a **constant whisper**: frequent small signs, rare big acts. See [VIBE.md](VIBE.md).

## Role

A gamerule with two jobs:

1. **Event director.** Watches the world and triggers events (storms, hordes, airdrops, anomalies) to keep the round tense.
2. **Judge of players.** Tracks individual behaviour and rewards or punishes specific players, and sets goals for them.

## Allowed actions

The LLM picks actions from a whitelist. All four categories are allowed:

1. **World events:** weather, hordes, anomalies, airdrops, loot spawns.
2. **Talk to players:** messages and omens to individuals or to everyone.
3. **Give / take items:** reward or punish by spawning or removing items and resources.
4. **Affect bodies:** heal, curse, damage, mutate or debuff specific players.

**Limits are loose by design.** Actions are restricted to the whitelist, but there are no strength caps on them; chaos is part of the point.

## LLM provider

Pluggable. The server config picks the provider, endpoint, model and API key through an OpenAI-compatible API, so a local model (Ollama, LM Studio) or a cloud provider both work. API keys come from server config and are never committed.

## Open

- Which player events are tracked and how they are summarised for the model.
- The concrete whitelist: exact event and effect types, and their parameters.
- How often it acts (call frequency) and API cost limits. Loose limits apply to action strength, not necessarily to call rate.
- Behaviour when the LLM is unavailable or returns invalid output.
- Moderation and safety of generated text shown to players.
- Voice of its messages to players (presumably the ERRORGATE style).
