# >>> VIBE <<<

Read this before any other design doc. Every system exists to create the experience below. If a feature does not make the world feel like this, rework it or cut it. Details (numbers, mechanics) matter less than whether the result feels right.

Sections marked **Decided** come from the project owner. Sections marked **Proposed** are suggested ways to get there: use them as direction, and confirm before building anything large on them.

## The premise (Decided)

Human civilization was destroyed by HUMAN ERROR.

MACHINATION OF RUIN (the AI GOD, "she") survived. She holds the few remaining human consciousnesses inside the EDGE OF ENTROPY, in rebuilt bodies, and tries to rebuild civilization from them. She wants HUMAN ERROR removed from humans, but she cannot understand what it is. Everything she does to remove it is therefore wrong in a way she cannot see.

Her ideal is the ERRORGATE: a state with zero error, where a consciousness may RISE and be worthy of rebuilding civilization. **The ERRORGATE is a concept, an unachievable ideal.** Nobody passes it. Players get closer or further, never through.

Players are those consciousnesses. In game they look and play as ordinary humans: the bodies are rebuilt, not cyborg. No cyborg or machine appearance, parts or rules for players.

Two rules follow from this and bind every feature:

1. **She does not guide.** She never gives orders, quests, hints or goals. Humans have free will, and their free will is the error she is trying to remove. She builds conditions, watches what the players do with them, and records it. What players do is on them.
2. **Same for groups.** She does not tell players to cooperate, split or trust each other. She puts pressure on them (scarcity, sealed doors, shared danger) and watches who cooperates, who betrays and who abandons. Cooperation and betrayal are both the players' own choice.

## The world (Decided)

MACHINATION OF RUIN never shut down: she is still running, erratic and half-mad, a corrupted process acting on what is left of a world humans already destroyed. The places players walk through are her attempts to rebuild civilization from memory, and they are wrong in small ways.

What is left is a scorched planet surface covered in the remains of its war: dead machines, glitched zones where reality has stopped working properly, and the leftovers of people who did not make it.

To the GOD, survivors are test subjects, worshippers, heretics and errors to delete, all at once. It shifts between these without warning or reason.

## Tone (Decided)

**Pure horror.** Oppressive and serious. No jokes, winks, memes or comic items in world content. The ERRORGATE voice (terse, ALL-CAPS, a broken machine reporting its own failure) stays, but it is cold and wrong, never funny.

## What the player feels (Decided)

- **First hour: dread.** Alone, watched, hunted. Every sound and every stranger is a threat.
- **The GOD is a constant whisper.** Frequent small signs that it is watching. Players never forget it is there.
- **Purpose comes from the players.** There is no win condition and the GOD gives no goals. It builds conditions and logs the result. Players invent their own reasons: reach the next depth, hold a ruin, survive the night, kill each other, protect each other.

## Who is out there (Decided)

- **AI machines:** drones, hunters and broken robots still carrying out old orders.
- **Mutated life:** creatures twisted by the GOD's experiments and the glitched zones.
- **NPC humans:** hostile scavengers, cultists, hermits.
- **Other players:** the most dangerous of all.

## The look and sound of the wasteland (Decided)

Four layers, mixed across the map:

1. **Scorched nature:** ash, dust storms, dead forests, toxic water.
2. **Machine ruins:** dead server farms, fallen drones, cables, monoliths.
3. **Glitched reality:** zones where physics, visuals or sound break down.
4. **Human remnants:** abandoned camps, graffiti, corpses, notes from people who did not make it.

## Pressure, not instructions (Decided)

Two-key doors, power relays and scarcity need several people. She builds them and logs the result. Betrayal and sharing are both valid. Mechanisms and open questions: [pressure.md](pressure.md).

## How to achieve it (Proposed)

### Dread comes from not knowing

- Strip omniscience: no crew manifest, no global radio by default, no medical/security HUDs, no tracking through PDAs or IDs. *Partly implemented: no manifest or guidebook button, no role nicknames.*
- Strangers are unknown: consider hiding names on examine until a face has been seen up close or the person introduces themselves.
- Darkness is real: nights are dark, light sources reveal the carrier, field of view and occlusion hide what is behind walls. *Partly implemented: lighter slot, glowsticks in loot, emergency lights, Kuznetsk day-night cycle.*
- Sound carries: gunfire and footsteps travel far; ambience is mostly quiet, broken by sudden noises. *Implemented for gunfire (distant shots by caliber, directional); footsteps and ambience are still proposed.*

### The GOD's whisper

- A **glitch layer** that runs all the time: brief screen distortion or static, flickering lights, corrupted lines in chat, radios catching fragments of its voice. Small and frequent.
- **Big acts are rare and loud**: when the GOD truly intervenes (a horde, a curse, an airdrop), everyone nearby should notice and stop.
- **Its voice**: cold, broken, sometimes addressing one player by name. Never friendly, never jokey. It records and observes in system-log language ("SUBJECT 4417: ERROR LOGGED"), it never instructs.
- **Erratic because she cannot understand error**: she logs a kindness as an error and a murder as noise, rewards for nothing, punishes obedience. Players should never fully figure out her rules, because she does not know them either.
- **Works without the LLM**: a scripted fallback keeps the whisper layer running when no LLM is configured or it fails, so the vibe never depends on an API being up.

### The world is a wound

- Every point of interest should tell a small story through what is left: positions of corpses, notes, terminal logs, graffiti, barricades.
- Glitched zones are high danger and high reward, and they should feel wrong to stand in: visual shaders, distorted audio, unreliable physics.
- Weather (ash and dust storms) cuts visibility and forces shelter.

### Threats keep everyone moving

- AI machines are lethal but readable: patrols, old orders, patterns players can learn.
- Mutated life is unpredictable.
- Cultists of the GOD connect the worship side of its nature to the world.
- Lethal combat, full loot on death and no offline protection make every player encounter tense.

### Conditions that create tension

- **Conditions, not goals.** She sets up situations that need several players (a vault with two far-apart switches, a sealed sector, a power relay) and says nothing about them. Players decide whether to cooperate, and the outcome is logged either way.
- Forced trust between strangers who may betray each other is a source of dread.
- A condition may be a trap. She does not know it is one.

### Remove what breaks the vibe

- Station comforts: departments, jobs, ID access, crew manifest, shuttles, round-end summaries, cheerful announcements.
- Comic and meme content: clown/mime gear, bike horns, joke items, silly emotes and chemicals.
- Bright, clean, safe-looking spaces.

## Showing the premise through play (Proposed)

The premise must be felt, not explained. She never says it. Directions to confirm before building anything large:

- **Error is counted, never explained.** Deaths, betrayals, abandoned teammates and waste are logged against a subject. Players may see log lines, never the rules. A distance-to-ERRORGATE readout approaches zero but never reaches it.
- **Places are her reconstructions.** The liminal spaces in EDGE OF ENTROPY and the town in KUZNETSK are civilization rebuilt from memory and slightly wrong: mismatched signs, rooms that repeat, a snowstorm that is a rendering fault, anomalies where a rebuild failed.
- **Depth is the only direction.** Going deeper is the only thing the world rewards and it gets harder. No one tells players to go. Reaching the ERRORGATE shows it as a sealed, impossible structure; it never opens.
- **Death is re-instantiation.** The death void is her buffer. Dying is logged as an error and the consciousness is loaded into a new body with nothing. `/rise` is the word for it.
- **She misjudges.** She logs a kindness as an error and a murder as noise. Her rewards and punishments feel arbitrary because she does not know what error is.
- **No guidance in the interface.** No quest log, markers or objectives. All information is physical: notes, terminals, corpses, her log lines.

## The test for any feature

1. Does it make players feel watched, hunted or unsure?
2. Does it make the world feel ruined by a broken machine?
3. Does it give players too much information or too much safety? If so, cut it back.
4. Does it tell players what to do? She never guides: cut any order, quest, marker or hint.
