using Content.Server.Chat.Systems;
using Content.Server.CombatMode.Disarm;
using Content.Server.Movement.Systems;
using Content.Shared.Actions.Events;
using Content.Shared.Administration.Components;
using Content.Shared.CombatMode;
using Content.Shared.Contests;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Effects;
using Content.Shared.Hands.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Speech.Components;
using Content.Shared.StatusEffect;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using System.Linq;
using System.Numerics;
using Content.Shared.Chat;
using Content.Shared.Coordinates;
using Content.Shared.Examine;
using Content.Shared.Item;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems; // WD EDIT

namespace Content.Server.Weapons.Melee;

public sealed class MeleeWeaponSystem : SharedMeleeWeaponSystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly DamageExamineSystem _damageExamine = default!;
    [Dependency] private readonly LagCompensationSystem _lag = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedColorFlashEffectSystem _color = default!;
    [Dependency] private readonly ContestsSystem _contests = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly SharedFlipAnimationEffectSystem _flipAnimation = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MeleeSpeechComponent, MeleeHitEvent>(OnSpeechHit);
        SubscribeLocalEvent<MeleeWeaponComponent, ExaminedEvent>(OnExamine);
    }

    private void OnExamine(EntityUid uid, MeleeWeaponComponent component, ref ExaminedEvent args)
    {
        if (component.Hidden || !args.IsInDetailsRange)
            return;

        var damageSpec = GetDamage(uid, args.Examined, component);

        if (damageSpec.Empty)
            return;

        // ERRORGATE NO EXAMINE VERBS, WE JUST PUSH SHIT STRAIGHT INTO THE EXAMINE WINDOW
        args.PushMessage(_damageExamine.AddDamageExamine(damageSpec, Loc.GetString("damage-melee")), -3);

        //if (damageSpec * component.HeavyDamageBaseModifier != damageSpec && !TryComp<GunComponent>(uid, out var gun)) // ERRORGATE NO MORE HEAVY ATTACK
        //    _damageExamine.AddDamageExamine(args.Message, damageSpec * component.HeavyDamageBaseModifier, Loc.GetString("damage-melee-heavy"));
    }

    protected override bool ArcRaySuccessful(EntityUid targetUid, Vector2 position, Angle angle, Angle arcWidth, float range, MapId mapId,
        EntityUid ignore, ICommonSession? session)
    {
        // Originally the client didn't predict damage effects so you'd intuit some level of how far
        // in the future you'd need to predict, but then there was a lot of complaining like "why would you add artifical delay" as if ping is a choice.
        // Now damage effects are predicted but for wide attacks it differs significantly from client and server so your game could be lying to you on hits.
        // This isn't fair in the slightest because it makes ping a huge advantage and this would be a hidden system.
        // Now the client tells us what they hit and we validate if it's plausible.

        // Even if the client is sending entities they shouldn't be able to hit:
        // A) Wide-damage is split anyway
        // B) We run the same validation we do for click attacks.

        // Could also check the arc though future effort + if they're aimbotting it's not really going to make a difference.

        // (This runs lagcomp internally and is what clickattacks use)
        if (!Interaction.InRangeUnobstructed(ignore, targetUid, range + 0.1f))
            return false;

        // TODO: Check arc though due to the aforementioned aimbot + damage split comments it's less important.
        return true;
    }

    protected override bool DoShove(EntityUid user, ShoveAttackEvent ev, EntityUid meleeUid, MeleeWeaponComponent component, ICommonSession? session)
    {
        if (!base.DoShove(user, ev, meleeUid, component, session)) // Checks and attempt animation
            return false;

        var target = GetEntity(ev.Target!.Value);
        var attacker = GetEntity(ev.Attacker!.Value);

        Interaction.DoContactInteraction(user, target);

        ApplyShovePhysics(target, attacker, component); // Before the status check so you can shove inanimate objects too
            //_flipAnimation.AnimateFlip(target); // Funny flip animation (was under if applyshove physics)

        var attemptEvent = new ShoveAttemptEvent(target, user); // nothing uses this afaik
        RaiseLocalEvent(target, attemptEvent);

        if (attemptEvent.Cancelled)
            return false;

        /* ERRORGATE LESS POPUPS
        var filterOther = Filter.PvsExcept(user, entityManager: EntityManager);
        var msgPrefix = "disarm-action-shove-";

        var msgOther = Loc.GetString(
                msgPrefix + "popup-message-other-clients",
                ("performerName", Identity.Entity(user, EntityManager)),
                ("targetName", Identity.Entity(target, EntityManager)));

        var msgUser = Loc.GetString(msgPrefix + "popup-message-cursor", ("targetName", Identity.Entity(target, EntityManager)));

        PopupSystem.PopupEntity(msgOther, user, filterOther, true);
        PopupSystem.PopupEntity(msgUser, target, user);
        */

        //_audio.PlayPvs(combatMode.DisarmSuccessSound, user, AudioParams.Default.WithVariation(0.025f).WithVolume(5f));
        AdminLogger.Add(LogType.DisarmedAction, $"{ToPrettyString(user):user} tried to shove {ToPrettyString(target):target}");

        var eventArgs = new ShoveEvent { Target = target, Source = user, StaminaDamage = component.ShoveStaminaDamage, StaminaCost = component.ShoveStaminaCost};
        RaiseLocalEvent(target, eventArgs);

        if (!eventArgs.Handled)
            return false;

        return true;
    }

    private bool ApplyShovePhysics(EntityUid target, EntityUid attacker, MeleeWeaponComponent component)
    {
        // Return false if couldn't push
        if (!TryComp<PhysicsComponent>(target, out var p))
            return false;

        if (p.BodyType != BodyType.Dynamic && p.BodyType != BodyType.KinematicController)
            return false;

        if (!TryComp<PhysicsComponent>(attacker, out var physics)) // Cant push if you have no physics yourself?
            return false;

        const float forcemultiplier = 100f;
        const float throwrangemultiplier = 3f;
        const float throwforcemultiplier = 12f;

        var attackermass = physics.Mass;

        var force = attackermass * forcemultiplier * component.ShoveForceMultiplier;

        var userPos = attacker.ToCoordinates().ToMapPos(EntityManager, TransformSystem);
        var targetPos = target.ToCoordinates().ToMapPos(EntityManager, TransformSystem);
        var pushVector = (targetPos - userPos).Normalized() * force;

        if (HasComp<ItemComponent>(target)) // If its an item, throw it instead.
        {
            _throwing.TryThrow(target, pushVector.Normalized() * throwrangemultiplier,
                throwforcemultiplier * component.ShoveForceMultiplier, attacker, 1f, false);
            return false; // Return false since the spin is handled by the TryThrow.
        }

        _physics.ApplyLinearImpulse(target, pushVector);
        return true;
    }

    protected override bool DoDisarm(EntityUid user, DisarmAttackEvent ev, EntityUid meleeUid, MeleeWeaponComponent component, ICommonSession? session)
    {
        if (!base.DoDisarm(user, ev, meleeUid, component, session))
            return false;

        if (!TryComp<CombatModeComponent>(user, out var combatMode) ||
            combatMode.CanDisarm != true)
        {
            return false;
        }

        var target = GetEntity(ev.Target!.Value);

        if (_mobState.IsIncapacitated(target))
        {
            return false;
        }

        if (!TryComp<HandsComponent>(target, out var targetHandsComponent))
        {
            if (!TryComp<StatusEffectsComponent>(target, out var status) || !status.AllowedEffects.Contains("KnockedDown"))
                return false;
        }

        if (!InRange(user, target, component.Range, session))
        {
            return false;
        }

        EntityUid? inTargetHand = null;

        if (targetHandsComponent?.ActiveHand is { IsEmpty: false })
        {
            inTargetHand = targetHandsComponent.ActiveHand.HeldEntity!.Value;
        }

        Interaction.DoContactInteraction(user, target);

        var attemptEvent = new DisarmAttemptEvent(target, user, inTargetHand);

        if (inTargetHand != null)
        {
            RaiseLocalEvent(inTargetHand.Value, attemptEvent);
        }

        RaiseLocalEvent(target, attemptEvent);

        if (attemptEvent.Cancelled)
            return false;

        var chance = CalculateDisarmChance(user, target, inTargetHand, combatMode);

        if (_random.Prob(chance))
        {
            // Don't play a sound as the swing is already predicted.
            // Also don't play popups because most disarms will miss.
            return false;
        }

        var filterOther = Filter.PvsExcept(user, entityManager: EntityManager);
        var msgPrefix = "disarm-action-";

        if (inTargetHand == null)
            msgPrefix = "disarm-action-shove-";

        var msgOther = Loc.GetString(
                msgPrefix + "popup-message-other-clients",
                ("performerName", Identity.Entity(user, EntityManager)),
                ("targetName", Identity.Entity(target, EntityManager)));

        var msgUser = Loc.GetString(msgPrefix + "popup-message-cursor", ("targetName", Identity.Entity(target, EntityManager)));

        PopupSystem.PopupEntity(msgOther, user, filterOther, true);
        PopupSystem.PopupEntity(msgUser, target, user);

        _audio.PlayPvs(combatMode.DisarmSuccessSound, user, AudioParams.Default.WithVariation(0.025f).WithVolume(5f));
        AdminLogger.Add(LogType.DisarmedAction, $"{ToPrettyString(user):user} used disarm on {ToPrettyString(target):target}");

        var eventArgs = new DisarmedEvent { Target = target, Source = user, PushProbability = 1 };
        RaiseLocalEvent(target, eventArgs);

        if (!eventArgs.Handled)
            return false;

        _audio.PlayPvs(combatMode.DisarmSuccessSound, user, AudioParams.Default.WithVariation(0.025f).WithVolume(5f));
        AdminLogger.Add(LogType.DisarmedAction, $"{ToPrettyString(user):user} used disarm on {ToPrettyString(target):target}");

        return true;
    }

    protected override bool InRange(EntityUid user, EntityUid target, float range, ICommonSession? session)
    {
        EntityCoordinates targetCoordinates;
        Angle targetLocalAngle;

        if (session is { } pSession)
        {
            (targetCoordinates, targetLocalAngle) = _lag.GetCoordinatesAngle(target, pSession);
        }
        else
        {
            var xform = Transform(target);
            targetCoordinates = xform.Coordinates;
            targetLocalAngle = xform.LocalRotation;
        }

        return Interaction.InRangeUnobstructed(user, target, targetCoordinates, targetLocalAngle, range);
    }

    protected override void DoDamageEffect(List<EntityUid> targets, EntityUid? user, TransformComponent targetXform)
    {
        var filter = Filter.Pvs(targetXform.Coordinates, entityMan: EntityManager).RemoveWhereAttachedEntity(o => o == user);
        _color.RaiseEffect(Color.Red, targets, filter);
    }

    private float CalculateDisarmChance(EntityUid disarmer, EntityUid disarmed, EntityUid? inTargetHand, CombatModeComponent disarmerComp)
    {
        return 1.0f;
        /*
        if (HasComp<DisarmProneComponent>(disarmer))
            return 1.0f;

        if (HasComp<DisarmProneComponent>(disarmed))
            return 0.0f;

        var chance = disarmerComp.BaseDisarmFailChance;

        if (inTargetHand != null && TryComp<DisarmMalusComponent>(inTargetHand, out var malus))
        {
            chance += malus.Malus;
        }

        var generated = Math.Clamp(chance
                        * _contests.MassContest(disarmer, disarmed, false, 0.5f)
                        * _contests.StaminaContest(disarmer, disarmed, false, 0.5f)
                        * _contests.HealthContest(disarmer, disarmed, false, 0.5f),
                        0f, 1f);

        Log.Debug($"Disarm chance was {(int)(generated * 10)}%");
        return generated;*/
    }

    public override void DoLunge(EntityUid user, EntityUid weapon, Angle angle, Vector2 localPos, string? animation, bool predicted = true)
    {
        Filter filter;

        if (predicted)
        {
            filter = Filter.PvsExcept(user, entityManager: EntityManager);
        }
        else
        {
            filter = Filter.Pvs(user, entityManager: EntityManager);
        }

        RaiseNetworkEvent(new MeleeLungeEvent(GetNetEntity(user), GetNetEntity(weapon), angle, localPos, animation), filter);
    }

    private void OnSpeechHit(EntityUid owner, MeleeSpeechComponent comp, MeleeHitEvent args)
    {
        if (!args.IsHit ||
        !args.HitEntities.Any())
        {
            return;
        }

        if (comp.Battlecry != null)//If the battlecry is set to empty, doesn't speak
        {
            _chat.TrySendInGameICMessage(args.User, comp.Battlecry, InGameICChatType.Speak, true, true, checkRadioPrefix: false);  //Speech that isn't sent to chat or adminlogs
        }

    }
}
