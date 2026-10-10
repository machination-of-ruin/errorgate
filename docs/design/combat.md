# >>> COMBAT <<<

## Focus

1. **Lethality.** Fewer hits to kill, bleeding, limb damage. Shitmed is already in the repo.
2. **Firearms depth.** Ammo scarcity, recoil, jamming, attachments.
3. **Melee depth.** Stamina, blocking, weapon reach and weight.
4. **PvE enemies.** Smarter, more dangerous mobs and creatures.

Combat logs were already improved and extended (port #6).

## Current state

- Guns: SMGs and shotguns are two-handed, a wide crosshair means bad accuracy (stay still to aim well), guns spawn with the bolt closed and no round chambered.
- Distant gunfire: shots beyond view are heard out to the PVS range, directional, quieter and lower, with a reverb tail and a per-caliber sound.
- Melee and movement: right-click with an empty hand or a melee weapon shoves, prying by hand takes 1 second, running drains stamina and being tired slows you down.
- Look far on Space aims and sees up to 12 tiles ahead.
- Combat log: uniform small font, colors per side, unseen attackers are shown as "Something". Pointing is an interaction without an arrow.
- Gibbing: the torso is never gibbed and gibbing leaves no limbs or organs.

## Open

- Which existing fork combat systems to build on (Goobstation, Shitmed, White, etc.).
- Concrete numbers: time-to-kill targets, armor behaviour.
- PvE enemy roster and spawning (fixed nests, roaming, AI GOD-driven).
