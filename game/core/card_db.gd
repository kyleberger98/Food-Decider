class_name CardDB
extends RefCounted
## Loads content from res://data and builds cards and enemy units from it.

const CARDS_PATH := "res://data/cards.json"
const ENEMIES_PATH := "res://data/enemies.json"
const RELICS_PATH := "res://data/relics.json"
const EVENTS_PATH := "res://data/events.json"
const ATTACHMENTS_PATH := "res://data/attachments.json"
const RECIPES_PATH := "res://data/recipes.json"

const STARTER_DECK := {"rifle_squad": 3, "bar_gunner": 1, "open_fire": 3, "dig_in": 3}

static var _cards := {}
static var _enemy_data := {}
static var _relics := {}
static var _events := {}
static var _attachments := {}
static var _recipes := []


static func cards() -> Dictionary:
	if _cards.is_empty():
		_cards = _load_json(CARDS_PATH)
	return _cards


static func enemies() -> Dictionary:
	return _enemy_data_cached()["enemies"]


static func encounters() -> Dictionary:
	return _enemy_data_cached()["encounters"]


static func relics() -> Dictionary:
	if _relics.is_empty():
		_relics = _load_json(RELICS_PATH)
	return _relics


static func events() -> Dictionary:
	if _events.is_empty():
		_events = _load_json(EVENTS_PATH)
	return _events


static func attachments() -> Dictionary:
	if _attachments.is_empty():
		_attachments = _load_json(ATTACHMENTS_PATH)
	return _attachments


## [{"a": id, "b": id, "result": id}, ...]
static func recipes() -> Array:
	if _recipes.is_empty():
		var parsed = JSON.parse_string(FileAccess.get_file_as_string(RECIPES_PATH))
		assert(parsed is Array, "Failed to parse " + RECIPES_PATH)
		_recipes = parsed
	return _recipes


## Card ids that can show up as rewards/shop stock, optionally of one rarity.
## Starter cards and merge results never do.
static func reward_pool(rarity := "") -> Array:
	return cards().keys().filter(func(id):
		var r: String = cards()[id].get("rarity", "")
		return r != "starter" and r != "merged" and (rarity == "" or r == rarity))


static func card_def(id: String, upgraded := false) -> Dictionary:
	assert(cards().has(id), "Unknown card: " + id)
	var base: Dictionary = cards()[id].duplicate(true)
	var upgrade: Dictionary = base.get("upgrade", {})
	base.erase("upgrade")
	if upgraded:
		_merge_into(base, upgrade)
	return base


static func make_card(id: String, upgraded := false) -> CardInstance:
	return CardInstance.new(id, card_def(id, upgraded), upgraded)


static func starter_deck() -> Array[CardInstance]:
	var deck: Array[CardInstance] = []
	for id in STARTER_DECK:
		for i in STARTER_DECK[id]:
			deck.append(make_card(id))
	return deck


## Builds a player-side Unit from a unit card.
static func unit_from_card(card: CardInstance) -> Unit:
	var u: Dictionary = card.def["unit"]
	var unit := Unit.new()
	unit.id = card.id
	unit.name = card.display_name()
	unit.max_hp = int(u["hp"])
	unit.hp = unit.max_hp
	unit.firepower = int(u.get("fp", 0))
	unit.shots = int(u.get("shots", 1))
	unit.attack = u.get("attack", "direct")
	unit.armor = int(u.get("armor", 0))
	unit.traits = u.get("traits", {}).duplicate()
	unit.entrenched = bool(u.get("entrenched", false))
	unit.card = card
	match card.attachment:
		"scope":
			unit.attack = "precision"
		"bayonet":
			unit.traits["bayonet"] = 3
		"extended_mag":
			unit.shots += 1
			unit.firepower = maxi(1, unit.firepower - 1)
		"sandbags":
			unit.traits["deploy_cover"] = int(unit.traits.get("deploy_cover", 0)) + 6
		"ration_pack":
			unit.max_hp += 4
			unit.hp = unit.max_hp
	return unit


static func make_enemy(id: String) -> Unit:
	assert(enemies().has(id), "Unknown enemy: " + id)
	var e: Dictionary = enemies()[id]
	var unit := Unit.new()
	unit.id = id
	unit.name = e["name"]
	unit.max_hp = int(e["hp"])
	unit.hp = unit.max_hp
	unit.armor = int(e.get("armor", 0))
	unit.entrenched = bool(e.get("entrenched", false))
	unit.cover = int(e.get("cover", 0))
	unit.traits = e.get("traits", {}).duplicate()
	unit.intents = e["intents"]
	unit.on_half_hp = e.get("on_half_hp", [])
	return unit


static func _enemy_data_cached() -> Dictionary:
	if _enemy_data.is_empty():
		_enemy_data = _load_json(ENEMIES_PATH)
	return _enemy_data


static func _load_json(path: String) -> Dictionary:
	var text := FileAccess.get_file_as_string(path)
	var parsed = JSON.parse_string(text)
	assert(parsed is Dictionary, "Failed to parse " + path)
	return _ints(parsed)


## JSON numbers parse as floats; turn whole numbers back into ints.
static func _ints(v: Variant) -> Variant:
	if v is float and v == floor(v):
		return int(v)
	if v is Dictionary:
		for k in v:
			v[k] = _ints(v[k])
	elif v is Array:
		for i in v.size():
			v[i] = _ints(v[i])
	return v


static func _merge_into(base: Dictionary, over: Dictionary) -> void:
	for k in over:
		if base.get(k) is Dictionary and over[k] is Dictionary:
			_merge_into(base[k], over[k])
		else:
			base[k] = over[k]
