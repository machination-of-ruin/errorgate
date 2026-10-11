using Content.Shared._ERRORGATE.Bunker;
using Content.Shared.Access.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Map;

namespace Content.Server._ERRORGATE.Bunker;

/// <summary>
///     The surface hatch, the hatch up and the console inside the vestibule. The hatches move people between the surface map and the
///     bunker map while they are unlocked, the vestibule system decides when. Nothing here says anything, there are only sounds.
/// </summary>
public sealed class BunkerHatchSystem : EntitySystem
{
    private static readonly SoundSpecifier DenySound = new SoundPathSpecifier("/Audio/Machines/custom_deny.ogg");
    private static readonly SoundSpecifier ClimbSound = new SoundPathSpecifier("/Audio/Machines/blastdoor.ogg");
    private static readonly SoundSpecifier ConsoleSound = new SoundPathSpecifier("/Audio/Machines/button.ogg");

    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly BunkerVestibuleSystem _vestibule = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BunkerHatchComponent, ActivateInWorldEvent>(OnHatchActivate);
        SubscribeLocalEvent<BunkerHatchComponent, InteractUsingEvent>(OnHatchInteractUsing);
        SubscribeLocalEvent<BunkerLadderComponent, ActivateInWorldEvent>(OnLadderActivate);
        SubscribeLocalEvent<BunkerConsoleComponent, ActivateInWorldEvent>(OnConsoleActivate);
    }

    private void OnHatchActivate(Entity<BunkerHatchComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        UseHatch(ent, args.User);
    }

    private void OnHatchInteractUsing(Entity<BunkerHatchComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        UseHatch(ent, args.User);
    }

    private void UseHatch(Entity<BunkerHatchComponent> ent, EntityUid user)
    {
        // Unlocked: down it goes
        if (!ent.Comp.Locked)
        {
            if (ent.Comp.Arrival is not { } arrival)
                return;

            _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(user):user} went down the bunker hatch");
            Teleport(user, arrival);
            return;
        }

        // Locked: the key on the user starts the sequence, if it can start just now
        if (ent.Comp.Vestibule is { } vestibule && _access.IsAllowed(user, ent) && _vestibule.OnKey(vestibule))
        {
            _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(user):user} used a key on the bunker hatch");
            Say(ent, Loc.GetString("bunker-entrance-key", ("seconds", SirenSeconds())));
            return;
        }

        _audio.PlayPvs(DenySound, ent);
        _popup.PopupEntity(Loc.GetString("bunker-hatch-locked"), ent, user);
    }

    private void OnLadderActivate(Entity<BunkerLadderComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;

        if (ent.Comp.Locked || ent.Comp.Surface is not { } surface)
        {
            _audio.PlayPvs(DenySound, ent);
            _popup.PopupEntity(Loc.GetString("bunker-exit-hatch-locked"), ent, args.User);
            return;
        }

        _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(args.User):user} came up the bunker hatch");
        Teleport(args.User, surface);
    }

    private void OnConsoleActivate(Entity<BunkerConsoleComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;

        if (ent.Comp.Vestibule is { } vestibule && _vestibule.OnConsole(vestibule))
        {
            _audio.PlayPvs(ConsoleSound, ent);
            _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(args.User):user} pressed the bunker console");

            // Everyone around sees where the cycle leads
            var toBunker = Comp<BunkerVestibuleComponent>(vestibule).SirenTarget == BunkerVestibuleState.InnerOpen;
            Say(ent, Loc.GetString(toBunker ? "bunker-console-to-bunker" : "bunker-console-to-surface", ("seconds", SirenSeconds())));
            return;
        }

        _audio.PlayPvs(DenySound, ent);
        Say(ent, Loc.GetString("bunker-console-busy"));
    }

    private int SirenSeconds() => (int) MathF.Round(_cfg.GetCVar(BunkerCVars.CycleSeconds));

    /// <summary>A popup everyone around sees.</summary>
    private void Say(EntityUid source, string message)
    {
        _popup.PopupEntity(message, source, Filter.Pvs(source, entityManager: EntityManager), true, PopupType.Medium);
    }

    /// <summary>
    ///     Moves a person and whatever they pull. The pull is taken up again on the other side.
    /// </summary>
    private void Teleport(EntityUid user, EntityCoordinates to)
    {
        EntityUid? pulled = null;
        if (_pulling.TryGetPulledEntity(user, out var pulling) && TryComp<PullableComponent>(pulling, out var pullable))
        {
            pulled = pulling;
            _pulling.TryStopPull(pulling.Value, pullable, user);
        }

        _audio.PlayPvs(ClimbSound, user);
        _transform.SetCoordinates(user, to);
        _transform.AttachToGridOrMap(user);

        if (pulled is { } other)
        {
            _transform.SetCoordinates(other, to);
            _transform.AttachToGridOrMap(other);
            _pulling.TryStartPull(user, other);
        }

        _audio.PlayPvs(ClimbSound, user);
    }
}
