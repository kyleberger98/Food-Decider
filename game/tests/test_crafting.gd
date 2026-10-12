extends TestCase


func _card(id: String, up := false) -> CardInstance:
	return CardDB.make_card(id, up)


func test_attachment_fit_rules() -> void:
	check(Crafting.attachment_fits(_card("rifle_squad"), "scope"))
	check(not Crafting.attachment_fits(_card("mortar_team"), "scope"), "scope needs direct fire")
	check(not Crafting.attachment_fits(_card("medic"), "scope"), "scope needs firepower")
	check(not Crafting.attachment_fits(_card("dig_in"), "bayonet"), "bayonet is units only")
	check(Crafting.attachment_fits(_card("dig_in"), "field_radio"))
	check(not Crafting.attachment_fits(_card("medic"), "extended_mag"))


func test_attachments_change_units() -> void:
	var card := _card("rifle_squad")
	card.attachment = "scope"
	eq(CardDB.unit_from_card(card).attack, "precision")
	card.attachment = "extended_mag"
	var u := CardDB.unit_from_card(card)
	eq([u.shots, u.firepower], [2, 2])
	card.attachment = "ration_pack"
	eq(CardDB.unit_from_card(card).max_hp, int(card.def["unit"]["hp"]) + 4)


func test_bayonet_and_sandbags_in_combat() -> void:
	var c := make_combat({"front": ["t_grunt"]})
	var card := _card("rifle_squad")
	card.attachment = "bayonet"
	c.hand.append(card)
	c.play_card(card, {"row": "front", "slot": 0})
	var u := c.field.get_unit(Battlefield.PLAYER, "front", 0)
	eq(c.attack_damage(u, u.firepower, "direct", enemy(c)), 8, "3 + 2 front + 3 bayonet")
	var bagged := _card("rifle_squad")
	bagged.attachment = "sandbags"
	c.hand.append(bagged)
	c.play_card(bagged, {"row": "front", "slot": 1})
	eq(c.field.get_unit(Battlefield.PLAYER, "front", 1).cover, 6)


func test_field_radio_draws() -> void:
	var c := make_combat({"front": ["t_grunt"]})
	var card := _card("dig_in")
	card.attachment = "field_radio"
	var rifle := deploy(c, "rifle_squad")
	c.hand.append(card)
	c.play_card(card, {"unit": rifle})
	eq(c.hand.size(), 1)


func test_recipe_merge_is_order_independent() -> void:
	eq(Crafting.recipe_for(_card("bazooka"), _card("rifle_squad")), "bazooka_team")
	eq(Crafting.recipe_for(_card("rifle_squad"), _card("bazooka")), "bazooka_team")
	var m := Crafting.merge(_card("rifle_squad"), _card("bazooka", true))
	eq(m.id, "bazooka_team")
	check(m.upgraded, "an upgraded input upgrades the result")
	check(m.is_merged())


func test_merge_rules() -> void:
	var a := _card("rifle_squad")
	eq(Crafting.merge_error(a, a), "Pick two different cards.")
	check(Crafting.merge_error(_card("rifle_squad"), _card("grenade")) != "", "unit + order isn't allowed without a recipe")
	eq(Crafting.merge_error(_card("bar_gunner"), _card("dig_in")), "", "recipe allows mixed types")
	check(Crafting.merge_error(_card("advance"), _card("fall_back")) != "", "conflicting row requirements")
	var merged := Crafting.merge(_card("rifle_squad"), _card("rifle_squad"))
	check(Crafting.merge_error(merged, _card("medic")) != "", "merged cards can't merge again")


func test_generic_unit_merge() -> void:
	var m := Crafting.merge(_card("sniper"), _card("mortar_team"))
	eq(m.type(), "unit")
	eq(m.cost(), 2)
	var u: Dictionary = m.def["unit"]
	var sniper: Dictionary = CardDB.cards()["sniper"]["unit"]
	var mortar: Dictionary = CardDB.cards()["mortar_team"]["unit"]
	eq(u["hp"], sniper["hp"] + mortar["hp"] / 2)
	eq(u["fp"], sniper["fp"] + mortar["fp"] / 2)
	eq(u["attack"], "precision")
	eq(u["rows"], ["back"])
	check(m.def["text"].begins_with("Deploy to back row. %d HP" % u["hp"]))


func test_generic_order_merge_plays_both_effects() -> void:
	var m := Crafting.merge(_card("grenade"), _card("requisition"))
	eq(m.def["target"], "enemy")
	check(m.def["expend"])
	var c := make_combat({"front": ["t_grunt"]})
	c.hand.append(m)
	eq(c.play_card(m, {"unit": enemy(c)}), "")
	eq(enemy(c).hp, 2)
	eq(c.hand.size(), 2, "drew 2")
	check(c.expended.has(m))


func test_generic_doctrine_merge_combines_triggers() -> void:
	var m := Crafting.merge(_card("hold_the_line"), _card("combined_arms"))
	check(m.def["triggers"].has("turn_start") and m.def["triggers"].has("on_deploy"))


func test_upgrade_generic_merge() -> void:
	var run := RunState.create(1)
	var a := run.add_card("sniper")
	var b := run.add_card("mortar_team")
	var m := run.merge_cards(a, b)
	check(not m.upgraded)
	var up := run.promote(m)
	check(up.upgraded and up.is_merged())
	eq(up.def["unit"]["fp"], 8 + 6 / 2, "built from upgraded parts")


func test_run_merge_replaces_cards_and_keeps_attachments() -> void:
	var run := RunState.create(2)
	var rifles := run.deck.filter(func(c): return c.id == "rifle_squad")
	rifles[0].attachment = "bayonet"
	rifles[1].attachment = "ration_pack"
	rifles[1].casualties = 2
	var size := run.deck.size()
	var m := run.merge_cards(rifles[0], rifles[1])
	eq(run.deck.size(), size - 1)
	eq(m.id, "reinforced_squad")
	eq(m.attachment, "bayonet")
	eq(run.attachments, ["ration_pack"], "second attachment returns to the kit")
	eq(m.casualties, 2)


func test_attach_from_kit() -> void:
	var run := RunState.create(3)
	run.attachments = ["scope", "sandbags"]
	var rifle: CardInstance = run.deck.filter(func(c): return c.id == "rifle_squad")[0]
	check(not run.attach(run.deck.filter(func(c): return c.id == "dig_in")[0], "scope"), "doesn't fit")
	check(run.attach(rifle, "scope"))
	check(run.attach(rifle, "sandbags"))
	eq(rifle.attachment, "sandbags")
	eq(run.attachments, ["scope"], "swapped attachment goes back to the kit")


func test_available_recipes_from_starter_deck() -> void:
	var run := RunState.create(4)
	var names := run.available_recipes().map(func(r): return r["result"])
	check(names.has("Reinforced Squad") and names.has("MG Nest") and names.has("Foxholes") and names.has("Volley Fire"))
	check(not names.has("Bazooka Team"))


func test_merged_cards_in_combat() -> void:
	var c := make_combat({"front": ["t_grunt"], "back": ["t_mortar"]})
	var nest := deploy(c, "mg_nest", "front", 0)
	eq(nest.cover, 4)
	check(nest.entrenched)
	var rifle := deploy(c, "rifle_squad", "front", 1)
	c.play_card(give(c, "foxholes"), {"unit": rifle})
	check(rifle.entrenched)
	c.cp = 3
	eq(c.play_card(give(c, "volley_fire")), "")
	check(c.events.filter(func(e): return e["type"] == "damaged").size() >= 4, "both units fired")
