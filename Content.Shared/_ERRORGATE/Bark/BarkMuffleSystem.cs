using Content.Shared._White.Bark;
using Content.Shared._White.Bark.Systems;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;

namespace Content.Shared._ERRORGATE.Bark;

/// <summary>
///     Lets worn masks with <see cref="BarkMuffleComponent"/> change the voice data the barks of the wearer are made from. The event
///     (<see cref="TransformSpeakerBarkEvent"/>) is relayed through the inventory of the speaker; the voice data is a shared object, so the
///     changed copy is put in the event instead of changing the original.
/// </summary>
public sealed class BarkMuffleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BarkMuffleComponent, InventoryRelayedEvent<TransformSpeakerBarkEvent>>(OnTransformSpeakerBark);
    }

    private void OnTransformSpeakerBark(Entity<BarkMuffleComponent> ent, ref InventoryRelayedEvent<TransformSpeakerBarkEvent> args)
    {
        // A mask pulled down does not muffle
        if (TryComp<MaskComponent>(ent, out var mask) && mask.IsToggled)
            return;

        var voice = args.Args.VoiceData;
        args.Args.VoiceData = new BarkVoiceData
        {
            BarkSound = voice.BarkSound,
            PauseAverage = voice.PauseAverage,
            PitchAverage = voice.PitchAverage + ent.Comp.PitchShift,
            PitchVariance = voice.PitchVariance,
            VolumeAverage = voice.VolumeAverage + ent.Comp.VolumeShift,
        };
    }
}
