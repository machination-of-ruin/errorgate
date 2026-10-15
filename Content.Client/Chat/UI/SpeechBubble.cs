using System.Numerics;
using Content.Shared._White.CCVar;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Speech;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using YamlDotNet.Core.Tokens;

namespace Content.Client.Chat.UI
{
    public abstract class SpeechBubble : Control
    {
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] protected readonly IConfigurationManager ConfigManager = default!;

        private readonly SharedTransformSystem _transformSystem;

        public enum SpeechType : byte
        {
            Emote,
            Say,
            Whisper,
            Looc
        }

        /// <summary>
        ///     The total time a speech bubble stays on screen.
        /// </summary>
        private const float TotalTime = 4;

        /// <summary>
        ///     The amount of time at the end of the bubble's life at which it starts fading.
        /// </summary>
        private const float FadeTime = 0.25f;

        /// <summary>
        ///     The distance in world space to offset the speech bubble from the center of the entity.
        ///     i.e. greater -> higher above the mob's head.
        /// </summary>
        private const float EntityVerticalOffset = 0.5f;

        /// <summary>
        ///     The default maximum width for speech bubbles.
        /// </summary>
        public const float SpeechMaxWidth = 256;

        private readonly EntityUid _senderEntity;

        private float _timeLeft = TotalTime;

        /// <summary>The text that appears letter by letter, if the bubble has one (set while the bubble is built).</summary>
        protected OutlinedSpeechText? RevealText;

        /// <summary>Seconds between two letters of a voice without barks, and the longer pause after punctuation.</summary>
        private const float DefaultLetterPause = 0.045f;

        public float VerticalOffset { get; set; }
        private float _verticalOffsetAchieved;

        public Vector2 ContentSize { get; private set; }

        // man down
        public event Action<EntityUid, SpeechBubble>? OnDied;


        public static SpeechBubble CreateSpeechBubble(SpeechType type, ChatMessage message, EntityUid senderEntity)
        {
            // WWDP EDIT START
            Color? color = null;
            if(IoCManager.Resolve<IConfigurationManager>().GetCVar(WhiteCVars.ColoredBubbleChat))
                color = Color.FromHex(SharedChatSystem.GetNameColor(SharedChatSystem.GetStringInsideTag(message, "Name")));
            // WWDP EDIT END
            switch (type)
            {
                case SpeechType.Emote:
                    return new TextSpeechBubble(message, senderEntity, "emoteBox", color); // WWDP EDIT

                case SpeechType.Say:
                    return new FancyTextSpeechBubble(message, senderEntity, "sayBox", color); // WWDP EDIT

                case SpeechType.Whisper:
                    return new FancyTextSpeechBubble(message, senderEntity, "whisperBox", color); // WWDP EDIT

                case SpeechType.Looc:
                    return new TextSpeechBubble(message, senderEntity, "emoteBox", Color.FromHex("#48d1cc"));

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public SpeechBubble(ChatMessage message, EntityUid senderEntity, string speechStyleClass, Color? fontColor = null)
        {
            IoCManager.InjectDependencies(this);
            _senderEntity = senderEntity;
            _transformSystem = _entityManager.System<SharedTransformSystem>();

            // Use text clipping so new messages don't overlap old ones being pushed up.
            RectClipContent = true;

            var bubble = BuildBubble(message, speechStyleClass, fontColor);

            AddChild(bubble);

            ForceRunStyleUpdate();

            bubble.Measure(Vector2Helpers.Infinity);
            ContentSize = bubble.DesiredSize;
            _verticalOffsetAchieved = -ContentSize.Y;

            // The box is measured with the whole text; then the text is taken away and comes back letter by letter
            if (RevealText != null && ConfigManager.GetCVar(CCVars.ChatBubbleLetterReveal))
                StartReveal(speechStyleClass == "whisperBox");
        }

        /// <summary>
        ///     Works out when each letter appears: the way the voice barks do (Content.Client/_White/Bark): one letter per bark, the pause
        ///     before each as the voice's data gives it, longer after punctuation and shorter after consonants. A voice without barks
        ///     uses a steady pace. The bubble stays long enough to be read after the last letter.
        /// </summary>
        private void StartReveal(bool whisper)
        {
            var text = RevealText!.PlainText;
            var count = text.Length;
            var times = new float[count];

            var barkVoice = _entityManager.TryGetComponent<Content.Shared._White.Bark.Components.BarkComponent>(_senderEntity, out var bark) ? bark.VoiceData : null;
            if (barkVoice == null)
            {
                // No voice, no barks: a steady pace
                var steady = 0f;
                for (var i = 0; i < count; i++)
                {
                    steady += text[i] is '.' or ',' or '?' or '!' ? DefaultLetterPause * 1.2f : DefaultLetterPause;
                    times[i] = steady;
                }
            }
            else
            {
                // The way ClientBarkSystem.Update plays them: it takes the next bark, waits until its pause has run out in steps of one
                // tick, plays it, and takes the next one at the next tick. So every bark costs its pause rounded up to whole ticks and one
                // tick more, not just its pause
                var barks = _entityManager.System<Content.Client._White.Bark.BarkSystem>();
                var tick = (float) IoCManager.Resolve<IGameTiming>().TickPeriod.TotalSeconds;
                var tickIndex = 0;
                var waited = 0f;
                for (var i = 0; i < count; i++)
                {
                    var pause = barks.GenBarkData(barkVoice, text[i], whisper).Pause;
                    waited = 0f;
                    while (true)
                    {
                        tickIndex++;
                        if (pause <= waited)
                            break;

                        waited += tick;
                    }

                    times[i] = tickIndex * tick;
                }

                // A long message gets barks only for its first letters (the bark limit of the options); the letters are revealed in that
                // same time, so the text is done when the barks are
                var limit = ConfigManager.GetCVar(WhiteCVars.BarkLimit);
                if (limit > 0 && count > limit)
                {
                    var factor = times[limit - 1] / times[count - 1];
                    for (var i = 0; i < count; i++)
                    {
                        times[i] *= factor;
                    }
                }
            }

            _timeLeft = TotalTime + (count > 0 ? times[count - 1] : 0f);
            RevealText.SetSchedule(times);
        }

        protected abstract Control BuildBubble(ChatMessage message, string speechStyleClass, Color? fontColor = null);

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);

            _timeLeft -= args.DeltaSeconds;

            if (_entityManager.Deleted(_senderEntity) || _timeLeft <= 0)
            {
                // Timer spawn to prevent concurrent modification exception.
                Timer.Spawn(0, Die);
                return;
            }

            // Lerp to our new vertical offset if it's been modified.
            if (MathHelper.CloseToPercent(_verticalOffsetAchieved - VerticalOffset, 0, 0.1))
            {
                _verticalOffsetAchieved = VerticalOffset;
            }
            else
            {
                _verticalOffsetAchieved = MathHelper.Lerp(_verticalOffsetAchieved, VerticalOffset, 10 * args.DeltaSeconds);
            }

            if (!_entityManager.TryGetComponent<TransformComponent>(_senderEntity, out var xform) || xform.MapID != _eyeManager.CurrentMap)
            {
                Modulate = Color.White.WithAlpha(0);
                return;
            }

            if (_timeLeft <= FadeTime)
            {
                // Update alpha if we're fading.
                Modulate = Color.White.WithAlpha(_timeLeft / FadeTime);
            }
            else
            {
                // Make opaque otherwise, because it might have been hidden before
                Modulate = Color.White;
            }

            var baseOffset = 0f;

			// WWDP EDIT START
            var spriteOffset = Vector2.Zero;
            if (_entityManager.TryGetComponent<SpriteComponent>(_senderEntity, out var spriteComponent))
                spriteOffset = spriteComponent.Offset;
			// WWDP EDIT END

            if (_entityManager.TryGetComponent<SpeechComponent>(_senderEntity, out var speech))
                baseOffset = speech.SpeechBubbleOffset;

            var offset = (-_eyeManager.CurrentEye.Rotation).ToWorldVec() * -(EntityVerticalOffset + baseOffset);
            offset += (-_eyeManager.CurrentEye.Rotation).RotateVec(spriteOffset); // WWDP EDIT // need to account for sprite offset for pixel perfect camera
            var worldPos = _transformSystem.GetWorldPosition(xform) + offset;

            var lowerCenter = _eyeManager.WorldToScreen(worldPos) / UIScale;
            var screenPos = lowerCenter - new Vector2(ContentSize.X / 2, ContentSize.Y + _verticalOffsetAchieved);
            // Round to nearest 0.5
            screenPos = (screenPos * 2).Rounded() / 2;
            LayoutContainer.SetPosition(this, screenPos);

            var height = MathF.Ceiling(MathHelper.Clamp(lowerCenter.Y - screenPos.Y, 0, ContentSize.Y));
            SetHeight = height;
        }

        private void Die()
        {
            if (Disposed)
            {
                return;
            }

            OnDied?.Invoke(_senderEntity, this);
        }

        /// <summary>
        ///     Causes the speech bubble to start fading IMMEDIATELY.
        /// </summary>
        public void FadeNow()
        {
            if (_timeLeft > FadeTime)
            {
                _timeLeft = FadeTime;
            }
        }


        protected FormattedMessage FormatSpeech(string message, string fontId) => FormatSpeech(message, null, fontId);                  // WWDP
        protected FormattedMessage FormatSpeech(string message, Color? fontColor = null) => FormatSpeech(message, fontColor, null);     // WWDP
        protected FormattedMessage FormatSpeech(string message, Color? fontColor = null, string? fontId = null)     // WWDP EDIT
        {
            var msg = new FormattedMessage();
            if (fontColor != null)
                msg.PushColor(fontColor.Value);
            if (fontId != null) // WWDP EDIT START
            {
                msg.AddMarkupOrThrow($"[font=\"{fontId}\"]");
                msg.AddMarkupOrThrow(message);
                msg.AddMarkupOrThrow($"[/font]");
            }
            else
                msg.AddMarkupOrThrow(message); // WWDP EDIT END
            return msg;
        }

        /// <summary>ERRORGATE speech: bold Bedstead with a black outline and no box (see <see cref="OutlinedSpeechText"/>).</summary>
        protected OutlinedSpeechText BuildOutlined(string markup, Color? fontColor, bool italic, float maxWidth = SpeechMaxWidth, float opacity = 1f)
        {
            return new OutlinedSpeechText(markup, fontColor, italic, maxWidth, opacity);
        }

        protected FormattedMessage ExtractAndFormatSpeechSubstring(ChatMessage message, string tag, Color? fontColor = null)
        {
            return FormatSpeech(SharedChatSystem.GetStringInsideTag(message, tag), fontColor);
        }

    }

    public sealed class TextSpeechBubble : SpeechBubble
    {
        public TextSpeechBubble(ChatMessage message, EntityUid senderEntity, string speechStyleClass, Color? fontColor = null)
            : base(message, senderEntity, speechStyleClass, fontColor)
        {
        }

        protected override Control BuildBubble(ChatMessage message, string speechStyleClass, Color? fontColor = null)
        {
            // Emotes and LOOC are drawn like the rest of the speech, in italics
            return BuildOutlined(message.WrappedMessage, fontColor, true, SpeechMaxWidth, ConfigManager.GetCVar(CCVars.SpeechBubbleTextOpacity));
        }
    }

    public sealed class FancyTextSpeechBubble : SpeechBubble
    {

        public FancyTextSpeechBubble(ChatMessage message, EntityUid senderEntity, string speechStyleClass, Color? fontColor = null)
            : base(message, senderEntity, speechStyleClass, fontColor)
        {
        }

        protected override Control BuildBubble(ChatMessage message, string speechStyleClass, Color? fontColor = null)
        {
            var italic = speechStyleClass == "whisperBox";
            var textOpacity = ConfigManager.GetCVar(CCVars.SpeechBubbleTextOpacity);
            var content = BuildOutlined(SharedChatSystem.GetStringInsideTag(message, "BubbleContent"), fontColor, italic, SpeechMaxWidth, textOpacity);
            RevealText = content;
            if (!ConfigManager.GetCVar(CCVars.ChatEnableFancyBubbles))
                return content;

            // The name of the speaker above the text, in the same outline
            var header = BuildOutlined(SharedChatSystem.GetStringInsideTag(message, "BubbleHeader"), fontColor, false, SpeechMaxWidth, ConfigManager.GetCVar(CCVars.SpeechBubbleSpeakerOpacity));
            header.HorizontalAlignment = HAlignment.Center;
            header.VerticalAlignment = VAlignment.Top;
            content.HorizontalAlignment = HAlignment.Center;
            content.VerticalAlignment = VAlignment.Bottom;
            content.Margin = new Thickness(0, 14, 0, 0);

            return new Control { Children = { content, header } };
        }
    }
}
