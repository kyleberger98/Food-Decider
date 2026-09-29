using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Crucible.View.UI
{
    /// <summary>
    /// Colour tokens for the HUD, in the spirit of Humankind: soft, translucent slate-blue glass with
    /// cream text and a warm gold accent; yields keep their familiar Civ-style hues.
    /// </summary>
    public static class Theme
    {
        public static readonly Color Chrome = new Color(0.07f, 0.10f, 0.13f, 0.80f);
        public static readonly Color Panel = new Color(0.09f, 0.12f, 0.16f, 0.90f);
        public static readonly Color Card = new Color(1f, 1f, 1f, 0.055f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color Glow = new Color(0.95f, 0.82f, 0.55f, 0.35f);
        public static readonly Color Text = new Color(0.96f, 0.93f, 0.86f);
        public static readonly Color Muted = new Color(0.68f, 0.71f, 0.74f);
        public static readonly Color Accent = new Color(0.94f, 0.78f, 0.47f);
        public static readonly Color AccentDark = new Color(0.55f, 0.42f, 0.20f);
        public static readonly Color Good = new Color(0.50f, 0.82f, 0.56f);
        public static readonly Color Bad = new Color(0.92f, 0.45f, 0.40f);
        public static readonly Color War = new Color(0.96f, 0.58f, 0.34f);
        public static readonly Color Info = new Color(0.58f, 0.76f, 0.98f);
        public static readonly Color Button = new Color(1f, 1f, 1f, 0.09f);
        public static readonly Color ButtonHover = new Color(1f, 1f, 1f, 0.17f);
        public static readonly Color Track = new Color(1f, 1f, 1f, 0.11f);

        public static readonly Color Food = new Color(0.56f, 0.84f, 0.36f);
        public static readonly Color Production = new Color(0.93f, 0.57f, 0.30f);
        public static readonly Color Gold = new Color(0.98f, 0.81f, 0.30f);
        public static readonly Color Science = new Color(0.42f, 0.72f, 1f);
        public static readonly Color Culture = new Color(0.80f, 0.52f, 0.96f);
        public static readonly Color Faith = new Color(0.95f, 0.93f, 0.72f);
        public static readonly Color Happiness = new Color(0.50f, 0.86f, 0.46f);
        public static readonly Color Tourism = new Color(0.96f, 0.52f, 0.72f);
    }

    public enum ButtonStyle
    {
        Normal,
        Primary,
        Danger,
        Ghost,
    }

    /// <summary>Tiny element factory: every style is inline, so the HUD needs no USS or theme assets.</summary>
    public static class Ui
    {
        /// <summary>Every text size is scaled by this and never drops below <see cref="MinFontSize"/>, so the HUD stays readable.</summary>
        public const float FontScale = 1.15f;
        public const float MinFontSize = 12f;

        /// <summary>Soft rounding for panels, cards and pills.</summary>
        public const float PanelRadius = 16f, CardRadius = 12f;

        static FontDefinition? _regular, _bold;

        /// <summary>Inter (Resources/Fonts, SIL OFL), medium for text and bold for emphasis; Unity's built-in font otherwise.</summary>
        public static void LoadFonts()
        {
            var regular = Resources.Load<Font>("Fonts/Inter-Medium");
            var bold = Resources.Load<Font>("Fonts/Inter-Bold");
            if (regular == null)
            {
                foreach (var name in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
                {
                    try { regular = Resources.GetBuiltinResource<Font>(name); }
                    catch (ArgumentException) { regular = null; }
                    if (regular != null) break;
                }
            }
            _regular = regular != null ? FontDefinition.FromFont(regular) : (FontDefinition?)null;
            _bold = bold != null ? FontDefinition.FromFont(bold) : (FontDefinition?)null;
        }

        public static void ApplyFont(VisualElement e, bool bold = false)
        {
            if (bold && _bold.HasValue) e.style.unityFontDefinition = _bold.Value;
            else if (_regular.HasValue) e.style.unityFontDefinition = _regular.Value;
            if (bold && !_bold.HasValue) e.style.unityFontStyleAndWeight = FontStyle.Bold;
        }

        public static float Size(float size) => Mathf.Max(MinFontSize, Mathf.Round(size * FontScale));
        public static VisualElement Box(Color? bg = null, float pad = 0, float radius = 0)
        {
            var e = new VisualElement();
            if (bg.HasValue) e.style.backgroundColor = bg.Value;
            Pad(e, pad);
            Radius(e, radius);
            return e;
        }

        sealed class GapInfo
        {
            public float Gap;
            public bool Vertical;
        }

        public static VisualElement Row(float gap = 0, bool center = true)
        {
            var e = new VisualElement { userData = new GapInfo { Gap = gap } };
            e.style.flexDirection = FlexDirection.Row;
            if (center) e.style.alignItems = Align.Center;
            return e;
        }

        public static VisualElement Col(float gap = 0)
        {
            var e = new VisualElement { userData = new GapInfo { Gap = gap, Vertical = true } };
            e.style.flexDirection = FlexDirection.Column;
            return e;
        }

        /// <summary>
        /// Adds a child, spacing it from the previous one by the container's gap (UI Toolkit has no
        /// flexbox <c>gap</c>). Returns the child for chaining.
        /// </summary>
        public static T Put<T>(this VisualElement parent, T child) where T : VisualElement
        {
            if (parent.userData is GapInfo g && g.Gap > 0 && parent.childCount > 0)
            {
                if (g.Vertical) child.style.marginTop = g.Gap;
                else child.style.marginLeft = g.Gap;
            }
            parent.Add(child);
            return child;
        }

        public static Label Text(string text, float size = 13, Color? color = null, bool bold = false, bool wrap = false)
        {
            var l = new Label(text);
            l.style.fontSize = Size(size);
            l.style.color = color ?? Theme.Text;
            ApplyFont(l, bold);
            l.style.whiteSpace = wrap ? WhiteSpace.Normal : WhiteSpace.NoWrap;
            Pad(l, 0);
            l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0;
            return l;
        }

        public static Label Heading(string text) => Text(text, 16, Theme.Text, bold: true);

        public static Label Caption(string text)
        {
            var c = Text(text.ToUpperInvariant(), 10, Theme.Accent, bold: true);
            c.style.letterSpacing = 1.2f;
            c.style.opacity = 0.85f;
            return c;
        }

        static readonly System.Text.RegularExpressions.Regex HotkeySuffix =
            new System.Text.RegularExpressions.Regex(@"^(.*\S)\s{2}\(((?:Shift\+)?(?:[A-Z]|F\d+|Esc|Space|Tab|Enter))\)$");

        public static Button Btn(string text, Action onClick, ButtonStyle style = ButtonStyle.Normal, bool enabled = true, float size = 12)
        {
            // Keep labels clean: a trailing "  (Key)" hint moves into the tooltip.
            string hotkey = null;
            var m = HotkeySuffix.Match(text);
            if (m.Success) { text = m.Groups[1].Value; hotkey = m.Groups[2].Value; }
            var b = new Button(onClick) { text = text };
            if (hotkey != null) b.tooltip = $"Shortcut: {hotkey}";
            // Soft pill buttons: frosted glass, a warm gold primary, a muted red for danger.
            Color bg = style == ButtonStyle.Primary ? Theme.Accent
                : style == ButtonStyle.Danger ? new Color(0.62f, 0.26f, 0.23f, 0.9f)
                : style == ButtonStyle.Ghost ? new Color(0, 0, 0, 0)
                : Theme.Button;
            Color hover = style == ButtonStyle.Primary ? new Color(1f, 0.87f, 0.60f)
                : style == ButtonStyle.Danger ? new Color(0.74f, 0.32f, 0.28f)
                : Theme.ButtonHover;
            b.style.backgroundColor = bg;
            b.style.color = style == ButtonStyle.Primary ? new Color(0.16f, 0.12f, 0.05f) : Theme.Text;
            b.style.fontSize = Size(size);
            ApplyFont(b, bold: true);
            b.style.paddingLeft = b.style.paddingRight = 14;
            b.style.paddingTop = b.style.paddingBottom = 7;
            b.style.marginLeft = b.style.marginRight = b.style.marginTop = b.style.marginBottom = 0;
            Radius(b, 18);
            Border(b, style == ButtonStyle.Ghost ? new Color(1, 1, 1, 0.14f) : new Color(1, 1, 1, style == ButtonStyle.Primary ? 0.25f : 0.06f), 1);
            b.RegisterCallback<PointerEnterEvent>(_ => { if (b.enabledSelf) b.style.backgroundColor = hover; });
            b.RegisterCallback<PointerLeaveEvent>(_ => b.style.backgroundColor = bg);
            b.SetEnabled(enabled);
            if (!enabled) b.style.opacity = 0.4f;
            return b;
        }

        /// <summary>A horizontal progress bar (fraction clamped to 0…1).</summary>
        public static VisualElement Bar(float fraction, Color fill, float height = 7, Color? track = null)
        {
            var t = Box(track ?? Theme.Track, 0, height / 2);
            t.style.height = height;
            t.style.flexGrow = 1;
            var f = Box(fill, 0, height / 2);
            f.style.height = height;
            f.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
            t.Add(f);
            return t;
        }

        public static VisualElement Pill(string text, Color bg, Color? fg = null, float size = 10)
        {
            var p = Box(bg, 0, 12);
            p.style.paddingLeft = p.style.paddingRight = 10;
            p.style.paddingTop = p.style.paddingBottom = 3;
            p.Add(Text(text, size, fg ?? new Color(0.08f, 0.08f, 0.1f), bold: true));
            return p;
        }

        /// <summary>
        /// A yield/stat chip, Humankind style: a soft coloured orb with the value beside it. The caption
        /// moves into the tooltip to keep the bar clean.
        /// </summary>
        public static VisualElement Stat(string caption, string value, Color color, string tooltip = null)
        {
            var row = Row();
            row.style.marginRight = 18;
            var orb = Box(new Color(color.r, color.g, color.b, 0.22f), 0, 9);
            orb.style.width = orb.style.height = 18;
            orb.style.alignItems = Align.Center;
            orb.style.justifyContent = Justify.Center;
            orb.style.marginRight = 6;
            var core = Box(color, 0, 4);
            core.style.width = core.style.height = 8;
            orb.Add(core);
            row.Add(orb);
            row.Add(Text(value, 14, Theme.Text, bold: true));
            string capital = caption.Length > 0 ? char.ToUpperInvariant(caption[0]) + caption.Substring(1) : caption;
            row.tooltip = tooltip != null ? $"{capital}: {tooltip}" : capital;
            return row;
        }

        public static VisualElement Divider()
        {
            var d = Box(Theme.Line);
            d.style.height = 1;
            d.style.marginTop = d.style.marginBottom = 10;
            return d;
        }

        public static VisualElement Spacer()
        {
            var s = new VisualElement();
            s.style.flexGrow = 1;
            return s;
        }

        public static VisualElement Card(float pad = 12)
        {
            var c = Box(Theme.Card, pad, CardRadius);
            c.style.marginBottom = 8;
            return c;
        }

        public static void Pad(VisualElement e, float p)
        {
            e.style.paddingLeft = e.style.paddingRight = e.style.paddingTop = e.style.paddingBottom = p;
        }

        public static void Radius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;
        }

        public static void Border(VisualElement e, Color c, float w)
        {
            e.style.borderLeftWidth = e.style.borderRightWidth = e.style.borderTopWidth = e.style.borderBottomWidth = w;
            e.style.borderLeftColor = e.style.borderRightColor = e.style.borderTopColor = e.style.borderBottomColor = c;
        }

        public static void Absolute(VisualElement e, float? left = null, float? top = null, float? right = null, float? bottom = null)
        {
            e.style.position = Position.Absolute;
            if (left.HasValue) e.style.left = left.Value;
            if (top.HasValue) e.style.top = top.Value;
            if (right.HasValue) e.style.right = right.Value;
            if (bottom.HasValue) e.style.bottom = bottom.Value;
        }

        /// <summary>
        /// Civ-style unit flag: a dark-rimmed heraldic shield in the owner's colour carrying the unit's
        /// symbol. Three stacked, tinted images, so any colour costs no new texture.
        /// </summary>
        public static VisualElement Flag(Icons.UnitIcon icon, Color color, float size = 28)
        {
            var root = Image(Icons.IconTextures.ShieldRim, new Color(0.07f, 0.07f, 0.09f, 0.95f), size);
            var field = Image(Icons.IconTextures.ShieldField, color, size);
            Absolute(field, 0, 0);
            root.Add(field);
            float g = size * 0.62f;
            var glyph = Image(Icons.IconTextures.Glyph(icon), new Color(0.98f, 0.96f, 0.9f), g);
            Absolute(glyph, (size - g) / 2, size * 0.14f);
            root.Add(glyph);
            root.pickingMode = field.pickingMode = glyph.pickingMode = PickingMode.Ignore;
            return root;
        }

        /// <summary>A bare unit symbol, tinted (for lists and cards).</summary>
        public static VisualElement Glyph(Icons.UnitIcon icon, Color color, float size = 16) =>
            Image(Icons.IconTextures.Glyph(icon), color, size);

        static VisualElement Image(Texture2D tex, Color tint, float size)
        {
            var e = new VisualElement();
            e.style.width = e.style.height = size;
            e.style.flexShrink = 0;
            e.style.backgroundImage = new StyleBackground(tex);
            e.style.unityBackgroundImageTintColor = tint;
            return e;
        }

        public static void Show(VisualElement e, bool visible) => e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>A soft floating panel: translucent glass, rounded, with a faint light rim.</summary>
        public static VisualElement Glass(float pad = 14, float radius = PanelRadius, Color? bg = null)
        {
            var e = Box(bg ?? Theme.Panel, pad, radius);
            Border(e, new Color(1, 1, 1, 0.08f), 1);
            e.style.borderTopColor = new Color(1, 1, 1, 0.14f); // lit from above
            return e;
        }

        public static string Signed(int v) => v > 0 ? "+" + v : v.ToString();
    }
}
