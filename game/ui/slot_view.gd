class_name SlotView
extends PanelContainer
## One battlefield slot. Shows its unit (HP, Cover, statuses, enemy intent) and accepts card drops.

signal pressed(view: SlotView)

const SIZE := Vector2(184, 104)

var side: int
var row: String
var slot: int
var unit: Unit = null
## Set by the combat screen: can_drop(card, view) -> bool, on_drop(card, view).
var can_drop: Callable
var on_drop: Callable

var _highlight := false
var _intent: Label
var _name: Label
var _hp_bar: ProgressBar
var _info: Label


func setup(s: int, r: String, i: int) -> SlotView:
	side = s
	row = r
	slot = i
	custom_minimum_size = SIZE
	mouse_filter = Control.MOUSE_FILTER_STOP
	var v := VBoxContainer.new()
	v.add_theme_constant_override("separation", 1)
	add_child(v)
	_intent = Palette.label("", 12, Palette.HIGHLIGHT)
	_name = Palette.label("", 14)
	_hp_bar = ProgressBar.new()
	_hp_bar.show_percentage = false
	_hp_bar.custom_minimum_size = Vector2(0, 14)
	_hp_bar.add_theme_stylebox_override("background", Palette.box(Palette.PANEL_DARK, Color.TRANSPARENT, 0, 3))
	_info = Palette.label("", 12, Palette.TEXT_DIM)
	_info.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	for c in [_intent, _name, _hp_bar, _info]:
		c.mouse_filter = Control.MOUSE_FILTER_IGNORE
		v.add_child(c)
	v.mouse_filter = Control.MOUSE_FILTER_IGNORE
	show_unit(null, "")
	return self


func show_unit(u: Unit, intent_text: String) -> void:
	unit = u
	_intent.visible = u != null and side == Battlefield.ENEMY
	_intent.text = "Intent: " + intent_text
	_hp_bar.visible = u != null
	if u == null:
		_name.text = row.capitalize()
		_name.add_theme_color_override("font_color", Palette.TEXT_DIM)
		_info.text = ""
	else:
		_name.text = u.name
		_name.add_theme_color_override("font_color", Palette.TEXT)
		_hp_bar.max_value = u.max_hp
		_hp_bar.value = u.hp
		var fill := Palette.PLAYER if side == Battlefield.PLAYER else Palette.ENEMY
		_hp_bar.add_theme_stylebox_override("fill", Palette.box(fill, Color.TRANSPARENT, 0, 3))
		_info.text = _info_text(u)
	_update_style()


func set_highlight(on: bool) -> void:
	_highlight = on
	_update_style()


func _info_text(u: Unit) -> String:
	var parts: PackedStringArray = ["HP %d/%d" % [u.hp, u.max_hp]]
	if u.firepower > 0:
		parts.append("%s %d%s" % [u.attack.capitalize(), u.firepower, ("x%d" % u.shots) if u.shots > 1 else ""])
	if u.cover > 0:
		parts.append("Cover %d" % u.cover)
	if u.armor > 0:
		parts.append("Armor %d" % u.armor)
	for s in u.statuses:
		parts.append("%s %d" % [String(s).capitalize(), u.statuses[s]])
	return "  ".join(parts)


func _update_style() -> void:
	var accent := Palette.PLAYER if side == Battlefield.PLAYER else Palette.ENEMY
	var bg := Palette.PANEL if unit != null else Palette.PANEL_DARK
	var border := Palette.VALID if _highlight else (accent if unit != null else Palette.PANEL)
	add_theme_stylebox_override("panel", Palette.box(bg, border, 4 if _highlight else 2, 6))


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		pressed.emit(self)


func _can_drop_data(_at: Vector2, data: Variant) -> bool:
	return data is Dictionary and data.has("card") and can_drop.is_valid() and can_drop.call(data["card"], self)


func _drop_data(_at: Vector2, data: Variant) -> void:
	on_drop.call(data["card"], self)
