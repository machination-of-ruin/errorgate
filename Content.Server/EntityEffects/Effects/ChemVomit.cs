using Content.Server.Medical;
using Content.Shared.EntityEffects;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server.EntityEffects.Effects
{
    /// <summary>
    /// Forces you to vomit.
    /// </summary>
    [UsedImplicitly]
    public sealed partial class ChemVomit : EntityEffect
    {
        /// How many units of thirst to add each time we vomit
        [DataField]
        public float ThirstAmount = -8f;
        /// How many units of hunger to add each time we vomit
        [DataField]
        public float HungerAmount = -8f;

        protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
            => Loc.GetString("reagent-effect-guidebook-chem-vomit", ("chance", Probability));

        public override void Effect(EntityEffectBaseArgs args)
        {
            // ERRORGATE: as in the old build, a partial metabolism scale does not cancel the vomit (breathing a thin gas
            // metabolises less than a full unit per tick and used to never make anyone sick)

            var vomitSys = args.EntityManager.EntitySysManager.GetEntitySystem<VomitSystem>();

            vomitSys.Vomit(args.TargetEntity, ThirstAmount, HungerAmount);
        }
    }
}
