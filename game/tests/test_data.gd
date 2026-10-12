extends TestCase
## Sanity checks over the shipped content in data/.

const KNOWN_OPS := ["attack", "draw", "cp", "smoke", "summon", "fire", "buff_fp", "cleanse", "entrench", "cover", "damage", "heal", "status", "move"]
const RUN_OPS := ["supply", "resolve", "card", "relic", "attachment", "chance", "clear_casualties"]
const CARD_TYPES := ["unit", "order", "doctrine", "status"]


func _check_ops(where: String, ops: Array) -> void:
	for op in ops:
		check(KNOWN_OPS.has(op.get("op")), "%s: unknown op %s" % [where, op.get("op")])


func test_every_card_builds_base_and_upgraded() -> void:
	for id in CardDB.cards():
		for up in [false, true]:
			var card := CardDB.make_card(id, up)
			check(CARD_TYPES.has(card.type()), id + ": bad type")
			check(card.def.has("name") and card.def.has("cost") and card.def.has("text"), id + ": missing fields")
			_check_ops(id, card.def.get("effects", []))
			for trigger in card.def.get("triggers", {}).values():
				_check_ops(id, trigger)
			if card.type() == "unit":
				var u := CardDB.unit_from_card(card)
				check(u.max_hp > 0, id + ": unit needs HP")


func test_every_enemy_builds_with_intents() -> void:
	for id in CardDB.enemies():
		var e := CardDB.make_enemy(id)
		check(e.max_hp > 0, id)
		check(not e.intents.is_empty(), id + ": needs intents")
		for intent in e.intents:
			_check_ops(id, intent["ops"])


func test_encounters_fit_the_field() -> void:
	for id in CardDB.encounters():
		var enc: Dictionary = CardDB.encounters()[id]
		check(enc.get("front", []).size() <= 3 and enc.get("back", []).size() <= 2, id + ": too many enemies")
		check(["easy", "normal", "elite", "boss"].has(enc.get("tier")), id + ": bad tier")
		for e in enc.get("front", []) + enc.get("back", []):
			check(e == "" or CardDB.enemies().has(e), "%s: unknown enemy %s" % [id, e])
	for id in CardDB.enemies():
		for op in CardDB.enemies()[id].get("on_half_hp", []) + _summons(id):
			if op["op"] == "summon":
				check(CardDB.enemies().has(op["enemy"]), id + ": summons unknown enemy")


func _summons(id: String) -> Array:
	var out := []
	for intent in CardDB.enemies()[id]["intents"]:
		out.append_array(intent["ops"])
	return out


func test_relics_and_events() -> void:
	for id in CardDB.relics():
		var r: Dictionary = CardDB.relics()[id]
		check(r.has("name") and r.has("text") and r.has("rarity"), id)
	for id in CardDB.events():
		var ev: Dictionary = CardDB.events()[id]
		check(ev.has("name") and ev.has("text") and not ev["choices"].is_empty(), id)
		for choice in ev["choices"]:
			_check_run_ops(id, choice["ops"])


func _check_run_ops(where: String, ops: Array) -> void:
	for op in ops:
		check(RUN_OPS.has(op["op"]), "%s: unknown run op %s" % [where, op["op"]])
		if op["op"] == "chance":
			_check_run_ops(where, op["then"])
		if op["op"] == "card" and op.has("id"):
			check(CardDB.cards().has(op["id"]), where + ": unknown card " + op["id"])


func test_recipes_and_attachments() -> void:
	for r in CardDB.recipes():
		for k in ["a", "b", "result"]:
			check(CardDB.cards().has(r[k]), "recipe uses unknown card " + r[k])
		eq(CardDB.cards()[r["result"]]["rarity"], "merged", r["result"] + " should be rarity merged")
	for id in CardDB.attachments():
		var a: Dictionary = CardDB.attachments()[id]
		check(["any", "unit", "unit_direct", "unit_fp2"].has(a["fits"]), id + ": bad fits")
	check(not CardDB.reward_pool().has("satchel_charge"), "merge results aren't rewards")
