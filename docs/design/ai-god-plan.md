# >>> AI GOD: IMPLEMENTATION PLAN <<<

How MACHINATION OF RUIN is built. Read [VIBE.md](VIBE.md) and [ai-god.md](ai-god.md) first. This plan is agreed in outline with the project owner; items marked **Open** still need a decision. Nothing here is built except the plumbing listed under "What exists".

## Decisions so far

| Topic | Decision |
|---|---|
| Speech | She may address players. **Subtle message** for one subject or a group, **global announcement** for the whole map, both through existing systems (see Communication). |
| Hearing | Players reach her by **prayer**: a mechanic that sends a message visible to admins. Chat lines that mention her or refer to a higher power are also watched. |
| Error | **Death is an error.** Everything else is adaptation, for now. Other behaviour is still observed and told to the model, but it is not scored. |
| Harm | **No instant kill.** Otherwise she can go wild: complicate lives, make people suffer, for better or worse. |
| Targets | Individuals, groups and places. |
| Memory | None across rounds. |
| Approval | The first playtests run in **approval mode**: an admin confirms each action. |
| Hosting | No privacy requirement beyond the valves below. |
| Delivery | Three steps, each playtested on its own (see Steps). |

## What exists (branch `ai-god`)

- `GodLlmSystem`: OpenAI-compatible client with backoff, rate limit, per-round cap, `<think>` stripping. Off by default.
- `GodEventBuffer` and `GodObserverSystem`: player deaths (with last attacker), damage, arrivals, speech. World systems report with `Record(...)`.
- Life log (`LifeLogSystem`): per-mind event list used for the death message. Its per-subject records are the base of the ledger below.
- Existing systems to reuse: `PrayerSystem.SendSubtleMessage(...)` (local chat message plus popup, used by admins today), `ChatSystem.DispatchGlobalAnnouncement(...)`, `PrayableComponent` and `PrayerSystem.Pray(...)` (message to admins and the admin log), anomalies (`AnomalyField`), `SmartMobSpawner`, `LootManager`, the weather system, status effects, the death void.

## Layers

```
 sensors ──> ledger ──> digest ──> brain ──> validator ──> actuators
 (events)   (subjects,   (text,     scripted   (whitelist,   (effects in
             world)       budgeted)  + LLM)     valves)       the world)
                                       ^                          |
                                       └──── her recent actions ──┘
```

1. **Sensors.** The observer (done) plus: movement by sector, nearness of subjects, prayers, mentions, item handovers (later), loot taken.
2. **Ledger.** Per round, per subject (mind): stable number (`S17`), character name, lives, errors (deaths), time alive, sector, group, kills and damage dealt, last words, prayers. Plus a world snapshot: players, deaths, time of day, weather, faults awake, loot pool left. Reset every round.
3. **Digest.** A compact text built deterministically from the ledger (see Context). Unit tested.
4. **Brain.**
   - **Scripted director:** timers and triggers, no model. Keeps the whisper alive and is the fallback.
   - **LLM director:** called every few minutes with the digest, returns structured actions.
   - **Writer mode (step 1 extension):** the model also fills a **line bank** of omens and log lines that the scripted director uses in real time, so the whisper stays frequent and cheap.
5. **Validator.** Whitelist, parameter schema, target rules, budget, cooldowns, text rules. Anything that fails is dropped and logged, and the scripted director may substitute a line.
6. **Actuators.** Each action is a small system that does one thing with existing game systems.

## Communication

- **Subtle message:** one subject, or a group (one message per subject). Uses `SendSubtleMessage`. Voice: cold system-log language, sometimes the character's name. No instructions, hints or goals.
- **Global announcement:** whole map. Uses `DispatchGlobalAnnouncement` with her own sender name. Rare and loud.
- **Environmental whisper:** no text. Glitch bursts (static, flicker, sound) around a subject or a sector. Frequent and small.
- **Prayer (Open: exact form):** a "pray" verb on every player body, using the existing prayer message to admins plus an event the observer records. An in-world altar can be added later. The text goes to admins and, if valves allow, to the model.
- **Mentions:** a keyword prefilter (a prototype list: god, machine, machination, ruin, errorgate, creator, pray, lord, divine, savior, "who are you"...) selects chat lines. They are passed verbatim (cut to 120 characters, at most 5 per call) and the model decides what they mean.

## Error accounting

The ledger counts **deaths** per subject and per round, nothing else. Everything else a subject does is "adaptation": still told to the model as plain observations, but with no score. A valve can add scored categories later if the owner wants them.

## Context: how the prompt is built

Calls are **stateless**: a fixed system prompt plus a fresh digest. There is no growing chat history, so the cost of a call is predictable and small. Long-term memory is a few lines of **notes** the model writes itself each call and gets back next time.

### Budget per call

| Part | Tokens (approx.) |
|---|---|
| System prompt (premise, rules, action catalog, output schema) | 900, identical every call |
| Digest | up to 2,000, hard cap |
| Response | up to 400 (`max_tokens`) |

At one call every 8 minutes that is about 25,000 tokens an hour. A small local model with an 8,000-token window works.

### The digest, section by section (priority order, trimmed from the bottom)

1. **ROUND** (about 40 tokens): minutes since start, players, deaths so far, day/night, weather, faults awake, loot left as a percentage.
2. **SUBJECTS** (about 40 tokens each, top 8 by salience; the rest as one line, "12 others: quiet"): `S17 IVAN PETROV | alive 23m | lives 2 | errors 1 | SECTOR C4 | with S4,S9 | last: killed S4 T-3m | spoke 14 | last words "..."`. Salience is recency times severity times novelty. Numbers are stable for the round.
3. **PRAYERS AND MENTIONS** (about 5 lines): verbatim, always included first when present.
4. **EVENTS** since the last call (up to 20 lines, about 25 tokens each): deaths, kills, big fights, faults triggered, arrivals, ranked by severity, repeated events merged ("S4 hurt S9 x6, 140 damage"). Chat is summarised into counts and quotes, never dumped.
5. **HER RECENT** (last 6 actions: type, target, T-minus, result): memory of what she did, so she does not repeat herself.
6. **STATE** (about 60 tokens): wrath budget, cooldowns of big acts, current mood.
7. **NOTES** (up to 300 characters): what she wrote last time.

Rules for the digest builder: deterministic, no free text from players except the capped quotes above, character names only (never account names), and the exact digest of every call is saved so `godlog` can show it. Tokens are counted by length, with the caps above enforced before sending. If the digest is empty (nothing happened, no prayers) the call is skipped.

### Output schema

```json
{
  "notes": "short text replacing the previous notes",
  "actions": [
    { "type": "subtle", "targets": ["S17"], "text": "..." },
    { "type": "announce", "text": "..." },
    { "type": "glitch", "target": "S17", "kind": "static" }
  ]
}
```

At most 3 actions per call. Anything else (extra fields, unknown types, bad targets) is dropped.

### Cadence

- **Scheduled call** every 8 minutes by default (valve).
- **Early call** when the salience spikes (a prayer, 3 or more deaths in a minute, a global event), but never sooner than 90 seconds after the last one.
- **Between calls** the scripted director reacts to deaths, kills and arrivals with lines from the line bank. Reaction time never depends on the API.

The log is informative enough for this: the digest is built from structured data and ranked, not pasted, so a 90-minute round with many players still fits under the cap, and the notes carry the long thread.

## Actions

Each action has a schema, valid targets, a point cost, a cooldown and a per-subject cap.

| Step | Action | Cost |
|---|---|---|
| 1 | subtle message (one or a group) | low |
| 1 | global announcement | high |
| 1 | glitch burst (static, flicker, sound) around a subject or sector | low |
| 2 | wake a fault pack or add a lone fault near a subject or in a sector | medium |
| 2 | horde spawn near a subject (existing mob spawners) | high |
| 2 | weather change | high |
| 2 | loot cache or airdrop in a sector | medium |
| 3 | give or take an item (tiered) | medium |
| 3 | complications: bleed, slow, blind, hunger and thirst drain, hallucination, mutation | medium |
| 3 | blessings: heal, stamina, warmth | medium |
| 3 | damage, clamped so it can never reach death | medium |

**No instant kill:** direct damage is clamped to leave the target above the death threshold. Over-time effects are allowed, a subject can die of them, but never by one action.

## Valves

All CVars (server config) with matching admin commands. Defaults are loose where the design says "chaos is the point".

| Group | Valve |
|---|---|
| Mode | `off`, `scripted`, `llm-assisted`, `llm-full`; `dry-run` (decide and log, do nothing); `approval` (an admin confirms each action, unanswered ones expire as denied after 120 s). First playtests: `approval`. |
| Tempo | seconds between calls, calls per round, quiet period at round start, minimum players, events per digest. |
| Budget | wrath points that accrue per minute and are spent by actions. Big acts need saved points and are capped per hour; per-target cooldown and cap. |
| Strength | multiplier per category (counts, damage, item tier) and per-action enable. |
| Protection | spawn grace (no harm within N seconds of a subject spawning), no body effects in the death void, the no-instant-kill clamp. |
| Mood | weights for test subject, worshipper, heretic, error to delete. It drifts at random and is written into the digest. |
| Safety | text length caps (subtle 140, announce 200), markup stripped, character set, banned words, a check that rejects text that reads as an order or hint, JSON schema checks. |
| Privacy | whether chat is sent to the model, how many lines, character name or subject number. |

## Admin tools

- `godstatus`: mode, budget, mood, cooldowns, last decisions.
- `godlog`: for each call the digest sent, the reply, and each action as executed, rejected (and why) or pending approval.
- `godapprove <id>` and `goddeny <id>`.
- `godpause`, `godresume`, `godforce <action> ...` (admin-triggered action through the same validator).
- Every executed action goes into the admin log.

## Testing

- A **fake LLM** returns scripted JSON, so the whole loop runs without a network.
- Unit tests for the digest builder (caps, ordering, merging, no account names), the validator, the budget and every valve.
- Replay tests: a saved digest and reply produce the expected actions.
- Playtest checklist per step in the steps below.

## Steps (each is playtested before the next starts)

### Step 1: the voice

Ledger, sectors, digest, prayer verb, mention filter, scripted director, LLM director, writer mode, subtle message, global announcement, glitch burst, approval mode, admin tools.
**Playtest:** she watches and speaks. Do the lines read cold and erratic, never like orders? Is the cadence right? Is the digest informative? Are approvals easy for an admin to run?

### Step 2: the world

Fault wakes and lone faults, hordes, weather, loot caches, with budgets.
**Playtest:** does she make the world feel watched without making it unfair? Are the budgets right?

### Step 3: bodies and items

Gifts and takes, complications, blessings, clamped damage.
**Playtest:** does she complicate lives in ways that read as hers and not as a griefing admin?

## Open

- Exact form of prayer (a self verb is proposed; an altar later).
- Sector naming (a letter and number grid over the map's coarse grid is proposed).
- Default values for the budget and cooldowns, to be tuned in the step 1 and 2 playtests.
- The mood list and how strongly it drives the text.
- Whether the model sees raw prayers or only a summary.
