using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Crucible.View.UI
{
    /// <summary>Colour tokens for the HUD. Dark translucent chrome; yields keep their familiar Civ-style hues.</summary>
    public static class Theme
    {
        public static readonly Color Chrome = new Color(0.06f, 0.07f, 0.09f, 0.90f);
        public static readonly Color Panel = new Color(0.10f, 0.11f, 0.14f, 0.96f);
        public static readonly Color Card = new Color(0.15f, 0.16f, 0.20f, 1f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Text = new Color(0.93f, 0.92f, 0.88f);
        public static readonly Color Muted = new Color(0.62f, 0.64f, 0.68f);
        public static readonly Color Accent = new Color(0.91f, 0.72f, 0.32f);
        public static readonly Color AccentDark = new Color(0.55f, 0.40f, 0.14f);
        public static readonly Color Good = new Color(0.44f, 0.80f, 0.46f);
        public static readonly Color Bad = new Color(0.90f, 0.38f, 0.33f);
        public static readonly Color War = new Color(0.96f, 0.52f, 0.25f);
        public static readonly Color Info = new Color(0.52f, 0.70f, 1f);
        public static readonly Color Button = new Color(0.20f, 0.22f, 0.27f);
        public static readonly Color ButtonHover = new Color(0.29f, 0.32f, 0.39f);
        public static readonly Color Track = new Color(1f, 1f, 1f, 0.10f);

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
            l.style.fontSize = size;
            l.style.color = color ?? Theme.Text;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.whiteSpace = wrap ? WhiteSpace.Normal : WhiteSpace.NoWrap;
            Pad(l, 0);
            l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0;
            return l;
        }

        public static Label Heading(string text) => Text(text, 15, Theme.Text, bold: true);

        public static Label Caption(string text) => Text(text.ToUpperInvariant(), 10, Theme.Muted, bold: true);

        public static Button Btn(string text, Action onClick, ButtonStyle style = ButtonStyle.Normal, bool enabled = true, float size = 12)
        {
            var b = new Button(onClick) { text = text };
            Color bg = style == ButtonStyle.Primary ? Theme.Accent
                : style == ButtonStyle.Danger ? new Color(0.55f, 0.20f, 0.18f)
                : style == ButtonStyle.Ghost ? new Color(0, 0, 0, 0)
                : Theme.Button;
            Color hover = style == ButtonStyle.Primary ? new Color(1f, 0.82f, 0.45f)
                : style == ButtonStyle.Danger ? new Color(0.70f, 0.27f, 0.23f)
                : Theme.ButtonHover;
            b.style.backgroundColor = bg;
            b.style.color = style == ButtonStyle.Primary ? new Color(0.12f, 0.09f, 0.03f) : Theme.Text;
            b.style.fontSize = size;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.paddingLeft = b.style.paddingRight = 10;
            b.style.paddingTop = b.style.paddingBottom = 5;
            b.style.marginLeft = b.style.marginRight = b.style.marginTop = b.style.marginBottom = 0;
            Radius(b, 5);
            Border(b, style == ButtonStyle.Ghost ? Theme.Line : new Color(0, 0, 0, 0), 1);
            b.RegisterCallback<PointerEnterEvent>(_ => { if (b.enabledSelf) b.style.backgroundColor = hover; });
            b.RegisterCallback<PointerLeaveEvent>(_ => b.style.backgroundColor = bg);
            b.SetEnabled(enabled);
            if (!enabled) b.style.opacity = 0.4f;
            return b;
        }

        /// <summary>A horizontal progress bar (fraction clamped to 0…1).</summary>
        public static VisualElement Bar(float fraction, Color fill, float height = 6, Color? track = null)
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
            var p = Box(bg, 0, 8);
            p.style.paddingLeft = p.style.paddingRight = 7;
            p.style.paddingTop = p.style.paddingBottom = 2;
            p.Add(Text(text, size, fg ?? new Color(0.08f, 0.08f, 0.1f), bold: true));
            return p;
        }

        /// <summary>A yield/stat chip: coloured dot, value, and a small caption.</summary>
        public static VisualElement Stat(string caption, string value, Color color, string tooltip = null)
        {
            var row = Row();
            row.style.marginRight = 14;
            var dot = Box(color, 0, 5);
            dot.style.width = dot.style.height = 10;
            dot.style.marginRight = 6;
            row.Add(dot);
            var col = Col();
            col.Add(Text(value, 14, Theme.Text, bold: true));
            col.Add(Text(caption, 9, Theme.Muted));
            row.Add(col);
            if (tooltip != null) row.tooltip = tooltip;
            return row;
        }

        public static VisualElement Divider()
        {
            var d = Box(Theme.Line);
            d.style.height = 1;
            d.style.marginTop = d.style.marginBottom = 8;
            return d;
        }

        public static VisualElement Spacer()
        {
            var s = new VisualElement();
            s.style.flexGrow = 1;
            return s;
        }

        public static VisualElement Card(float pad = 10)
        {
            var c = Box(Theme.Card, pad, 8);
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

        public static string Signed(int v) => v > 0 ? "+" + v : v.ToString();
    }
}
