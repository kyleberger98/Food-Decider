extends TestCase

const ONE_GRENADIER := {"front": ["t_grunt"]}


func test_combat_starts_with_hand_and_cp() -> void:
	var c := Combat.from_encounter(CardDB.starter_deck(), "patrol", 60, 60, 7)
	eq(c.hand.size(), 5)
	eq(c.draw_pile.size(), 5)
	eq(c.cp, 3)
	eq(c.turn, 1)
	eq(c.field.units(Battlefield.ENEMY).size(), 2)
	check(not c.field.units(Battlefield.ENEMY)[0].intent.is_empty(), "enemies show an intent")


func test_same_seed_same_draws() -> void:
	var a := Combat.from_encounter(CardDB.starter_deck(), "patrol", 60, 60, 42)
	var b := Combat.from_encounter(CardDB.starter_deck(), "patrol", 60, 60, 42)
	eq(a.hand.map(func(x): return x.id), b.hand.map(func(x): return x.id))


func test_deploy_unit_spends_cp() -> void:
	var c := make_combat(ONE_GRENADIER)
	var card := give(c, "rifle_squad")
	eq(c.play_card(card, {"row": "front", "slot": 1}), "")
	eq(c.cp, 2)
	var u := c.field.get_unit(Battlefield.PLAYER, "front", 1)
	check(u != null and u.card == card)
	check(c.deployed.has(card))
	check(not c.hand.has(card))


func test_deploy_rejects_bad_row_and_cost() -> void:
	var c := make_combat(ONE_GRENADIER)
	var mortar := give(c, "mortar_team")
	eq(c.play_card(mortar, {"row": "front", "slot": 0}), "bad_row")
	c.cp = 1
	eq(c.play_card(mortar, {"row": "back", "slot": 0}), "not_enough_cp")
	check(c.hand.has(mortar), "failed play leaves the card in hand")


func test_front_row_bonus_and_lane_fire() -> void:
	var c := make_combat({"front": ["t_mg"]})
	var rifle := deploy(c, "rifle_squad", "front", 0)
	var mg := enemy(c)
	eq(c.attack_damage(rifle, rifle.firepower, "direct", mg), 5, "3 + front bonus 2")
	c.field.move(rifle, "back")
	eq(c.attack_damage(rifle, rifle.firepower, "direct", mg), 3, "no bonus from back row")


func test_dig_in_cover_absorbs_damage() -> void:
	var c := make_combat(ONE_GRENADIER)
	var rifle := deploy(c, "rifle_squad")
	eq(c.play_card(give(c, "dig_in"), {"unit": rifle}), "")
	eq(rifle.cover, 5)
	c.end_turn()  # grenadier attacks for 3 (rifle fires first and doesn't kill it: 8hp - 5)
	eq(rifle.hp, rifle.max_hp, "cover took the hit")
	eq(enemy(c).hp, 3)
	eq(rifle.cover, 0, "cover resets at the start of your turn")


func test_enemy_hits_hq_when_line_is_empty() -> void:
	var c := make_combat(ONE_GRENADIER)
	c.end_turn()
	eq(c.resolve, 57)


func test_smoke_makes_direct_fire_miss() -> void:
	var c := make_combat(ONE_GRENADIER)
	var rifle := deploy(c, "rifle_squad")
	c.play_card(give(c, "smoke_screen"))
	c.end_turn()
	eq(rifle.hp, rifle.max_hp)
	check(c.events.any(func(e): return e["type"] == "missed"))


func test_unit_death_returns_card_and_counts_casualty() -> void:
	var c := make_combat({"front": ["t_mg"]})
	var medic := deploy(c, "medic")  # 4 HP vs 3x2 direct
	var card := medic.card
	c.end_turn()
	check(not medic.is_on_field())
	check(c.discard_pile.has(card), "dead unit's card goes to discard")
	check(not c.deployed.has(card))
	eq(card.casualties, 1)


func test_card_lost_after_max_casualties() -> void:
	var c := make_combat(ONE_GRENADIER)
	var card := CardDB.make_card("rifle_squad")
	card.casualties = Combat.MAX_CASUALTIES - 1
	check(c.lost_cards().is_empty())
	c.hand.append(card)
	c.play_card(card, {"row": "front", "slot": 0})
	var u := c.field.get_unit(Battlefield.PLAYER, "front", 0)
	c._lose_hp(u, 99)
	eq(c.lost_cards(), [card] as Array[CardInstance])


func test_killing_all_enemies_wins() -> void:
	var c := make_combat(ONE_GRENADIER)
	var g := give(c, "grenade", true)  # 9 indirect vs 8 HP
	eq(c.play_card(g, {"unit": enemy(c)}), "")
	eq(c.phase, Combat.Phase.WON)
	check(c.is_over())


func test_orders_respect_reach() -> void:
	var c := make_combat({"front": ["t_grunt"], "back": ["t_mortar"]})
	var mortar := enemy(c, "back", 0)
	# Grenade is Indirect, so it can reach the back row.
	eq(c.play_card(give(c, "grenade"), {"unit": mortar}), "")
	eq(mortar.hp, 1)


func test_suppressed_and_exposed_modifiers() -> void:
	var c := make_combat(ONE_GRENADIER)
	var g := enemy(c)
	g.add_status("suppressed", 1)
	eq(c.attack_damage(g, 4, "direct", null), 3, "suppressed: 25% less, floored")
	var rifle := deploy(c, "rifle_squad", "back")
	rifle.add_status("exposed", 1)
	eq(c.attack_damage(null, 4, "indirect", rifle), 6, "exposed: +50%")


func test_armor_and_anti_armor() -> void:
	var c := make_combat(ONE_GRENADIER)
	var g := enemy(c)
	g.armor = 2
	var rifle := deploy(c, "rifle_squad", "back")
	eq(c.attack_damage(rifle, 3, "direct", g), 1, "armor reduces direct fire")
	eq(c.attack_damage(rifle, 3, "indirect", g), 3, "armor ignores indirect")
	var bazooka := deploy(c, "bazooka", "back", 1)
	eq(c.attack_damage(bazooka, 6, "direct", g), 9, "anti-armor ignores armor, +50%")


func test_suppressing_fire_hits_enemy_front_row() -> void:
	var c := make_combat({"front": ["t_grunt", "t_grunt"], "back": ["t_mortar"]})
	c.play_card(give(c, "suppressing_fire"))
	eq(enemy(c, "front", 0).status("suppressed"), 2)
	eq(enemy(c, "front", 1).status("suppressed"), 2)
	eq(enemy(c, "back", 0).status("suppressed"), 0)


func test_timed_statuses_tick_down() -> void:
	var c := make_combat(ONE_GRENADIER)
	enemy(c).add_status("suppressed", 2)
	c.end_turn()
	eq(enemy(c).status("suppressed"), 1)


func test_mg42_cycle_suppresses_front_row() -> void:
	var c := make_combat({"front": ["t_mg"]})
	var sherman := deploy(c, "sherman")  # survives three MG turns
	sherman.firepower = 0
	c.end_turn()
	c.end_turn()
	eq(c.describe_intent(enemy(c)), "Suppressed 1, Attack 3 (direct)")
	c.end_turn()
	eq(sherman.status("suppressed"), 1, "applied this enemy turn, ticks on your next end turn")


func test_mortar_crew_targets_toughest_unit() -> void:
	var c := make_combat({"back": ["t_mortar"]})
	var weak := deploy(c, "medic", "front", 0)
	var tough := deploy(c, "rifle_squad", "back", 0)
	c.end_turn()
	eq(tough.hp, tough.max_hp - 5, "rifle squad (most HP) took 5 indirect")
	eq(weak.hp, weak.max_hp)


func test_advance_moves_and_fires() -> void:
	var c := make_combat(ONE_GRENADIER)
	var rifle := deploy(c, "rifle_squad", "back")
	eq(c.play_card(give(c, "advance"), {"unit": rifle}), "")
	eq(rifle.row, "front")
	eq(enemy(c).hp, 3, "fired with front bonus: 5")
	eq(c.play_card(give(c, "advance"), {"unit": rifle}), "wrong_row")


func test_medic_heals_adjacent() -> void:
	var c := make_combat({"back": ["t_mortar"]})
	deploy(c, "medic", "front", 0)
	var rifle := deploy(c, "rifle_squad", "front", 1)
	rifle.hp = 2
	c.end_turn()  # medic heals 2 at end of your turn; mortar then hits the first 4-HP unit (medic)
	eq(rifle.hp, 4)


func test_doctrine_triggers_each_turn() -> void:
	var c := make_combat({"back": ["t_mortar"]})
	var rifle := deploy(c, "rifle_squad")
	c.cp = 3
	eq(c.play_card(give(c, "hold_the_line")), "")
	check(c.doctrines.size() == 1)
	c.end_turn()
	eq(rifle.cover, 2)


func test_expend_removes_card_for_combat() -> void:
	var c := make_combat(ONE_GRENADIER)
	var req := give(c, "requisition")
	c.play_card(req)
	check(c.expended.has(req))
	eq(c.hand.size(), 2)


func test_upgrade_applies() -> void:
	var card := CardDB.make_card("rifle_squad", true)
	eq(card.display_name(), "Rifle Squad+")
	eq(card.def["unit"]["fp"], 4)
	eq(card.def["unit"]["rows"], ["front", "back"], "unchanged fields survive the merge")
	eq(CardDB.make_card("smoke_screen", true).cost(), 0)


func test_deck_reshuffles_when_empty() -> void:
	var c := Combat.from_encounter(CardDB.starter_deck(), "patrol", 60, 60, 3)
	c.end_turn()
	eq(c.hand.size(), 5)
	eq(c.draw_pile.size() + c.discard_pile.size() + c.hand.size(), 10)
	c.end_turn()
	eq(c.hand.size(), 5, "discard reshuffled into draw pile")


func test_relic_bronze_star_field_manual_flare() -> void:
	var c := Combat.new(CardDB.starter_deck(), ONE_GRUNT_LAYOUT(), 60, 60, 1, ["bronze_star", "field_manual", "signal_flare"])
	eq(c.cp, 4)
	eq(c.hand.size(), 6)
	eq(enemy(c).status("exposed"), 1)


func ONE_GRUNT_LAYOUT() -> Dictionary:
	return {"front": ["t_grunt"]}


func test_relic_cib_covers_first_deploy_only() -> void:
	var c := Combat.new(CardDB.starter_deck(), ONE_GRUNT_LAYOUT(), 60, 60, 1, ["cib"])
	c.draw_pile.append_array(c.hand)
	c.hand.clear()
	var a := deploy(c, "rifle_squad", "front", 0)
	var b := deploy(c, "rifle_squad", "front", 1)
	eq(a.cover, 4)
	eq(b.cover, 0)


func test_relic_lucky_zippo() -> void:
	var c := Combat.new(CardDB.starter_deck(), ONE_GRUNT_LAYOUT(), 60, 60, 1, ["lucky_zippo"])
	c.hand.clear()
	var a := deploy(c, "rifle_squad", "front", 0)
	c._lose_hp(a, 50)
	eq(a.hp, 1)
	check(a.is_on_field())
	c._lose_hp(a, 50)
	check(not a.is_on_field(), "only once per combat")


func test_summon_and_half_hp_trigger() -> void:
	var c := make_combat({"front": ["", "tiger", ""]})
	var tiger := enemy(c, "front", 1)
	c._lose_hp(tiger, tiger.max_hp - (tiger.max_hp / 2 + 1))
	eq(c.field.units(Battlefield.ENEMY).size(), 1, "not yet at half")
	c._lose_hp(tiger, 1)
	eq(c.field.units(Battlefield.ENEMY).size(), 3, "two grenadiers called in")
	check(not enemy(c, "front", 0).intent.is_empty(), "summoned during your turn: shows an intent")
	c._lose_hp(tiger, 10)
	eq(c.field.units(Battlefield.ENEMY).size(), 3, "triggers only once")


func test_tiger_armor_and_anti_armor() -> void:
	var c := make_combat({"front": ["", "tiger", ""]})
	var tiger := enemy(c, "front", 1)
	var rifle := deploy(c, "rifle_squad", "front", 1)
	eq(c.attack_damage(rifle, rifle.firepower, "direct", tiger), 2, "3 + 2 front - 3 armor")


func test_bunker_immune_to_precision() -> void:
	var c := make_combat({"front": ["bunker"]})
	eq(c.attack_damage(null, 10, "precision", enemy(c)), 0)
	eq(c.attack_damage(null, 10, "indirect", enemy(c)), 10)


func test_strength_adds_damage() -> void:
	var c := make_combat(ONE_GRUNT_LAYOUT())
	enemy(c).add_status("strength", 2)
	eq(c.attack_damage(enemy(c), 3, "direct", null), 5)


func test_flank_fires_as_precision() -> void:
	var c := make_combat({"front": ["t_grunt"], "back": ["t_mortar"]})
	var rifle := deploy(c, "rifle_squad", "back")
	var mortar := enemy(c, "back", 0)
	enemy(c).hp = 20
	eq(c.play_card(give(c, "flank"), {"unit": rifle}), "")
	eq(mortar.hp, 4, "precision picks the weakest enemy, even in the back row")


func test_artillery_barrage_hits_everyone() -> void:
	var c := make_combat({"front": ["t_grunt", "t_grunt"], "back": ["t_mortar"]})
	var grunt := enemy(c, "front", 0)
	eq(c.play_card(give(c, "artillery_barrage")), "")
	check(not grunt.is_on_field(), "8 HP grunt dies")
	eq(c.phase, Combat.Phase.WON)


func test_unit_death_costs_morale() -> void:
	var c := make_combat(ONE_GRUNT_LAYOUT())
	var u := deploy(c, "rifle_squad")
	c._lose_hp(u, 99)
	eq(c.resolve, 60 - Combat.MORALE_LOSS)


func test_dog_tags_raise_casualty_limit() -> void:
	var c := Combat.new(CardDB.starter_deck(), ONE_GRUNT_LAYOUT(), 60, 60, 1, ["dog_tags"])
	var card := CardDB.make_card("rifle_squad")
	card.casualties = Combat.MAX_CASUALTIES
	c.discard_pile.append(card)
	check(c.lost_cards().is_empty())


func test_spotter_boosts_indirect() -> void:
	var c := make_combat(ONE_GRUNT_LAYOUT())
	deploy(c, "spotter", "back")
	eq(c.attack_damage(null, 6, "indirect", enemy(c)), 8)
	eq(c.attack_damage(null, 6, "direct", enemy(c)), 6, "only indirect")
	eq(c.attack_damage(enemy(c), 6, "indirect", null), 6, "not for the enemy")


func test_engineer_and_lieutenant() -> void:
	var c := make_combat({"back": ["bunker"]})
	deploy(c, "engineer", "front", 0)
	var rifle := deploy(c, "rifle_squad", "front", 1)
	c.cp = 2
	deploy(c, "lieutenant", "back", 0)
	c.end_turn()
	check(c.events.any(func(e): return e["type"] == "cover_gained" and e["unit"] == rifle), "engineer dug in the neighbour")
	eq(c.cp, Combat.CP_PER_TURN + 1, "lieutenant adds command")


func test_flamethrower_ignores_cover_and_burns() -> void:
	var c := make_combat(ONE_GRUNT_LAYOUT())
	var g := enemy(c)
	g.cover = 10
	var flame := deploy(c, "flamethrower")
	c._fire_unit(flame)
	eq(g.hp, 8 - 6, "4 + 2 front bonus, cover ignored")
	eq(g.cover, 10)
	eq(g.status("bleeding"), 2)


func test_medevac_returns_card_without_casualty() -> void:
	var c := make_combat(ONE_GRUNT_LAYOUT())
	var u := deploy(c, "rifle_squad")
	var card := u.card
	eq(c.play_card(give(c, "medevac"), {"unit": u}), "")
	check(not u.is_on_field() and c.hand.has(card))
	eq(card.casualties, 0)
	eq(c.resolve, 60, "no morale loss")


func test_bangalore_strips_cover() -> void:
	var c := make_combat({"front": ["bunker"]})
	var b := enemy(c)
	c.cp = 3
	eq(c.play_card(give(c, "bangalore")), "")
	eq(b.cover, 0)
	eq(b.hp, b.max_hp - 6)
