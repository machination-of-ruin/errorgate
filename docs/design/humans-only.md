# >>> HUMANS ONLY <<<

ERRORGATE is a **human-only** fork. Every survivor is a human.

## Lore

Survivors are human consciousnesses in bodies rebuilt by MACHINATION OF RUIN (see the premise in [VIBE.md](VIBE.md)). They look and play as plain humans. No cyborg appearance, parts or rules.

## What this means

- Only the `Human` species is selectable at roundstart (`roundStart: false` on every other species) and the random species weights contain only `Human`. See port #4, commit `affa868b99`.
- The other species prototypes, markings and sprites are **still in the repo on purpose**. Removing them is a big job with little gain right now. Do not delete them without being asked.
- Because there is no other body type, templates can be written for humans only. Example: the human inventory template's suit storage slot no longer requires outer clothing (port #26). Do not add per-species special cases to new ERRORGATE content, and do not spend effort keeping non-human variants working.
- New species-specific code (prototypes, locale, sprites) for non-human species is out of scope.

## Related

- [maps.md](maps.md): dev map and spawn flow.
- Job: a single `HumanError` role, every other job is hidden from character setup.
