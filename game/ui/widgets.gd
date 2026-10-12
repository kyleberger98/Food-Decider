class_name Widgets
extends RefCounted
## Small builders shared by the run screens.


## A full-rect background (optional) with a centered column; returns the column.
static func centered_column(parent: Control, separation := 16, with_bg := true) -> VBoxContainer:
	if with_bg:
		var bg := ColorRect.new()
		bg.color = Palette.BG
		bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		parent.add_child(bg)
	var center := CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	parent.add_child(center)
	var col := VBoxContainer.new()
	col.add_theme_constant_override("separation", separation)
	center.add_child(col)
	return col


static func title(text: String, color := Palette.TEXT, size := 32) -> Label:
	var l := Palette.label(text, size, color)
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	return l


static func body(text: String, color := Palette.TEXT_DIM, width := 640.0) -> Label:
	var l := Palette.label(text, 16, color)
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	l.custom_minimum_size = Vector2(width, 0)
	return l


static func button(text: String, action: Callable, min_width := 260.0) -> Button:
	var b := Button.new()
	b.text = text
	b.custom_minimum_size = Vector2(min_width, 40)
	b.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	b.pressed.connect(action)
	return b


static func row(separation := 16) -> HBoxContainer:
	var h := HBoxContainer.new()
	h.alignment = BoxContainer.ALIGNMENT_CENTER
	h.add_theme_constant_override("separation", separation)
	return h


static func relic_text(id: String) -> String:
	var r: Dictionary = CardDB.relics()[id]
	return "%s: %s" % [r["name"], r["text"]]
