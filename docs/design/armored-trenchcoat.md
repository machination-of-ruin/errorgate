# >>> ARMORED TRENCHCOAT <<<

**Status: idea, drafted for approval. Not built.** A craft that turns a trenchcoat (`ClothingOuterCoatTrench`, a Tier 1-2 Living / Military loot item) into an armored version. A worked example of the cost-per-upgrade rule in [progression.md](progression.md): protection you pay for, with a price in weight and tiredness.

## What exists

- `ClothingOuterCoatTrench` in the loot (Living / Military, Tier 1-2).
- The crafting menu and construction graphs (diegetic crafting, `Resources/Prototypes/Recipes/Construction`). The crafting cull removed most station recipes, so a new recipe is cheap to add; it needs a graph and a `construction` entry, not a loot table.
- Armor values live in the `Armor` component of the coat; speed in `ClothingSpeedModifier`; stamina cost in the clothing's `StaminaDrain`-style modifiers if any (to check).

## Proposal

| Part | Proposal |
|---|---|
| **Name** | `ClothingOuterCoatTrenchArmored`, a child of the trenchcoat, with a suffix `Armored` in the spawn menu. |
| **Ingredients** | The trenchcoat, scavenged plates (2 sheets of steel or 3 scrap plates), a roll of cord or wire, 1 sewing item or tool. |
| **Time** | A do-after of about 8 seconds, so it is not done in a fight. |
| **Protection** | About one third more than the trenchcoat on Blunt, Slash and Piercing; small on Heat; nothing on Radiation. It stays below a flak vest (Tier 2) so the vest is still worth finding. |
| **Cost** | Heavier: sprint and walk slowed a little (like the large outer clothing, about 0.95), and it drains stamina a bit faster. Armor against bullets stays weak, it is for melee and shrapnel. |
| **Look** | The trenchcoat sprite with plates; new art is needed. Until then the same sprite with a suffix. |
| **Where it sits in loot** | It is not in the loot, only craftable. |

## Open questions

- Ingredients: steel sheets or a rarer scrap, and is the sewing item needed?
- Protection numbers against the other Tier 1-2 armor (see [combat.md](combat.md) for the damage values).
- Slowdown only, or tiredness too?
- Sprite: does someone draw it, or does it ship with the plain coat sprite for now?
- Name and description: player-facing, to be drafted and approved.

## If approved, the work

A construction graph and recipe, the child prototype with the numbers above, a test that crafting yields the coat and consumes the ingredients, and a loading tip (to be approved).
