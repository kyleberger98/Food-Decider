extends TestCase


func _rng(seed_value: int) -> RandomNumberGenerator:
	var r := RandomNumberGenerator.new()
	r.seed = seed_value
	return r


func test_map_structure() -> void:
	for s in 30:
		var m := RunMap.generate(_rng(s))
		eq(m.rows.size(), RunMap.FLOORS)
		check(m.rows[0].size() >= 2, "at least two starting choices")
		for node in m.rows[0]:
			eq(node.type, "skirmish")
		for node in m.rows[RunMap.FLOORS - 1]:
			eq(node.type, "aid")
			check(node.next.size() == 1 and node.next[0] == m.boss, "last floor leads to the boss")
		for f in RunMap.FLOORS - 1:
			for node in m.rows[f]:
				check(not node.next.is_empty(), "every node leads somewhere")
				for n in node.next:
					eq(n.row, f + 1)
					check(absi(n.col - node.col) <= 1, "paths move at most one column")
				if f < RunMap.MIN_FLOOR["elite"]:
					check(node.type != "elite" and node.type != "aid", "no elites/rests early")


func test_every_node_reachable_from_start() -> void:
	var m := RunMap.generate(_rng(3))
	var seen := {}
	var frontier: Array = m.rows[0].duplicate()
	while not frontier.is_empty():
		var n = frontier.pop_back()
		if seen.has(n):
			continue
		seen[n] = true
		frontier.append_array(n.next)
	eq(seen.size(), m.all_nodes().size())


func test_new_run_defaults() -> void:
	var run := RunState.create(1)
	eq(run.deck.size(), 10)
	eq(run.resolve, RunState.START_RESOLVE)
	eq(run.supply, RunState.START_SUPPLY)
	eq(run.relics, ["cib"])
	eq(run.available_nodes(), run.map.rows[0])
	eq(run.floor_number(), 0)


func test_enter_moves_along_paths() -> void:
	var run := RunState.create(2)
	var first: RunMap.MapNode = run.available_nodes()[0]
	run.enter(first)
	eq(run.floor_number(), 1)
	check(first.visited)
	eq(run.available_nodes(), first.next)
	eq(run.tier_for("skirmish"), "easy")


func test_winning_a_fight_gives_rewards() -> void:
	var run := RunState.create(4)
	run.enter(run.available_nodes()[0])
	var c := run.start_combat(run.pick_encounter("easy"))
	for e in c.field.units(Battlefield.ENEMY):
		c._lose_hp(e, 999)
	var r := run.finish_combat(c, "easy")
	eq(run.fights_won, 1)
	check(r["supply"] >= 12 and r["supply"] <= 20)
	eq(run.supply, RunState.START_SUPPLY + r["supply"])
	eq(r["cards"].size(), 3)
	eq(r["relic"], "")


func test_elite_gives_a_commendation_and_boss_wins_run() -> void:
	var run := RunState.create(5)
	var c := run.start_combat("bunker_line")
	for e in c.field.units(Battlefield.ENEMY):
		c._lose_hp(e, 999)
	var r := run.finish_combat(c, "elite")
	check(r["relic"] != "" and run.relics.has(r["relic"]))
	var boss := run.start_combat("tiger")
	for e in boss.field.units(Battlefield.ENEMY):
		boss._lose_hp(e, 999)
	run.finish_combat(boss, "boss")
	check(run.won and run.is_over())


func test_losing_a_fight_ends_run_and_casualties_carry() -> void:
	var run := RunState.create(6)
	var veteran := run.deck[0]
	veteran.casualties = Combat.MAX_CASUALTIES
	var c := run.start_combat("patrol")
	c._hit_hq(999, null)
	run.finish_combat(c, "easy")
	check(run.lost)
	check(not run.deck.has(veteran), "card at the casualty limit is removed")


func test_purple_heart_heals_after_combat() -> void:
	var run := RunState.create(7)
	run.relics.append("purple_heart")
	run.resolve = 30
	var c := run.start_combat("patrol")
	for e in c.field.units(Battlefield.ENEMY):
		c._lose_hp(e, 999)
	run.finish_combat(c, "easy")
	eq(run.resolve, 36)


func test_aid_station_options() -> void:
	var run := RunState.create(8)
	run.resolve = 10
	eq(run.rest_heal(), 18)
	eq(run.resolve, 28)
	var card := run.deck[0]
	card.casualties = 2
	var up := run.promote(card)
	check(up.upgraded and run.deck.has(up) and not run.deck.has(card))
	eq(up.casualties, 2, "promotion keeps casualties")
	run.rotate_troops(up)
	eq(up.casualties, 0)


func test_shop() -> void:
	var run := RunState.create(9)
	var stock := run.shop_stock()
	eq(stock["cards"].size(), 5)
	var item: Dictionary = stock["cards"][0]
	run.supply = item["price"]
	check(run.buy_card(item["id"], item["price"]))
	eq(run.deck.size(), 11)
	eq(run.supply, 0)
	check(not run.buy_card(item["id"], item["price"]), "can't buy without supply")
	run.supply = 100
	check(run.remove_card(run.deck[0], RunState.REMOVE_PRICE))
	eq(run.deck.size(), 10)
	eq(run.supply, 25)


func test_events() -> void:
	var run := RunState.create(10)
	run.supply = 10
	var choices: Array = CardDB.events()["resistance"]["choices"]
	check(not run.choice_available(choices[0]), "can't pay 50 with 10 supply")
	run.resolve = 40
	run.apply_event_choice("resistance", 1)
	eq(run.resolve, 50)
	run.apply_event_choice("stragglers", 0)
	eq(run.deck.size(), 11)
	run.deck[0].casualties = 2
	run.supply = 30
	run.apply_event_choice("field_hospital", 0)
	eq(run.deck[0].casualties, 0)
	eq(run.supply, 0)


func test_event_damage_can_end_run() -> void:
	var run := RunState.create(11)
	run.resolve = 3
	run.apply_event_choice("stragglers", 1)
	check(run.lost)


func test_encounter_pick_avoids_repeats() -> void:
	var run := RunState.create(12)
	var a := run.pick_encounter("normal")
	for i in 10:
		var b := run.pick_encounter("normal")
		check(a != b, "same fight twice in a row")
		a = b


func test_new_event_ops() -> void:
	var run := RunState.create(13)
	run.apply_event_choice("mail_call", 1)
	eq(run.deck.filter(func(c): return c.upgraded).size(), 1)
	run.apply_event_choice("liberated_village", 0)
	eq(run.max_resolve, RunState.START_RESOLVE + 6)


func test_mess_kit() -> void:
	var run := RunState.create(14)
	var base := run.rest_heal_amount()
	run.relics.append("mess_kit")
	eq(run.rest_heal_amount(), base + 15)


func test_enemy_hp_scales_with_floor() -> void:
	var run := RunState.create(15)
	eq(run.enemy_hp_mult("patrol"), 1.0, "no scaling before the first node")
	for i in 5:
		run.enter(run.available_nodes()[0])
	var c := run.start_combat("patrol")
	var g: Unit = c.field.units(Battlefield.ENEMY)[0]
	eq(g.max_hp, int(round(CardDB.enemies()["grenadier"]["hp"] * (1.0 + RunState.HP_SCALING_PER_FLOOR * 4))))
	eq(run.enemy_hp_mult("tiger"), 1.0, "boss doesn't scale")
