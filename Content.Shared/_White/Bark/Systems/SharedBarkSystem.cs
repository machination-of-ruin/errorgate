using System.Linq;
using Content.Shared._White.Bark.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._White.Bark.Systems;


public abstract class SharedBarkSystem : EntitySystem
{
    private static readonly char[] LongPauseChars = ['.', ',', '?', '!',];
    private static readonly char[] Vowels = ['a', 'e', 'i', 'o', 'u', 'y', 'а', 'е', 'ё', 'и', 'о', 'у', 'ы', 'э', 'ю', 'я',];
    private static readonly char[] SkipChars = [' ', '\n', '\r', '\t',];

    private static readonly char[] Soglasnoy =
        ['Б', 'В', 'Г', 'Д', 'Ж', 'З', 'Й', 'К', 'Л', 'М', 'Н', 'П', 'Р', 'С', 'Т', 'Ф', 'Х', 'Ц', 'Ч', 'Ш', 'Щ',];

    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ApplyBarkProtoComponent, ComponentInit>(OnApplyBarkInit);
    }

    private void OnApplyBarkInit(Entity<ApplyBarkProtoComponent> ent, ref ComponentInit args)
    {
        ApplyBark(ent.Owner, ent.Comp.VoiceProto, ent.Comp.PercentageApplyData);
        RemComp(ent.Owner, ent.Comp);
    }

    public List<BarkVoicePrototype> GetVoiceList(
        HumanoidCharacterProfile profile,
        JobPrototype? job = null,
        ProtoId<BarkListPrototype>? id = null
        )
    {
        var listRaw = _prototypeManager.Index(id ?? "default");
        var list = new List<BarkVoicePrototype>();
        foreach (var (voice, requirements) in listRaw.VoiceList)
        {
            if(!_prototypeManager.TryIndex(voice, out var prototype))
                continue;

            var isValid = true;

            foreach (var requirement in requirements)
            {
                if (requirement.IsValid(
                    job ?? default!,
                    profile,
                    new Dictionary<string, TimeSpan>(),
                    false,
                    prototype,
                    EntityManager,
                    _prototypeManager,
                    _cfg,
                    out var reason) == !requirement.Inverted)
                    continue;

                isValid = false;
                break;
            }

            if (!isValid)
                continue;

            list.Add(prototype);
        }

        return list;
    }

    public void ApplyBark(EntityUid uid, ProtoId<BarkVoicePrototype> protoId, BarkPercentageApplyData? data = null)
    {
        if (!_prototypeManager.TryIndex(protoId, out var prototype))
            return;

        ApplyBark(uid, prototype.BarkSound, prototype.ClampData, data);
    }

    public void ApplyBark(EntityUid uid, SoundSpecifier barkSound, BarkClampData clampData, BarkPercentageApplyData? data = null)
    {
        var voiceData = BarkVoiceData.WithClampingValue(
            barkSound,
            clampData,
            data ?? BarkPercentageApplyData.Default);

        var wasBarkComp = HasComp<BarkComponent>(uid);
        var comp = EnsureComp<BarkComponent>(uid);
        comp.VoiceData = voiceData;

        if (wasBarkComp)
            Dirty(uid, comp);
    }

    /// <summary>
    ///     One bark per syllable, not per letter. The queue holds only the barks that sound: the first vowel of each run of vowels
    ///     ("beautiful" is ea-u-i-u), or the first letter of a word without a vowel ("hmm", "brr", "TV"), and a silent pause for each
    ///     punctuation mark. Letters that do not sound and spaces take no time, so the barks keep the intervals they always had and
    ///     the text is simply faster.
    /// </summary>
    public List<BarkData> GenBarkData(BarkVoiceData data, string text, bool isWhisper) =>
        GenBarkPlan(data, text, isWhisper, out _);

    /// <summary>
    ///     The queue of <see cref="GenBarkData(BarkVoiceData, string, bool)"/> and, for each letter of the text, the index of the bark it
    ///     appears with: letters before a vowel with that vowel's bark, the letters after the last vowel of a word with the last bark of
    ///     the word, spaces with the bark before them. All -1 when nothing in the text sounds.
    /// </summary>
    public List<BarkData> GenBarkPlan(BarkVoiceData data, string text, bool isWhisper, out int[] charEntry)
    {
        var barks = new List<BarkData>();
        charEntry = new int[text.Length];
        var i = 0;
        while (i < text.Length)
        {
            if (!char.IsLetterOrDigit(text[i]))
            {
                // Punctuation is a silent pause, anything else (spaces) no time at all
                if (LongPauseChars.Contains(text[i]))
                    barks.Add(GenBarkData(data, text[i], isWhisper));

                charEntry[i] = barks.Count - 1;
                i++;
                continue;
            }

            var end = i;
            while (end < text.Length && char.IsLetterOrDigit(text[end]))
            {
                end++;
            }

            var starts = new List<int>();
            for (var j = i; j < end; j++)
            {
                if (IsVowel(text[j]) && (j == i || !IsVowel(text[j - 1])))
                    starts.Add(j);
            }

            if (starts.Count == 0)
                starts.Add(i);

            var first = barks.Count;
            foreach (var start in starts)
            {
                var bark = GenBarkData(data, text[start], isWhisper);
                bark.Enabled = true;
                barks.Add(bark);
            }

            var next = 0;
            for (var j = i; j < end; j++)
            {
                while (next < starts.Count && starts[next] < j)
                {
                    next++;
                }

                charEntry[j] = first + Math.Min(next, starts.Count - 1);
            }

            i = end;
        }

        // Leading spaces appear with the first bark
        for (var j = 0; j < charEntry.Length && charEntry[j] < 0; j++)
        {
            charEntry[j] = barks.Count > 0 ? 0 : -1;
        }

        return barks;
    }

    private static bool IsVowel(char c) => Vowels.Contains(char.ToLowerInvariant(c));

    public BarkData GenBarkData(BarkVoiceData data, char currChar, bool isWhisper)
    {
        var currBark = new BarkData(
            data.PitchAverage,
            data.VolumeAverage,
            data.PauseAverage);

        if (SkipChars.Contains(currChar))
            currBark.Enabled = false;

        if (LongPauseChars.Contains(currChar))
        {
            currBark.Pause *= 1.2f;
            currBark.Enabled = false;
        }

        if (isWhisper)
            currBark.Volume -= SharedAudioSystem.GainToVolume(4f);

        if (Soglasnoy.Contains(currChar))
        {
            currBark.Pitch -= 0.2f;
            currBark.Volume -= SharedAudioSystem.GainToVolume(4f);
            currBark.Pause *= 0.8f;
        }

        currBark.Pitch += System.Random.Shared.NextFloat(-data.PitchVariance, data.PitchVariance);

        return currBark;
    }

    public void Bark(Entity<BarkComponent> entity, string text, bool isWhisper)
    {
        var ev = new TransformSpeakerBarkEvent(entity, entity.Comp.VoiceData);
        RaiseLocalEvent(entity, ev);
        Bark(entity, GenBarkData(ev.VoiceData, text, isWhisper));
    }

    public abstract void Bark(Entity<BarkComponent> entity, List<BarkData> barks);
}

public sealed class TransformSpeakerBarkEvent(EntityUid sender, BarkVoiceData voiceData) : EntityEventArgs, IInventoryRelayEvent
{
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;

    public EntityUid Sender = sender;
    public BarkVoiceData VoiceData = voiceData;
}
