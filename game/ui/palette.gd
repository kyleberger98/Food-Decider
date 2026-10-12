class_name Palette
extends RefCounted
## Colors and style helpers for the placeholder UI.

const BG := Color("23271e")
const PANEL := Color("343a2b")
const PANEL_DARK := Color("2a2e22")
const TEXT := Color("e8e2cf")
const TEXT_DIM := Color("a39d88")
const PAPER := Color("e3d9bb")
const INK := Color("2b2618")
const PLAYER := Color("6f8f4f")
const ENEMY := Color("9a4b43")
const HIGHLIGHT := Color("d9b44a")
const VALID := Color("fff3a0")
const COVER := Color("6fa3c9")
const DAMAGE := Color("ff6b5a")
const HEAL := Color("8fd16a")
const TYPE_COLORS := {"unit": Color("5f7a43"), "order": Color("8a5a32"), "doctrine": Color("4d6a85"), "status": Color("555555")}


static func box(bg: Color, border := Color.TRANSPARENT, border_w := 0, radius := 6) -> StyleBoxFlat:
	var sb := StyleBoxFlat.new()
	sb.bg_color = bg
	sb.border_color = border
	sb.set_border_width_all(border_w)
	sb.set_corner_radius_all(radius)
	sb.set_content_margin_all(6)
	return sb


static func label(text := "", size := 14, color := TEXT) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", color)
	return l
