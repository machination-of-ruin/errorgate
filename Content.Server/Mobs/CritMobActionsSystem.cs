using Content.Server.Administration;
using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Server.Speech.Muting;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.Console;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Content.Shared.Speech.Muting;

namespace Content.Server.Mobs;

/// <summary>
///     Handles performing crit-specific actions.
/// </summary>
public sealed class CritMobActionsSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!; // ERRORGATE
    [Dependency] private readonly DeathgaspSystem _deathgasp = default!;
    [Dependency] private readonly IServerConsoleHost _host = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly QuickDialogSystem _quickDialog = default!;
    [Dependency] private readonly MobThresholdSystem _thresholds = default!; // ERRORGATE
    [Dependency] private readonly DamageableSystem _damageable = default!; // ERRORGATE

    private const int MaxLastWordsLength = 30;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MobStateActionsComponent, CritSuccumbEvent>(OnSuccumb);
        SubscribeLocalEvent<MobStateActionsComponent, CritFakeDeathEvent>(OnFakeDeath);
        SubscribeLocalEvent<MobStateActionsComponent, CritLastWordsEvent>(OnLastWords);
    }

    private void OnSuccumb(EntityUid uid, MobStateActionsComponent component, CritSuccumbEvent args)
    {
        if (!TryComp<ActorComponent>(uid, out var actor) || !_mobState.IsCritical(uid))
            return;

        // ERRORGATE: there are no ghosts, succumbing is dying
        Die(uid);
        args.Handled = true;
    }

    private void OnFakeDeath(EntityUid uid, MobStateActionsComponent component, CritFakeDeathEvent args)
    {
        if (!_mobState.IsCritical(uid))
            return;

        if (HasComp<MutedComponent>(uid))
        {
            _popupSystem.PopupEntity(Loc.GetString("fake-death-muted"), uid, uid);
            return;
        }

        args.Handled = _deathgasp.Deathgasp(uid);
    }

    private void OnLastWords(EntityUid uid, MobStateActionsComponent component, CritLastWordsEvent args)
    {
        if (!TryComp<ActorComponent>(uid, out var actor))
            return;

        _quickDialog.OpenDialog(actor.PlayerSession, Loc.GetString("action-name-crit-last-words"), "",
            (string lastWords) =>
            {
                // Intentionally does not check for muteness
                if (actor.PlayerSession.AttachedEntity != uid
                    || !_mobState.IsCritical(uid))
                    return;

                if (lastWords.Length > MaxLastWordsLength)
                {
                    lastWords = lastWords.Substring(0, MaxLastWordsLength);
                }
                lastWords += "...";

                _chat.TrySendInGameICMessage(uid, lastWords, InGameICChatType.Whisper, ChatTransmitRange.Normal, checkRadioPrefix: false, ignoreActionBlocker: true);
                // ERRORGATE: there are no ghosts, the last words end in death
                Die(uid);
            });

        args.Handled = true;
    }

    /// <summary>
    ///     ERRORGATE: dies for real. Forcing the mob state to dead is not enough: the next damage tick (burning, no air)
    ///     works the state out from the damage again and puts a character who is not past the death threshold back in
    ///     crit, and the death void lets them go. So the character takes the damage that is missing to the threshold.
    /// </summary>
    private void Die(EntityUid uid)
    {
        if (_thresholds.TryGetThresholdForState(uid, MobState.Dead, out var dead)
            && TryComp<DamageableComponent>(uid, out var damageable))
        {
            for (var i = 0; i < 3 && !_mobState.IsDead(uid); i++)
            {
                var missing = dead.Value - damageable.TotalDamage + 1;
                if (missing <= 0)
                    break;

                var damage = new DamageSpecifier(_proto.Index<DamageTypePrototype>("Bloodloss"), missing);
                _damageable.TryChangeDamage(uid, damage, true, false, damageable, origin: uid, canSever: false, doPartDamage: false);
            }
        }

        if (!_mobState.IsDead(uid))
            _mobState.ChangeMobState(uid, MobState.Dead);
    }
}
