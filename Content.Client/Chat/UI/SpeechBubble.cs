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

        private static readonly (int X, int Y)[] OutlineOffsets =
        {
            (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1),
        };

        /// <summary>
        ///     ERRORGATE speech: bold Bedstead with a black outline and no box behind it. The outline is the same text drawn eight times
        ///     in black, a pixel off in every direction, under the text itself. All the copies have the same margin in total, so they
        ///     measure the same and the bubble is as wide as the text with a pixel of room on each side.
        /// </summary>
        protected Control BuildOutlined(string markup, Color? fontColor, bool italic, float maxWidth = SpeechMaxWidth, float opacity = 1f)
        {
            var fontId = italic ? "BedsteadBoldItalic" : "BedsteadBold";
            var holder = new Control();
            var plain = FormattedMessage.RemoveMarkupPermissive(markup);

            foreach (var (dx, dy) in OutlineOffsets)
            {
                var shadow = new RichTextLabel
                {
                    MaxWidth = maxWidth,
                    Margin = new Thickness(1 + dx, 1 + dy, 1 - dx, 1 - dy),
                    ModulateSelfOverride = Color.White.WithAlpha(opacity),
                };

                var shadowMessage = new FormattedMessage();
                shadowMessage.PushColor(Color.Black);
                shadowMessage.AddMarkupOrThrow($"[font=\"{fontId}\"]");
                shadowMessage.AddText(plain);
                shadowMessage.AddMarkupOrThrow("[/font]");
                shadow.SetMessage(shadowMessage);
                holder.AddChild(shadow);
            }

            var main = new RichTextLabel
            {
                MaxWidth = maxWidth,
                Margin = new Thickness(1),
                ModulateSelfOverride = Color.White.WithAlpha(opacity),
            };

            // The message brings its own font tag; bold and italic pick the variant of whatever font is in force
            main.SetMessage(FormatSpeech(italic ? $"[bold][italic]{markup}[/italic][/bold]" : $"[bold]{markup}[/bold]", fontColor, "Bedstead"));
            holder.AddChild(main);
            return holder;
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
