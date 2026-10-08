using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Utility;
using System.Linq;
using Content.Shared.Traits.Assorted.Components;

namespace Content.Shared.HealthExaminable;

public abstract class SharedHealthExaminableSystem : EntitySystem
{
    [Dependency] private readonly ExamineSystemShared _examineSystem = default!;
    [Dependency] private readonly MobThresholdSystem _threshold = default!;

    public override void Initialize()
    {
        base.Initialize();

        // ERRORGATE: health shows inline in the examine window instead of behind a button
        SubscribeLocalEvent<HealthExaminableComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, HealthExaminableComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !TryComp<DamageableComponent>(uid, out var damage))
            return;

        args.PushMessage(GetMarkup(args.Examiner, (uid, component), damage), -5);
    }

    public FormattedMessage GetMarkup(
        EntityUid examiner,
        Entity<HealthExaminableComponent> examinable,
        DamageableComponent damageable
        ) =>
        examiner == examinable.Owner && TryComp<SelfAwareComponent>(examinable, out var selfAware)
            ? CreateMarkupSelfAware(examinable, selfAware, examinable.Comp, damageable)
            : CreateMarkup(examinable, examinable.Comp, damageable, examiner == examinable.Owner); // ERRORGATE: first person when examining yourself

    private FormattedMessage CreateMarkup(EntityUid uid, HealthExaminableComponent component, DamageableComponent damage, bool self = false)
    {
        var msg = new FormattedMessage();

        var first = true;

        var adjustedThresholds = GetAdjustedThresholds(uid, component.Thresholds);

        foreach (var type in component.ExaminableTypes)
        {
            if (!damage.Damage.DamageDict.TryGetValue(type, out var dmg))
                continue;

            if (dmg == FixedPoint2.Zero)
                continue;

            FixedPoint2 closest = FixedPoint2.Zero;

            string chosenLocStr = string.Empty;
            foreach (var threshold in adjustedThresholds)
            {
                var str = $"health-examinable-{component.LocPrefix}-{type}-{threshold}";
                var tempLocStr = Loc.GetString($"health-examinable-{component.LocPrefix}-{type}-{threshold}", ("target", Identity.Entity(uid, EntityManager)));

                // ERRORGATE: first person variant when examining yourself
                if (self)
                {
                    var selfStr = $"health-examinable-selfaware-{component.LocPrefix}-{type}-{threshold}";
                    var selfLocStr = Loc.GetString(selfStr);
                    if (selfLocStr != selfStr)
                        tempLocStr = selfLocStr;
                }

                // i.e., this string doesn't exist, because theres nothing for that threshold
                if (tempLocStr == str)
                    continue;

                if (dmg > threshold && threshold > closest)
                {
                    chosenLocStr = tempLocStr;
                    closest = threshold;
                }
            }

            if (closest == FixedPoint2.Zero)
                continue;

            if (!first)
                msg.PushNewline();
            else
                first = false;
            msg.AddMarkupOrThrow(chosenLocStr);
        }

        if (msg.IsEmpty)
        {
            // ERRORGATE: first person variant when examining yourself
            var noneKey = self ? $"health-examinable-selfaware-{component.LocPrefix}-none" : $"health-examinable-{component.LocPrefix}-none";
            var noneStr = Loc.GetString(noneKey);
            if (noneStr == noneKey)
                noneStr = Loc.GetString($"health-examinable-{component.LocPrefix}-none");
            msg.AddMarkupOrThrow(noneStr);
        }

        // Anything else want to add on to this?
        RaiseLocalEvent(uid, new HealthBeingExaminedEvent(msg, self), true);

        return msg;
    }

    private FormattedMessage CreateMarkupSelfAware(EntityUid target, SelfAwareComponent selfAware, HealthExaminableComponent component, DamageableComponent damage)
    {
        var msg = new FormattedMessage();

        var first = true;

        foreach (var type in selfAware.AnalyzableTypes)
        {
            if (!damage.Damage.DamageDict.TryGetValue(type, out var typeDmgUnrounded))
                continue;

            var typeDmg = (int) Math.Round(typeDmgUnrounded.Float(), 0);
            if (typeDmg <= 0)
                continue;

            var damageString = Loc.GetString(
                "health-examinable-selfaware-type-text",
                ("damageType", Loc.GetString($"health-examinable-selfaware-type-{type}")),
                ("amount", typeDmg)
            );

            if (!first)
                msg.PushNewline();
            else
                first = false;
            msg.AddMarkupOrThrow(damageString);
        }

        var adjustedThresholds = GetAdjustedThresholds(target, selfAware.Thresholds);

        foreach (var group in selfAware.DetectableGroups)
        {
            if (!damage.DamagePerGroup.TryGetValue(group, out var groupDmg)
                || groupDmg == FixedPoint2.Zero)
                continue;

            FixedPoint2 closest = FixedPoint2.Zero;

            string chosenLocStr = string.Empty;
            foreach (var threshold in adjustedThresholds)
            {
                var locName = $"health-examinable-selfaware-group-{group}-{threshold}";
                var locStr = Loc.GetString(locName);

                var locDoesNotExist = locStr == locName;
                if (locDoesNotExist)
                    continue;

                if (groupDmg > threshold && threshold > closest)
                {
                    chosenLocStr = locStr;
                    closest = threshold;
                }
            }

            if (closest == FixedPoint2.Zero)
                continue;

            if (!first)
                msg.PushNewline();
            else
                first = false;
            msg.AddMarkupOrThrow(chosenLocStr);
        }

        if (msg.IsEmpty)
            msg.AddMarkupOrThrow(Loc.GetString($"health-examinable-selfaware-none"));

        // Event listeners can know if the examination is Self-Aware.
        RaiseLocalEvent(target, new HealthBeingExaminedEvent(msg, true), true);

        return msg;
    }

    /// <summary>
    ///     Return thresholds as percentages of an entity's critical threshold.
    /// </summary>
    private List<FixedPoint2> GetAdjustedThresholds(EntityUid uid, List<FixedPoint2> thresholdPercentages)
    {
        FixedPoint2 critThreshold = 0;
        if (TryComp<MobThresholdsComponent>(uid, out var threshold))
            critThreshold = _threshold.GetThresholdForState(uid, Shared.Mobs.MobState.Critical, threshold);

        // Fallback to 100 crit threshold if none found
        if (critThreshold == 0)
            critThreshold = 100;

        return thresholdPercentages.Select(percentage => critThreshold * percentage).ToList();
    }
}

/// <summary>
///     A class raised on an entity whose health is being examined
///     in order to add special text that is not handled by the
///     damage thresholds.
/// </summary>
public sealed class HealthBeingExaminedEvent
{
    public FormattedMessage Message;
    public bool IsSelfAware;

    public HealthBeingExaminedEvent(FormattedMessage message, bool isSelfAware)
    {
        Message = message;
        IsSelfAware = isSelfAware;
    }
}
