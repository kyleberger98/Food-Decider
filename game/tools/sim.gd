extends SceneTree
## Balance smoke test with a simple greedy bot.
##   godot --headless --path game -s res://tools/sim.gd -- fights [n]   # starter deck vs every encounter
##   godot --headless --path game -s res://tools/sim.gd -- runs [n]     # full runs through the map

const MAX_TURNS := 40


func _init() -> void:
	var args := OS.get_cmdline_user_args()
	var mode: String = args[0] if args.size() > 0 else "fights"
	var n := int(args[1]) if args.size() > 1 else 200
	if mode == "runs":
		_sim_runs(n)
	else:
		_sim_fights(n)
	quit()


func _sim_fights(runs: int) -> void:
	print("%-14s %6s %8s %10s %9s" % ["encounter", "win%", "turns", "resolve", "deaths"])
	for enc in CardDB.encounters():
		var wins := 0
		var turns := 0
		var resolve_left := 0
		var deaths := 0
		for seed_value in runs:
			var deck := CardDB.starter_deck()
			var c := Combat.from_encounter(deck, enc, 60, 60, seed_value, ["cib"])
			_fight(c)
			if c.phase == Combat.Phase.WON:
				wins += 1
				resolve_left += c.resolve
			turns += c.turn
			for card in deck:
				deaths += card.casualties
		print("%-14s %5.0f%% %8.1f %10.1f %9.2f" % [enc, 100.0 * wins / runs, float(turns) / runs,
			float(resolve_left) / maxi(1, wins), float(deaths) / runs])


func _fight(c: Combat) -> void:
	while not c.is_over() and c.turn <= MAX_TURNS:
		_play_turn(c)
		c.end_turn()
	if not c.is_over():
		c._hit_hq(9999, null)  # stalemate counts as a loss


func _sim_runs(runs: int) -> void:
	var wins := 0
	var floors := 0
	var cards_lost := 0
	var killers := {}
	for seed_value in runs:
		var run := RunState.create(seed_value)
		var last_enc := ""
		while not run.is_over():
			var options := run.available_nodes()
			var node: RunMap.MapNode = options[run.rng.randi() % options.size()]
			for o in options:
				if o.type == "aid" and run.resolve < run.max_resolve / 2:
					node = o
			run.enter(node)
			match node.type:
				"skirmish", "elite", "boss":
					var tier := run.tier_for(node.type)
					last_enc = run.pick_encounter(tier)
					var c := run.start_combat(last_enc)
					_fight(c)
					var r := run.finish_combat(c, tier)
					cards_lost += r["lost_cards"].size()
					if not r["cards"].is_empty():
						run.add_card(r["cards"][0])
				"aid":
					var worn := run.deck.filter(func(x): return x.casualties >= 2)
					if run.resolve < run.max_resolve * 0.6:
						run.rest_heal()
					elif not worn.is_empty():
						run.rotate_troops(worn[0])
					else:
						var fresh := run.deck.filter(func(x): return not x.upgraded)
						if not fresh.is_empty():
							run.promote(fresh[0])
				"quartermaster":
					var stock := run.shop_stock()
					for item in stock["cards"]:
						if run.buy_card(item["id"], item["price"]):
							break
				"intel":
					var ev := run.pick_event()
					var choices: Array = CardDB.events()[ev]["choices"]
					for i in choices.size():
						if run.choice_available(choices[i]):
							run.apply_event_choice(ev, i)
							break
		if run.won:
			wins += 1
		else:
			killers[last_enc] = killers.get(last_enc, 0) + 1
		floors += run.floor_number()
	print("runs %d  win %.0f%%  avg floor %.1f  cards lost/run %.2f" % [runs, 100.0 * wins / runs, float(floors) / runs, float(cards_lost) / runs])
	print("deaths by encounter: ", killers)


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
