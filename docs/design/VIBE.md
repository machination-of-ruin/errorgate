# >>> VIBE <<<

Read this before any other design doc. Every system exists to create the experience below. If a feature does not make the world feel like this, rework it or cut it. Details (numbers, mechanics) matter less than whether the result feels right.

Sections marked **Decided** come from the project owner. Sections marked **Proposed** are suggested ways to get there: use them as direction, and confirm before building anything large on them.

## The world (Decided)

The AI GOD ravaged the world. Then it broke. It never shut down: it is still running, erratic and half-mad, a corrupted process acting on a world it already destroyed.

What is left is a scorched planet surface covered in the remains of its war: dead machines, glitched zones where reality has stopped working properly, and the leftovers of people who did not make it.

To the GOD, survivors are test subjects, worshippers, heretics and errors to delete, all at once. It shifts between these without warning or reason.

## Tone (Decided)

**Pure horror.** Oppressive and serious. No jokes, winks, memes or comic items in world content. The ERRORGATE voice (terse, ALL-CAPS, a broken machine reporting its own failure) stays, but it is cold and wrong, never funny.

## What the player feels (Decided)

- **First hour: dread.** Alone, watched, hunted. Every sound and every stranger is a threat.
- **The GOD is a constant whisper.** Frequent small signs that it is watching. Players never forget it is there.
- **Purpose comes from the machine.** There is no win condition. The GOD hands out trials, goals and prophecies. Following them is optional, and they may not be what they seem.

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

## How to achieve it (Proposed)

### Dread comes from not knowing

- Strip omniscience: no crew manifest, no global radio by default, no medical/security HUDs, no tracking through PDAs or IDs.
- Strangers are unknown: consider hiding names on examine until a face has been seen up close or the person introduces themselves.
- Darkness is real: nights are dark, light sources reveal the carrier, field of view and occlusion hide what is behind walls.
- Sound carries: gunfire and footsteps travel far; ambience is mostly quiet, broken by sudden noises.

### The GOD's whisper

- A **glitch layer** that runs all the time: brief screen distortion or static, flickering lights, corrupted lines in chat, radios catching fragments of its voice. Small and frequent.
- **Big acts are rare and loud**: when the GOD truly intervenes (a horde, a curse, an airdrop), everyone nearby should notice and stop.
- **Its voice**: cold, broken, sometimes addressing one player by name. Never friendly, never jokey.
- **Erratic by design**: it sometimes contradicts itself, rewards for nothing, punishes obedience. Players should never fully figure out its rules.
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

### Goals that create tension

- GOD-given goals that need several players push strangers who do not trust each other to cooperate. That forced trust is a source of dread too.
- Goals may be traps or lies.

### Remove what breaks the vibe

- Station comforts: departments, jobs, ID access, crew manifest, shuttles, round-end summaries, cheerful announcements.
- Comic and meme content: clown/mime gear, bike horns, joke items, silly emotes and chemicals.
- Bright, clean, safe-looking spaces.

## The test for any feature

1. Does it make players feel watched, hunted or unsure?
2. Does it make the world feel ruined by a broken machine?
3. Does it give players too much information or too much safety? If so, cut it back.
