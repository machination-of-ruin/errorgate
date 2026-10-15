using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Chat.UI;

/// <summary>
///     ERRORGATE speech: bold Bedstead with a black outline and no box behind it. The outline is the very same message drawn in black,
///     shifted a pixel or two in every direction, under the text itself, so it always lines up with the text however the message is
///     built. All the copies have the same margin in total, so they measure the same.
///
///     The text can be revealed letter by letter (<see cref="SetSchedule"/>): the letters not yet revealed stay in the layout but are drawn
///     transparent, so the box and the line breaks do not move while the text appears.
/// </summary>
public sealed class OutlinedSpeechText : Control
{
    private const int OutlineWidth = 2;

    // A round outline two pixels thick: every offset within a distance of two, but not the corners
    private static readonly (int X, int Y)[] OutlineOffsets = BuildOutlineOffsets();

    private readonly List<RichTextLabel> _labels = new();
    private readonly FormattedMessage _full;
    private int _shown = -1;
    private float[]? _times;
    private float _elapsed;
    private int _revealed;
    private float _lineHeight = -1;

    /// <summary>How many characters the text has (markup not counted).</summary>
    public int Length { get; }

    /// <summary>The text without markup, character by character.</summary>
    public string PlainText { get; }

    public OutlinedSpeechText(string markup, Color? fontColor, bool italic, float maxWidth, float opacity)
    {
        // The message brings its own font tag; bold and italic pick the variant of whatever font is in force
        var styled = italic ? $"[bold][italic]{markup}[/italic][/bold]" : $"[bold]{markup}[/bold]";
        _full = new FormattedMessage();
        if (fontColor != null)
            _full.PushColor(fontColor.Value);

        _full.AddMarkupOrThrow("[font=\"Bedstead\"]");
        _full.AddMarkupOrThrow(styled);
        _full.AddMarkupOrThrow("[/font]");

        var length = 0;
        var plain = new System.Text.StringBuilder();
        foreach (var node in _full.Nodes)
        {
            if (node.Name != null || node.Value.StringValue is not { } text)
                continue;

            length += text.Length;
            plain.Append(text);
        }

        Length = length;
        PlainText = plain.ToString();

        foreach (var (dx, dy) in OutlineOffsets)
        {
            var shadow = new RichTextLabel
            {
                MaxWidth = maxWidth,
                Margin = new Thickness(OutlineWidth + dx, OutlineWidth + dy, OutlineWidth - dx, OutlineWidth - dy),
                ModulateSelfOverride = Color.Black.WithAlpha(opacity),
            };

            _labels.Add(shadow);
            AddChild(shadow);
        }

        var main = new RichTextLabel
        {
            MaxWidth = maxWidth,
            Margin = new Thickness(OutlineWidth),
            ModulateSelfOverride = Color.White.WithAlpha(opacity),
        };

        _labels.Add(main);
        AddChild(main);
        Reveal(Length);
    }

    private static (int, int)[] BuildOutlineOffsets()
    {
        var list = new List<(int, int)>();
        for (var x = -OutlineWidth; x <= OutlineWidth; x++)
        {
            for (var y = -OutlineWidth; y <= OutlineWidth; y++)
            {
                if ((x == 0 && y == 0) || x * x + y * y > OutlineWidth * OutlineWidth + 1)
                    continue;

                list.Add((x, y));
            }
        }

        return list.ToArray();
    }

    /// <summary>Shows the first <paramref name="count"/> characters; the rest are in the layout but transparent.</summary>
    public void Reveal(int count)
    {
        count = Math.Clamp(count, 0, Length);
        if (count == _shown)
            return;

        _shown = count;
        var message = new FormattedMessage();
        var seen = 0;
        foreach (var node in _full.Nodes)
        {
            if (node.Name != null)
            {
                if (node.Closing)
                    message.Pop();
                else
                    message.PushTag(node);

                continue;
            }

            if (node.Value.StringValue is not { } text)
                continue;

            var visible = Math.Clamp(count - seen, 0, text.Length);
            seen += text.Length;
            if (visible > 0)
                message.AddText(text[..visible]);

            if (visible < text.Length)
            {
                message.PushTag(new MarkupNode("color", new MarkupParameter(Color.Transparent), null));
                message.AddText(text[visible..]);
                message.Pop();
            }
        }

        foreach (var label in _labels)
        {
            label.SetMessage(message);
        }
    }

    /// <summary>Sets when each letter appears, in seconds from now. Without a schedule all the letters are there at once.</summary>
    public void SetSchedule(float[]? times)
    {
        _times = times;
        _elapsed = 0;
        _revealed = 0;
        Reveal(times == null ? Length : 0);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_times == null || _revealed >= _times.Length)
            return;

        _elapsed += args.DeltaSeconds;
        var n = _revealed;
        while (n < _times.Length && _times[n] <= _elapsed)
        {
            n++;
        }

        if (n != _revealed)
        {
            _revealed = n;
            Reveal(n);
        }
    }

    /// <summary>
    ///     The height of the lines that have letters so far (the whole height without a schedule), estimated from how much of the text is
    ///     there: the bubble is as tall as this and grows upwards from the head as the lines come in.
    /// </summary>
    public float VisibleHeight
    {
        get
        {
            var full = DesiredSize.Y;
            if (_times == null || _revealed >= Length || Length == 0)
                return full;

            if (_lineHeight < 0)
            {
                // The height of one line: this text's own font, measured once
                var probe = new RichTextLabel { MaxWidth = 1000 };
                probe.SetMessage(FormattedMessage.FromMarkupPermissive("[font=\"Bedstead\"][bold]A[/bold][/font]"));
                probe.Measure(new Vector2(1000, 1000));
                _lineHeight = Math.Max(1f, probe.DesiredSize.Y);
            }

            var content = full - 2 * OutlineWidth;
            var lines = Math.Max(1, (int) MathF.Round(content / _lineHeight));
            var visible = Math.Clamp((int) MathF.Ceiling(lines * (_revealed + 1f) / Length), 1, lines);
            return visible * _lineHeight + 2 * OutlineWidth;
        }
    }
}
