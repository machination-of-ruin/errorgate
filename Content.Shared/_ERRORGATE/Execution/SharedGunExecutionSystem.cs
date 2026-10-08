using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Execution;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Player;

namespace Content.Shared._ERRORGATE.Execution;

/// <summary>
///     Executing someone with a gun: the verb and the do-after. The server fires the round when the do-after ends,
///     see <c>GunExecutionSystem</c>.
/// </summary>
/// <remarks>
///     WWDP's execution system only knows about melee, so a gun with an <see cref="ExecutionComponent"/> used to pistol
///     whip the victim. Guns are handled here instead and <see cref="SharedExecutionSystem"/> ignores them.
/// </remarks>
public sealed class SharedGunExecutionSystem : EntitySystem
{
    /// <summary>
    ///     How long it takes to press the gun against someone and pull the trigger.
    /// </summary>
    public const float GunExecutionDuration = 5f;

    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedExecutionSystem _execution = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunComponent, GetVerbsEvent<UtilityVerb>>(OnGetVerbs);
    }

    private void OnGetVerbs(Entity<GunComponent> gun, ref GetVerbsEvent<UtilityVerb> args)
    {
        if (args.Hands == null || args.Using != gun.Owner || !args.CanAccess || !args.CanInteract)
            return;

        var attacker = args.User;
        var victim = args.Target;

        if (!_execution.CanBeExecuted(victim, attacker, gun))
            return;

        args.Verbs.Add(new UtilityVerb
        {
            Act = () => TryStart(gun, victim, attacker),
            Impact = LogImpact.High,
            Text = Loc.GetString("execution-verb-name"),
            Message = Loc.GetString("execution-verb-message"),
        });
    }

    private void TryStart(EntityUid gun, EntityUid victim, EntityUid attacker)
    {
        if (!_execution.CanBeExecuted(victim, attacker, gun))
            return;

        var self = attacker == victim;
        Popup(self ? "execution-popup-gun-self-initial-internal" : "execution-popup-gun-initial-internal", attacker, victim, gun, true);
        Popup(self ? "execution-popup-gun-self-initial-external" : "execution-popup-gun-initial-external", attacker, victim, gun, false);

        var doAfter = new DoAfterArgs(EntityManager, attacker, GunExecutionDuration, new ExecutionDoAfterEvent(), gun, target: victim, used: gun)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        _doAfter.TryStartDoAfter(doAfter);
    }

    private void Popup(string message, EntityUid attacker, EntityUid victim, EntityUid gun, bool internalMessage)
    {
        var text = Loc.GetString(message, ("attacker", attacker), ("victim", victim), ("weapon", gun));

        if (internalMessage)
            _popup.PopupClient(text, attacker, attacker, PopupType.MediumCaution);
        else
            _popup.PopupEntity(text, attacker, Filter.PvsExcept(attacker), true, PopupType.MediumCaution);
    }
}
