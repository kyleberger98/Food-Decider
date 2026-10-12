class_name CardView
extends PanelContainer
## A card in hand (or in a reward choice). Click to select, or drag onto a target.

signal pressed(view: CardView)

const SIZE := Vector2(132, 184)

var card: CardInstance
var playable := true
var selected := false
var draggable := true


func setup(c: CardInstance) -> CardView:
	card = c
	custom_minimum_size = SIZE
	mouse_filter = Control.MOUSE_FILTER_STOP
	tooltip_text = c.def["text"]
	var v := VBoxContainer.new()
	v.mouse_filter = Control.MOUSE_FILTER_IGNORE
	v.add_theme_constant_override("separation", 2)
	add_child(v)

	var header := HBoxContainer.new()
	header.mouse_filter = Control.MOUSE_FILTER_IGNORE
	v.add_child(header)
	var cost := Palette.label(str(c.cost()), 20, Palette.TYPE_COLORS.get(c.type(), Palette.INK))
	cost.custom_minimum_size = Vector2(18, 0)
	header.add_child(cost)
	var name_l := Palette.label(c.display_name(), 13, Palette.INK)
	name_l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	name_l.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(name_l)

	var type_l := Palette.label(c.type().to_upper(), 10, Palette.TYPE_COLORS.get(c.type(), Palette.INK))
	v.add_child(type_l)

	var text := Palette.label(c.def["text"], 12, Palette.INK)
	text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	text.size_flags_vertical = Control.SIZE_EXPAND_FILL
	v.add_child(text)

	if c.casualties > 0:
		v.add_child(Palette.label("Casualties %d/%d" % [c.casualties, Combat.MAX_CASUALTIES], 10, Palette.ENEMY))
	for child in v.get_children() + header.get_children():
		if child is Control:
			child.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_update_style()
	return self


func set_state(is_playable: bool, is_selected: bool) -> void:
	playable = is_playable
	selected = is_selected
	_update_style()


func _update_style() -> void:
	var border: Color = Palette.HIGHLIGHT if selected else Palette.TYPE_COLORS.get(card.type(), Palette.INK)
	add_theme_stylebox_override("panel", Palette.box(Palette.PAPER, border, 4 if selected else 3, 8))
	modulate = Color.WHITE if playable else Color(0.6, 0.6, 0.6)


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		pressed.emit(self)


func _get_drag_data(_at: Vector2) -> Variant:
	if not draggable or not playable:
		return null
	var holder := Control.new()
	var preview := CardView.new().setup(card)
	preview.modulate.a = 0.85
	preview.position = -SIZE / 2
	holder.add_child(preview)
	set_drag_preview(holder)
	return {"card": card}
