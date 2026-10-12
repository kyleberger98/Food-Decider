class_name TestCase
extends RefCounted
## Minimal base class for headless tests. Methods named test_* are run by run_tests.gd.

var failures: PackedStringArray = []
var current_test := ""

## Fixed-stat enemies so tests don't break when real content is rebalanced.
const FIXTURE_ENEMIES := {
	"t_grunt": {"name": "Test Grunt", "hp": 8, "intents": [
		{"ops": [{"op": "attack", "amount": 3, "attack": "direct"}]}]},
	"t_mg": {"name": "Test MG", "hp": 10, "intents": [
		{"ops": [{"op": "attack", "amount": 3, "hits": 2, "attack": "direct"}]},
		{"ops": [{"op": "attack", "amount": 3, "hits": 2, "attack": "direct"}]},
		{"ops": [{"op": "status", "status": "suppressed", "amount": 1, "scope": "row", "row": "front"},
				{"op": "attack", "amount": 3, "attack": "direct"}]}]},
	"t_mortar": {"name": "Test Mortar", "hp": 7, "intents": [
		{"ops": [{"op": "attack", "amount": 5, "attack": "indirect", "target": "highest_hp"}]},
		{"ops": [{"op": "cover", "amount": 4}]}]},
}


func _init() -> void:
	CardDB.enemies().merge(FIXTURE_ENEMIES, true)


func check(cond: bool, msg := "") -> void:
	if not cond:
		failures.append("%s: check failed %s" % [current_test, msg])


func eq(actual: Variant, expected: Variant, msg := "") -> void:
	var comparable := typeof(actual) == typeof(expected) or (_is_num(actual) and _is_num(expected))
	if not (comparable and actual == expected):
		failures.append("%s: expected %s, got %s %s" % [current_test, str(expected), str(actual), msg])


func _is_num(v: Variant) -> bool:
	return v is int or v is float


# --- Helpers ---------------------------------------------------------------

## A combat with an empty hand and full CP, so tests control exactly what's played.
func make_combat(layout: Dictionary, deck: Array[CardInstance] = [], resolve := 60) -> Combat:
	if deck.is_empty():
		deck = CardDB.starter_deck()
	var c := Combat.new(deck, layout, resolve, resolve, 1234)
	c.draw_pile.append_array(c.hand)
	c.hand.clear()
	return c


func give(c: Combat, id: String, upgraded := false) -> CardInstance:
	var card := CardDB.make_card(id, upgraded)
	c.hand.append(card)
	return card


func deploy(c: Combat, id: String, row := "front", slot := 0) -> Unit:
	var card := give(c, id)
	c.cp += card.cost()
	var err := c.play_card(card, {"row": row, "slot": slot})
	assert(err == "", "deploy failed: " + err)
	return c.field.get_unit(Battlefield.PLAYER, row, slot)


func enemy(c: Combat, row := "front", slot := 0) -> Unit:
	return c.field.get_unit(Battlefield.ENEMY, row, slot)
