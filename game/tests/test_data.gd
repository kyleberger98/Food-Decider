extends TestCase
## Sanity checks over the shipped content in data/.

const KNOWN_OPS := ["attack", "draw", "cp", "smoke", "fire", "cover", "damage", "heal", "status", "move"]
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
		for e in enc.get("front", []) + enc.get("back", []):
			check(CardDB.enemies().has(e), "%s: unknown enemy %s" % [id, e])
