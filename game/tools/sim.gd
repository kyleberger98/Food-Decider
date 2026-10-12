extends SceneTree
## Balance smoke test: a simple greedy bot plays the starter deck against each encounter.
## godot --headless --path game -s res://tools/sim.gd [-- runs]

const MAX_TURNS := 40


func _init() -> void:
	var args := OS.get_cmdline_user_args()
	var runs := int(args[0]) if args.size() > 0 else 200
	print("%-12s %6s %8s %10s %9s" % ["encounter", "win%", "turns", "resolve", "deaths"])
	for enc in CardDB.encounters():
		var wins := 0
		var turns := 0
		var resolve_left := 0
		var deaths := 0
		for seed_value in runs:
			var deck := CardDB.starter_deck()
			var c := Combat.from_encounter(deck, enc, 60, 60, seed_value)
			while not c.is_over() and c.turn <= MAX_TURNS:
				_play_turn(c)
				c.end_turn()
			if c.phase == Combat.Phase.WON:
				wins += 1
				resolve_left += c.resolve
			turns += c.turn
			for card in deck:
				deaths += card.casualties
		print("%-12s %5.0f%% %8.1f %10.1f %9.2f" % [enc, 100.0 * wins / runs, float(turns) / runs,
			float(resolve_left) / maxi(1, wins), float(deaths) / runs])
	quit()


## Greedy: deploy units (front first), cover the most hurt front unit, then fire with the strongest unit.
func _play_turn(c: Combat) -> void:
	var progress := true
	while progress and not c.is_over():
		progress = false
		for card in _by_priority(c.hand):
			var target: Variant = _choose_target(c, card)
			if target != null and c.play_card(card, target) == "":
				progress = true
				break


func _by_priority(hand: Array[CardInstance]) -> Array[CardInstance]:
	var order := {"unit": 0, "doctrine": 1, "order": 2}
	var sorted := hand.duplicate()
	sorted.sort_custom(func(a, b): return order.get(a.type(), 3) < order.get(b.type(), 3))
	return sorted


func _choose_target(c: Combat, card: CardInstance) -> Variant:
	var d := card.def
	if card.type() == "unit":
		for row in d["unit"]["rows"]:
			var s := c.field.first_free(Battlefield.PLAYER, row)
			if s >= 0:
				return {"row": row, "slot": s}
		return null
	match d.get("target", "none"):
		"ally":
			var mine := c.field.units(Battlefield.PLAYER)
			if mine.is_empty():
				return null
			var best: Unit = mine[0]
			for u in mine:
				if _ally_score(card, u) > _ally_score(card, best):
					best = u
			return {"unit": best}
		"enemy":
			var foes := c.field.legal_targets("indirect", Battlefield.ENEMY)
			return {"unit": foes[0]} if not foes.is_empty() else null
	return {}


func _ally_score(card: CardInstance, u: Unit) -> float:
	if card.id == "dig_in":
		return (10.0 if u.row == Battlefield.FRONT else 0.0) - u.hp - u.cover
	return u.firepower * u.shots
