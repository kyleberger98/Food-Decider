class_name CardInstance
extends RefCounted
## One copy of a card in the player's deck. Lives for the whole run.

static var _next_uid := 1

var uid: int
var id: String
var upgraded := false
var attachment := ""
## Times this card's unit has been killed this run. At Combat.MAX_CASUALTIES the card is lost.
var casualties := 0
## Resolved definition (base + upgrade applied). Read-only by convention.
var def: Dictionary
## Set on cards made by merging: [[id, upgraded], [id, upgraded]] for generic merges, or the recipe inputs.
var components: Array = []


func _init(card_id: String, card_def: Dictionary, is_upgraded := false) -> void:
	uid = _next_uid
	_next_uid += 1
	id = card_id
	def = card_def
	upgraded = is_upgraded


func display_name() -> String:
	return def["name"] + ("+" if upgraded else "")


func is_merged() -> bool:
	return not components.is_empty()


func cost() -> int:
	return int(def["cost"])


func type() -> String:
	return def["type"]
