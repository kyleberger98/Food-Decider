extends Control
## The fight screen. Drives one Combat; emits `finished` a moment after it ends.

signal finished(combat: Combat)

const END_DELAY := 1.4

var combat: Combat
var title := ""
var selected: CardInstance = null

var _slots := {}  # "side/row/slot" -> SlotView
var _event_cursor := 0
var _resolve_label: Label
var _cp_label: Label
var _turn_label: Label
var _piles_label: Label
var _doctrine_label: Label
var _hint_label: Label
var _end_turn_btn: Button
var _hand_box: HBoxContainer
var _log: RichTextLabel
var _fx_layer: Control
var _ended := false


func setup(c: Combat, fight_title: String) -> Control:
	combat = c
	title = fight_title
	return self


func _ready() -> void:
	_build_ui()
	_log.append_text("[b]%s[/b]\n" % title)
	_after_action()


# --- Input -----------------------------------------------------------------

func _unhandled_input(event: InputEvent) -> void:
	var cancel := event.is_action_pressed("ui_cancel")
	cancel = cancel or (event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_RIGHT)
	if cancel and selected != null:
		_select(null)
	elif event.is_action_pressed("ui_accept") and not combat.is_over():
		_on_end_turn()


func _on_card_pressed(view: CardView) -> void:
	if combat.is_over():
		return
	if selected != view.card and view.card.cost() > combat.cp:
		_flash_hint(_error_text("not_enough_cp"))
		return
	if selected == view.card:
		if _is_untargeted(view.card):
			_play(view.card, {})
		else:
			_select(null)
		return
	_select(view.card)


func _on_slot_pressed(view: SlotView) -> void:
	if selected != null and _can_target(selected, view):
		_play(selected, _target_for(selected, view))


func _can_target(card: CardInstance, view: SlotView) -> bool:
	if combat.phase != Combat.Phase.PLAYER or card.cost() > combat.cp:
		return false
	if _is_untargeted(card):
		return true
	var target := _target_for(card, view)
	return not target.is_empty() and combat.validate_target(card, target) == ""


func _drop_card(card: CardInstance, view: SlotView) -> void:
	_play(card, _target_for(card, view))


func _target_for(card: CardInstance, view: SlotView) -> Dictionary:
	if card.type() == "unit":
		return {"row": view.row, "slot": view.slot} if view.side == Battlefield.PLAYER else {}
	if _is_untargeted(card):
		return {}
	return {"unit": view.unit} if view.unit != null else {}


func _is_untargeted(card: CardInstance) -> bool:
	return card.type() != "unit" and card.def.get("target", "none") == "none"


func _select(card: CardInstance) -> void:
	selected = card
	_refresh()


func _play(card: CardInstance, target: Dictionary) -> void:
	var err := combat.play_card(card, target)
	if err != "":
		_flash_hint(_error_text(err))
		return
	selected = null
	_after_action()


func _on_end_turn() -> void:
	if combat.phase != Combat.Phase.PLAYER:
		return
	selected = null
	combat.end_turn()
	_after_action()


# --- State -> view -----------------------------------------------------------

func _after_action() -> void:
	var fx_delay := 0.0
	while _event_cursor < combat.events.size():
		var e := combat.events[_event_cursor]
		_event_cursor += 1
		var line := EventText.describe(e)
		if line != "":
			_log.append_text(line + "\n")
		fx_delay += _spawn_fx(e, fx_delay)
	_refresh()
	if combat.is_over():
		_on_fight_over()


func _refresh() -> void:
	_resolve_label.text = "Resolve %d/%d" % [combat.resolve, combat.max_resolve]
	_cp_label.text = "Command %d/%d" % [combat.cp, Combat.CP_PER_TURN]
	_turn_label.text = "Turn %d" % combat.turn
	_piles_label.text = "Draw %d   Discard %d   Expended %d" % [
		combat.draw_pile.size(), combat.discard_pile.size(), combat.expended.size()]
	var doctrine_names := combat.doctrines.map(func(c): return c.display_name())
	_doctrine_label.text = ("Doctrines: " + ", ".join(PackedStringArray(doctrine_names))) if not doctrine_names.is_empty() else ""
	_end_turn_btn.disabled = combat.phase != Combat.Phase.PLAYER

	for key in _slots:
		var view: SlotView = _slots[key]
		var u := combat.field.get_unit(view.side, view.row, view.slot)
		view.show_unit(u, combat.describe_intent(u) if u != null and view.side == Battlefield.ENEMY else "")
		view.set_highlight(selected != null and not _is_untargeted(selected) and _can_target(selected, view))

	_sync_hand()
	for cv in _hand_box.get_children():
		cv.set_state(cv.card.cost() <= combat.cp and combat.phase == Combat.Phase.PLAYER, cv.card == selected)
	# Overlap cards when the hand gets wide.
	var n := combat.hand.size()
	var room := size.x - 40.0
	var sep := 8.0 if n < 2 else minf(8.0, (room - n * CardView.SIZE.x) / (n - 1))
	_hand_box.add_theme_constant_override("separation", int(sep))

	_hint_label.text = _hint()


## Rebuilds hand views only when the hand changed, so a card being pressed/dragged isn't freed.
func _sync_hand() -> void:
	var shown := _hand_box.get_children().map(func(v): return v.card)
	if shown == Array(combat.hand):
		return
	for child in _hand_box.get_children():
		_hand_box.remove_child(child)
		child.queue_free()
	for card in combat.hand:
		var cv := CardView.new().setup(card)
		cv.pressed.connect(_on_card_pressed)
		_hand_box.add_child(cv)


func _hint() -> String:
	if combat.is_over():
		return ""
	if selected == null:
		return "Click or drag a card. Space: end turn."
	if _is_untargeted(selected):
		return "%s: click it again, or drop it on the battlefield, to play. Right-click to cancel." % selected.display_name()
	match selected.type():
		"unit":
			return "Deploy %s to a highlighted slot (%s row)." % [selected.display_name(), " or ".join(PackedStringArray(selected.def["unit"]["rows"]))]
	match selected.def.get("target"):
		"ally":
			return "%s: choose one of your units." % selected.display_name()
		"enemy":
			return "%s: choose an enemy." % selected.display_name()
	return ""


func _flash_hint(text: String) -> void:
	_hint_label.text = text
	_hint_label.add_theme_color_override("font_color", Palette.DAMAGE)
	var t := create_tween()
	t.tween_interval(1.2)
	t.tween_callback(func(): _hint_label.add_theme_color_override("font_color", Palette.TEXT_DIM))


func _error_text(err: String) -> String:
	return {
		"not_enough_cp": "Not enough Command Points.",
		"bad_row": "That unit can't deploy to that row.",
		"slot_taken": "That slot is occupied.",
		"need_ally": "Choose one of your units.",
		"need_enemy": "Choose an enemy.",
		"out_of_reach": "Direct fire can't reach the back row while their front row stands.",
		"wrong_row": "That unit is in the wrong row for this order.",
		"row_full": "No free slot in the other row.",
		"not_your_turn": "Wait for your turn.",
	}.get(err, err)


## Floating numbers for damage/heals/cover. Returns how much to stagger the next one.
func _spawn_fx(e: Dictionary, delay: float) -> float:
	var text := ""
	var color := Palette.TEXT
	var anchor: Control = null
	match e["type"]:
		"damaged":
			text = "-%d" % e["amount"] if e["amount"] > 0 else "Blocked"
			color = Palette.DAMAGE if e["amount"] > 0 else Palette.COVER
		"missed":
			text = "Miss"
			color = Palette.TEXT_DIM
		"healed":
			if e["amount"] <= 0:
				return 0.0
			text = "+%d" % e["amount"]
			color = Palette.HEAL
		"cover_gained":
			text = "+%d Cover" % e["amount"]
			color = Palette.COVER
		"hq_damaged":
			text = "-%d" % e["amount"]
			color = Palette.DAMAGE
			anchor = _resolve_label
		_:
			return 0.0
	if anchor == null:
		if not e.has("at"):
			return 0.0
		anchor = _slots.get("%d/%s/%d" % e["at"])
	if anchor == null:
		return 0.0
	var l := Palette.label(text, 22, color)
	l.add_theme_color_override("font_outline_color", Color.BLACK)
	l.add_theme_constant_override("outline_size", 6)
	l.modulate.a = 0.0
	_fx_layer.add_child(l)
	l.global_position = anchor.global_position + Vector2(anchor.size.x / 2 - 20, anchor.size.y / 3)
	var t := create_tween()
	t.tween_interval(delay)
	t.tween_property(l, "modulate:a", 1.0, 0.05)
	t.parallel().tween_property(l, "position:y", l.position.y - 40, 0.9)
	t.tween_property(l, "modulate:a", 0.0, 0.3)
	t.tween_callback(l.queue_free)
	return 0.12


# --- End of fight -------------------------------------------------------------

func _on_fight_over() -> void:
	if _ended:
		return
	_ended = true
	_end_turn_btn.disabled = true
	var won := combat.phase == Combat.Phase.WON
	_hint_label.text = "VICTORY" if won else "YOUR LINE HAS BROKEN"
	_hint_label.add_theme_color_override("font_color", Palette.HEAL if won else Palette.DAMAGE)
	_hint_label.add_theme_font_size_override("font_size", 24)
	await get_tree().create_timer(END_DELAY).timeout
	finished.emit(combat)


# --- Layout -------------------------------------------------------------------

func _build_ui() -> void:
	var bg := ColorRect.new()
	bg.color = Palette.BG
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(bg)

	var margin := MarginContainer.new()
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]:
		margin.add_theme_constant_override("margin_" + side, 12)
	add_child(margin)
	var root := VBoxContainer.new()
	root.add_theme_constant_override("separation", 8)
	margin.add_child(root)

	# Top bar
	var top := HBoxContainer.new()
	top.add_theme_constant_override("separation", 24)
	root.add_child(top)
	_resolve_label = Palette.label("", 20, Palette.TEXT)
	_cp_label = Palette.label("", 20, Palette.HIGHLIGHT)
	_turn_label = Palette.label("", 20, Palette.TEXT_DIM)
	_doctrine_label = Palette.label("", 14, Palette.COVER)
	for l in [_resolve_label, _cp_label, _turn_label, _doctrine_label]:
		top.add_child(l)
	var spacer := Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	top.add_child(spacer)
	_piles_label = Palette.label("", 14, Palette.TEXT_DIM)
	top.add_child(_piles_label)
	_end_turn_btn = Button.new()
	_end_turn_btn.text = "End Turn"
	_end_turn_btn.custom_minimum_size = Vector2(120, 36)
	_end_turn_btn.pressed.connect(_on_end_turn)
	top.add_child(_end_turn_btn)

	# Battlefield + log
	var middle := HBoxContainer.new()
	middle.size_flags_vertical = Control.SIZE_EXPAND_FILL
	middle.add_theme_constant_override("separation", 12)
	root.add_child(middle)
	var field := HBoxContainer.new()
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	field.alignment = BoxContainer.ALIGNMENT_CENTER
	field.add_theme_constant_override("separation", 10)
	middle.add_child(field)
	field.add_child(_column(Battlefield.PLAYER, Battlefield.BACK, "YOUR BACK ROW"))
	field.add_child(_column(Battlefield.PLAYER, Battlefield.FRONT, "YOUR FRONT ROW"))
	var gap := VSeparator.new()
	gap.custom_minimum_size = Vector2(24, 0)
	field.add_child(gap)
	field.add_child(_column(Battlefield.ENEMY, Battlefield.FRONT, "ENEMY FRONT ROW"))
	field.add_child(_column(Battlefield.ENEMY, Battlefield.BACK, "ENEMY BACK ROW"))

	var log_panel := PanelContainer.new()
	log_panel.custom_minimum_size = Vector2(250, 0)
	log_panel.add_theme_stylebox_override("panel", Palette.box(Palette.PANEL_DARK))
	middle.add_child(log_panel)
	_log = RichTextLabel.new()
	_log.bbcode_enabled = true
	_log.scroll_following = true
	_log.add_theme_font_size_override("normal_font_size", 12)
	_log.add_theme_font_size_override("bold_font_size", 12)
	_log.add_theme_color_override("default_color", Palette.TEXT_DIM)
	log_panel.add_child(_log)

	_hint_label = Palette.label("", 14, Palette.TEXT_DIM)
	_hint_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	root.add_child(_hint_label)

	_hand_box = HBoxContainer.new()
	_hand_box.alignment = BoxContainer.ALIGNMENT_CENTER
	_hand_box.custom_minimum_size = Vector2(0, CardView.SIZE.y)
	root.add_child(_hand_box)

	_fx_layer = Control.new()
	_fx_layer.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_fx_layer.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_fx_layer)


func _column(side: int, row: String, title: String) -> VBoxContainer:
	var col := VBoxContainer.new()
	col.alignment = BoxContainer.ALIGNMENT_CENTER
	col.add_theme_constant_override("separation", 8)
	var t := Palette.label(title, 11, Palette.PLAYER if side == Battlefield.PLAYER else Palette.ENEMY)
	t.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	col.add_child(t)
	for i in Battlefield.ROW_SIZE[row]:
		var view := SlotView.new().setup(side, row, i)
		view.can_drop = _can_target
		view.on_drop = _drop_card
		view.pressed.connect(_on_slot_pressed)
		_slots["%d/%s/%d" % [side, row, i]] = view
		col.add_child(view)
	return col
