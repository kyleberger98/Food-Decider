extends SceneTree
## Drives the real combat screen with synthetic mouse input (click-to-play and drag-and-drop).
## Needs a display: xvfb-run godot --path game -s res://tools/ui_smoke.gd

var screen
var failures := 0
var _last_pos := Vector2.ZERO


func _initialize() -> void:
	screen = load("res://ui/main.tscn").instantiate()
	root.add_child(screen)
	await _frames(2)
	screen.start_run(5)
	await _frames(2)
	var c: Combat = screen.combat

	# Click a unit card, then click an empty front slot.
	var unit_card := _first_in_hand(func(x): return x.type() == "unit")
	_check(unit_card != null, "starter hand has a unit card")
	var cp_before := c.cp
	await _click(_card_view(unit_card))
	_check(screen.selected == unit_card, "clicking a card selects it")
	await _click(screen._slots["0/front/1"])
	_check(c.field.get_unit(Battlefield.PLAYER, "front", 1) != null, "click deploys to the slot")
	_check(c.cp == cp_before - unit_card.cost(), "deploy spent CP")

	# Drag an ally-targeted order onto that unit.
	var order := _first_in_hand(func(x): return x.def.get("target") == "ally" and x.cost() <= c.cp)
	if order != null:
		var unit := c.field.get_unit(Battlefield.PLAYER, "front", 1)
		var hand_before := c.hand.size()
		await _drag(_card_view(order), screen._slots["0/front/1"])
		_check(c.hand.size() == hand_before - 1, "drag-and-drop played %s" % order.display_name())
		_check(unit.cover > 0 or c.events.any(func(e): return e["type"] == "damaged"), "order took effect")

	var turn := c.turn
	screen._on_end_turn()
	await _frames(2)
	_check(c.turn == turn + 1 or c.is_over(), "end turn advances")

	# Win the fight, pick a reward, and land in the next fight.
	for e in c.field.units(Battlefield.ENEMY):
		c._lose_hp(e, 999)
	screen._after_action()
	await _frames(2)
	_check(screen._overlay.visible, "victory screen shows")
	var rewards := _find_card_views(screen._overlay)
	_check(rewards.size() == 3, "three reward choices")
	var deck_before: int = screen.deck.size()
	if rewards.size() > 0:
		await _click(rewards[0])
	_check(screen.deck.size() == deck_before + 1, "reward added to deck")
	_check(not screen._overlay.visible and screen.combat != c, "next fight started")

	print("ui smoke: %s" % ("OK" if failures == 0 else "%d failures" % failures))
	quit(1 if failures > 0 else 0)


func _find_card_views(node: Node) -> Array:
	var out := []
	for child in node.get_children():
		if child is CardView:
			out.append(child)
		out.append_array(_find_card_views(child))
	return out


func _first_in_hand(pred: Callable) -> CardInstance:
	for card in screen.combat.hand:
		if pred.call(card):
			return card
	return null


func _card_view(card: CardInstance) -> Control:
	for v in screen._hand_box.get_children():
		if v is CardView and v.card == card:
			return v
	return null


func _center(ctrl: Control) -> Vector2:
	return ctrl.get_global_rect().get_center()


func _mouse(type: String, pos: Vector2, pressed := false) -> void:
	var e: InputEvent
	if type == "button":
		e = InputEventMouseButton.new()
		e.button_index = MOUSE_BUTTON_LEFT
		e.pressed = pressed
		e.button_mask = MOUSE_BUTTON_MASK_LEFT if pressed else 0
	else:
		e = InputEventMouseMotion.new()
		e.button_mask = MOUSE_BUTTON_MASK_LEFT if pressed else 0
		e.relative = pos - _last_pos  # Godot starts a drag from accumulated relative motion.
		_last_pos = pos
	e.position = pos
	e.global_position = pos
	Input.parse_input_event(e)
	Input.flush_buffered_events()


func _click(ctrl: Control) -> void:
	var p := _center(ctrl)
	_mouse("motion", p)
	await _frames(1)
	_mouse("button", p, true)
	await _frames(1)
	_mouse("button", p, false)
	await _frames(2)


func _drag(from: Control, to: Control) -> void:
	var a := _center(from)
	var b := _center(to)
	_mouse("motion", a)
	await _frames(1)
	_mouse("button", a, true)
	await _frames(1)
	for i in range(1, 11):
		_mouse("motion", a.lerp(b, i / 10.0), true)
		await _frames(1)
	_mouse("button", b, false)
	await _frames(2)


func _frames(n: int) -> void:
	for i in n:
		await process_frame


func _check(cond: bool, msg: String) -> void:
	print(("  ok   " if cond else "  FAIL ") + msg)
	if not cond:
		failures += 1
