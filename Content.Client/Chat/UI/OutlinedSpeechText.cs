using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Chat.UI;

/// <summary>
///     ERRORGATE speech: bold Bedstead with a black outline two pixels thick, no box behind it. It draws the letters itself (it wraps the
///     lines and draws every letter), so each letter can be revealed at its own time and the letters of an exclamation can jump when
///     they appear. The markup of the message gives the colours and the italics; the font is always Bedstead Bold.
///
///     The size of the text is that of the whole message from the start, so the box and the lines do not change while the letters
///     come in (<see cref="SetSchedule"/>).
/// </summary>
public sealed class OutlinedSpeechText : Control
{
    private const int OutlineWidth = 2;
    private const int FontSize = 12;

    /// <summary>How long a letter of an exclamation jumps, and how high, in pixels.</summary>
    private const float JumpSeconds = 0.2f;
    private const float JumpHeight = 3f;

    // A round outline two pixels thick: every offset within a distance of two, but not the corners
    private static readonly (int X, int Y)[] OutlineOffsets = BuildOutlineOffsets();

    private readonly record struct Glyph(Rune Rune, Color Color, bool Italic, bool Jumps);

    private readonly List<Glyph> _glyphs = new();
    private readonly List<(int Glyph, Vector2 Baseline)> _placed = new();
    private readonly float _maxWidth;
    private readonly float _opacity;
    private readonly VectorFont _regular;
    private readonly VectorFont _italic;

    private float _laidOutScale = -1;
    private Vector2 _sizePixels;
    private float[]? _times;
    private float _elapsed;

    /// <summary>How many letters the text has (markup not counted).</summary>
    public int Length => _glyphs.Count;

    /// <summary>The text without markup, letter by letter.</summary>
    public string PlainText { get; }

    public OutlinedSpeechText(string markup, Color? fontColor, bool italic, float maxWidth, float opacity)
    {
        _maxWidth = maxWidth;
        _opacity = opacity;

        var cache = IoCManager.Resolve<IResourceCache>();
        _regular = new VectorFont(cache.GetResource<FontResource>("/Fonts/_White/Bedstead/Bedstead-bold.otf"), FontSize);
        _italic = new VectorFont(cache.GetResource<FontResource>("/Fonts/_White/Bedstead/Bedstead-bold-italic.ttf"), FontSize);

        // The letters with their colours and italics, from the markup of the message
        var colors = new Stack<Color>();
        colors.Push(fontColor ?? Color.White);
        var italics = italic ? 1 : 0;
        var plain = new StringBuilder();
        foreach (var node in FormattedMessage.FromMarkupPermissive(markup).Nodes)
        {
            if (node.Name == null)
            {
                if (node.Value.StringValue is not { } text)
                    continue;

                foreach (var rune in text.EnumerateRunes())
                {
                    _glyphs.Add(new Glyph(rune, colors.Peek(), italics > 0, false));
                    plain.Append(rune.ToString());
                }

                continue;
            }

            switch (node.Name)
            {
                case "color" when !node.Closing:
                    colors.Push(node.Value.ColorValue ?? (node.Value.StringValue is { } name && Color.TryFromName(name, out var named) ? named : colors.Peek()));
                    break;
                case "color" when colors.Count > 1:
                    colors.Pop();
                    break;
                case "italic":
                    italics += node.Closing ? -1 : 1;
                    break;
            }
        }

        PlainText = plain.ToString();
        MarkExclamations();
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

    /// <summary>The letters of a sentence that ends in an exclamation mark are the ones that jump.</summary>
    private void MarkExclamations()
    {
        var start = 0;
        for (var i = 0; i < _glyphs.Count; i++)
        {
            if (_glyphs[i].Rune.Value is not ('.' or '?' or '!'))
                continue;

            // A run of terminators ends the sentence; one '!' in it makes the sentence an exclamation
            var end = i;
            var exclamation = false;
            while (end < _glyphs.Count && _glyphs[end].Rune.Value is '.' or '?' or '!')
            {
                exclamation |= _glyphs[end].Rune.Value == '!';
                end++;
            }

            if (exclamation)
            {
                for (var k = start; k < end; k++)
                {
                    if (!Rune.IsWhiteSpace(_glyphs[k].Rune))
                        _glyphs[k] = _glyphs[k] with { Jumps = true };
                }
            }

            start = end;
            i = end - 1;
        }
    }

    /// <summary>
    ///     Sets when each letter appears, in seconds from now (the time of the letter at the same place in <see cref="PlainText"/>). Without a
    ///     schedule all the letters are there at once.
    /// </summary>
    public void SetSchedule(float[]? times)
    {
        _times = times;
        _elapsed = 0;
    }

    private void UpdateLayout(float scale)
    {
        if (MathF.Abs(scale - _laidOutScale) < 0.001f)
            return;

        _laidOutScale = scale;
        _placed.Clear();

        var pad = OutlineWidth * scale;
        var limit = MathF.Max(1f, _maxWidth * scale - 2 * pad);
        var lineHeight = _regular.GetLineHeight(scale);
        var ascent = _regular.GetAscent(scale);
        var jumpRoom = JumpHeight * scale;

        float Advance(int index)
        {
            var glyph = _glyphs[index];
            var font = glyph.Italic ? _italic : _regular;
            return font.TryGetCharMetrics(glyph.Rune, scale, out var metrics) ? metrics.Advance : 0;
        }

        var x = 0f;
        var line = 0;
        var widest = 0f;
        var i = 0;
        while (i < _glyphs.Count)
        {
            if (_glyphs[i].Rune.Value == '\n')
            {
                widest = MathF.Max(widest, x);
                x = 0;
                line++;
                i++;
                continue;
            }

            if (Rune.IsWhiteSpace(_glyphs[i].Rune))
            {
                x += Advance(i);
                i++;
                continue;
            }

            // A word is placed whole; it goes to the next line if it does not fit (a word longer than a line is broken where it ends)
            var end = i;
            var wordWidth = 0f;
            while (end < _glyphs.Count && !Rune.IsWhiteSpace(_glyphs[end].Rune))
            {
                wordWidth += Advance(end);
                end++;
            }

            if (x > 0 && x + wordWidth > limit)
            {
                widest = MathF.Max(widest, x);
                x = 0;
                line++;
            }

            for (var k = i; k < end; k++)
            {
                var advance = Advance(k);
                if (x > 0 && x + advance > limit)
                {
                    widest = MathF.Max(widest, x);
                    x = 0;
                    line++;
                }

                _placed.Add((k, new Vector2(pad + x, pad + jumpRoom + line * lineHeight + ascent)));
                x += advance;
            }

            i = end;
        }

        widest = MathF.Max(widest, x);
        _sizePixels = new Vector2(widest + 2 * pad, (line + 1) * lineHeight + 2 * pad + jumpRoom);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        UpdateLayout(UIScale);
        return _sizePixels / UIScale;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_times != null)
            _elapsed += args.DeltaSeconds;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        UpdateLayout(UIScale);
        var scale = UIScale;

        foreach (var (index, baseline) in _placed)
        {
            if (_times != null && index < _times.Length && _times[index] > _elapsed)
                continue;

            var glyph = _glyphs[index];
            var origin = baseline;
            if (_times != null && glyph.Jumps && index < _times.Length)
            {
                var age = (_elapsed - _times[index]) / JumpSeconds;
                if (age is >= 0 and < 1)
                    origin.Y -= JumpHeight * scale * 4f * age * (1f - age);
            }

            var font = glyph.Italic ? _italic : _regular;
            var outline = Color.Black.WithAlpha(_opacity);
            foreach (var (dx, dy) in OutlineOffsets)
            {
                font.DrawChar(handle, glyph.Rune, origin + new Vector2(dx, dy) * scale, scale, outline);
            }

            font.DrawChar(handle, glyph.Rune, origin, scale, glyph.Color.WithAlpha(glyph.Color.A * _opacity));
        }
    }
}
