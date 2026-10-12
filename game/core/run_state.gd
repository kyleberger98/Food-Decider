class_name RunState
extends RefCounted
## Everything that persists across one run: deck, Resolve, Supply, Commendations, map position.

const START_RESOLVE := 60
const START_SUPPLY := 99
const STARTER_RELIC := "cib"
const REST_HEAL := 0.3
const PURPLE_HEART_HEAL := 6
const REWARD_CHOICES := 3
## Supply rewards by fight tier: [min, max].
const SUPPLY_REWARD := {"easy": [12, 20], "normal": [15, 25], "elite": [30, 40], "boss": [0, 0]}
## Rarity odds for card rewards (common, uncommon, rare), elites roll better.
const RARITY_ODDS := {"normal": [0.6, 0.33, 0.07], "elite": [0.4, 0.42, 0.18]}
const CARD_PRICE := {"common": 50, "uncommon": 75, "rare": 140}
const RELIC_PRICE := 150
const REMOVE_PRICE := 75
const ATTACHMENT_PRICE := 40
## Chance a normal fight also drops an attachment (elites always do).
const ATTACHMENT_DROP := 0.3
## Floors (0-based) that use the easy encounter pool.
const EASY_FLOORS := 3

var rng := RandomNumberGenerator.new()
var deck: Array[CardInstance] = []
var resolve := START_RESOLVE
var max_resolve := START_RESOLVE
var supply := START_SUPPLY
var relics: Array = []
## Attachment ids carried but not yet fitted (fit them at a Workshop).
var attachments: Array = []
var map: RunMap
var position: RunMap.MapNode = null
var fights_won := 0
var won := false
var lost := false
var _last_encounter := ""


static func create(seed_value: int) -> RunState:
	var run := RunState.new()
	run.rng.seed = seed_value
	run.deck = CardDB.starter_deck()
	run.relics = [STARTER_RELIC]
	run.map = RunMap.generate(run.rng)
	return run


func is_over() -> bool:
	return won or lost


func floor_number() -> int:
	return 0 if position == null else position.row + 1


func available_nodes() -> Array:
	if is_over():
		return []
	return map.rows[0] if position == null else position.next


func enter(node: RunMap.MapNode) -> void:
	assert(available_nodes().has(node), "Node not reachable")
	position = node
	node.visited = true


# --- Combat ------------------------------------------------------------------

func tier_for(node_type: String) -> String:
	match node_type:
		"elite":
			return "elite"
		"boss":
			return "boss"
	return "easy" if position != null and position.row < EASY_FLOORS else "normal"


func pick_encounter(tier: String) -> String:
	var pool := CardDB.encounters().keys().filter(func(id):
		return CardDB.encounters()[id]["tier"] == tier and id != _last_encounter)
	if pool.is_empty():
		pool = CardDB.encounters().keys().filter(func(id): return CardDB.encounters()[id]["tier"] == tier)
	_last_encounter = pool[rng.randi() % pool.size()]
	return _last_encounter


func start_combat(encounter_id: String) -> Combat:
	return Combat.from_encounter(deck, encounter_id, resolve, max_resolve, rng.randi(), relics)


## Applies a finished combat to the run. Returns the rewards on offer:
## {"lost_cards": [...], "supply": int, "cards": [ids], "relic": id or ""}.
func finish_combat(c: Combat, tier: String) -> Dictionary:
	var result := {"lost_cards": c.lost_cards(), "supply": 0, "cards": [], "relic": "", "attachment": ""}
	for card in result["lost_cards"]:
		deck.erase(card)
	resolve = c.resolve
	if c.phase == Combat.Phase.LOST:
		lost = true
		return result
	fights_won += 1
	if relics.has("purple_heart"):
		heal(PURPLE_HEART_HEAL)
	if tier == "boss":
		won = true
		return result
	var range_: Array = SUPPLY_REWARD[tier]
	result["supply"] = rng.randi_range(range_[0], range_[1])
	supply += result["supply"]
	result["cards"] = card_choices(REWARD_CHOICES, "elite" if tier == "elite" else "normal")
	if tier == "elite" or rng.randf() < ATTACHMENT_DROP:
		result["attachment"] = random_attachment()
		attachments.append(result["attachment"])
	if tier == "elite":
		result["relic"] = random_relic()
		if result["relic"] != "":
			relics.append(result["relic"])
	return result


# --- Cards & relics -------------------------------------------------------------

func roll_rarity(odds_key := "normal") -> String:
	var odds: Array = RARITY_ODDS[odds_key]
	var r := rng.randf()
	if r < odds[0]:
		return "common"
	if r < odds[0] + odds[1]:
		return "uncommon"
	return "rare"


func random_card_id(rarity: String) -> String:
	var pool := CardDB.reward_pool(rarity)
	if pool.is_empty():
		pool = CardDB.reward_pool()
	return pool[rng.randi() % pool.size()]


## n distinct card ids.
func card_choices(n: int, odds_key := "normal") -> Array:
	var picks := []
	var guard := 0
	while picks.size() < n and guard < 100:
		guard += 1
		var id := random_card_id(roll_rarity(odds_key))
		if not picks.has(id):
			picks.append(id)
	return picks


func add_card(id: String) -> CardInstance:
	var card := CardDB.make_card(id)
	deck.append(card)
	return card


func random_relic() -> String:
	var pool := CardDB.relics().keys().filter(func(id):
		return not relics.has(id) and CardDB.relics()[id]["rarity"] != "starter")
	return pool[rng.randi() % pool.size()] if not pool.is_empty() else ""


func heal(amount: int) -> int:
	var before := resolve
	resolve = mini(max_resolve, resolve + amount)
	return resolve - before


# --- Field Aid Station ------------------------------------------------------------

func rest_heal_amount() -> int:
	return int(ceil(max_resolve * REST_HEAL))


func rest_heal() -> int:
	return heal(rest_heal_amount())


## Upgrade a card in place (new instance keeps casualties and attachment).
func promote(card: CardInstance) -> CardInstance:
	assert(not card.upgraded)
	var up := Crafting.upgrade_merged(card) if card.is_merged() else CardDB.make_card(card.id, true)
	up.casualties = card.casualties
	up.attachment = card.attachment
	deck[deck.find(card)] = up
	return up


func rotate_troops(card: CardInstance) -> void:
	card.casualties = 0


# --- Quartermaster -------------------------------------------------------------------

## {"cards": [{"id", "price"}], "relics": [{"id", "price"}], "remove_price": int}
func shop_stock() -> Dictionary:
	var cards := []
	for id in card_choices(5):
		var rarity: String = CardDB.cards()[id]["rarity"]
		cards.append({"id": id, "price": CARD_PRICE.get(rarity, 75)})
	var att_stock := []
	for i in 2:
		att_stock.append({"id": random_attachment(), "price": ATTACHMENT_PRICE})
	var relic_stock := []
	var a := random_relic()
	if a != "":
		relic_stock.append({"id": a, "price": RELIC_PRICE})
	return {"cards": cards, "attachments": att_stock, "relics": relic_stock, "remove_price": REMOVE_PRICE}


func can_afford(price: int) -> bool:
	return supply >= price


func buy_card(id: String, price: int) -> bool:
	if not can_afford(price):
		return false
	supply -= price
	add_card(id)
	return true


func buy_relic(id: String, price: int) -> bool:
	if not can_afford(price) or relics.has(id):
		return false
	supply -= price
	relics.append(id)
	return true


func buy_attachment(id: String, price: int) -> bool:
	if not can_afford(price):
		return false
	supply -= price
	attachments.append(id)
	return true


func remove_card(card: CardInstance, price: int) -> bool:
	if not can_afford(price) or not deck.has(card):
		return false
	supply -= price
	deck.erase(card)
	return true


# --- Workshop ------------------------------------------------------------------------

func random_attachment() -> String:
	var ids := CardDB.attachments().keys()
	return ids[rng.randi() % ids.size()]


func workshop_actions() -> int:
	return 2 if relics.has("spare_parts") else 1


## Fits an attachment from the kit to a card. A card's previous attachment goes back to the kit.
func attach(card: CardInstance, att_id: String) -> bool:
	if not attachments.has(att_id) or not deck.has(card) or not Crafting.attachment_fits(card, att_id):
		return false
	attachments.erase(att_id)
	if card.attachment != "":
		attachments.append(card.attachment)
	card.attachment = att_id
	return true


## Replaces a and b in the deck with their merge. Attachments: the first one that fits stays on the
## new card; any other goes back to the kit.
func merge_cards(a: CardInstance, b: CardInstance) -> CardInstance:
	assert(deck.has(a) and deck.has(b) and Crafting.merge_error(a, b) == "")
	var preview := preview_merge(a, b)
	var merged: CardInstance = preview["card"]
	attachments.append_array(preview["returned"])
	deck[deck.find(a)] = merged
	deck.erase(b)
	return merged


## What merging would produce, without changing anything: {"card": CardInstance, "returned": [attachment ids]}.
func preview_merge(a: CardInstance, b: CardInstance) -> Dictionary:
	var merged := Crafting.merge(a, b)
	var returned := []
	for att in [a.attachment, b.attachment]:
		if att == "":
			continue
		if merged.attachment == "" and Crafting.attachment_fits(merged, att):
			merged.attachment = att
		else:
			returned.append(att)
	return {"card": merged, "returned": returned}


## Recipes the current deck can make: [{"a": name, "b": name, "result": name}].
func available_recipes() -> Array:
	var out := []
	for r in CardDB.recipes():
		var a_cards := deck.filter(func(c): return c.id == r["a"])
		var b_cards := deck.filter(func(c): return c.id == r["b"])
		var enough: bool = a_cards.size() >= 2 if r["a"] == r["b"] else (not a_cards.is_empty() and not b_cards.is_empty())
		if enough:
			out.append({"a": CardDB.cards()[r["a"]]["name"], "b": CardDB.cards()[r["b"]]["name"],
				"result": CardDB.cards()[r["result"]]["name"]})
	return out


# --- Intel events -------------------------------------------------------------------

func pick_event() -> String:
	var ids := CardDB.events().keys()
	return ids[rng.randi() % ids.size()]


func choice_available(choice: Dictionary) -> bool:
	return supply >= int(choice.get("requires", {}).get("supply", 0))


## Applies an event choice. Returns lines describing what happened.
func apply_event_choice(event_id: String, index: int) -> PackedStringArray:
	var choice: Dictionary = CardDB.events()[event_id]["choices"][index]
	assert(choice_available(choice))
	var out: PackedStringArray = []
	_run_ops(choice["ops"], out)
	if resolve <= 0:
		resolve = 0
		lost = true
	return out


func _run_ops(ops: Array, out: PackedStringArray) -> void:
	for op in ops:
		match op["op"]:
			"supply":
				supply += int(op["amount"])
				out.append("%+d Supply" % int(op["amount"]))
			"resolve":
				var amt := int(op["amount"])
				if amt >= 0:
					out.append("Healed %d Resolve" % heal(amt))
				else:
					resolve += amt
					out.append("Lost %d Resolve" % -amt)
			"card":
				var id: String = op.get("id", "")
				if id == "":
					id = random_card_id(op.get("rarity", "common"))
				out.append("Gained card: " + add_card(id).display_name())
			"relic":
				var r := random_relic()
				if r != "":
					relics.append(r)
					out.append("Gained Commendation: " + CardDB.relics()[r]["name"])
			"chance":
				if rng.randf() < float(op["p"]):
					_run_ops(op["then"], out)
			"attachment":
				var att := random_attachment()
				attachments.append(att)
				out.append("Gained attachment: " + CardDB.attachments()[att]["name"])
			"clear_casualties":
				for c in deck:
					c.casualties = 0
				out.append("All casualties cleared")
