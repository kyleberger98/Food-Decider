class_name Combat
extends RefCounted
## One battle: deck piles, battlefield, turn flow and the effect interpreter.
## Pure logic: the UI drives it through play_card()/end_turn() and animates from `events`.

signal event_emitted(e: Dictionary)

const HAND_SIZE := 5
const MAX_HAND := 10
const CP_PER_TURN := 3
## Your Direct-fire units deal this much extra damage from the front row.
const FRONT_BONUS := 2
## A card whose unit has died this many times in a run is lost.
const MAX_CASUALTIES := 3
const SUPPRESSED_MULT := 0.75
const EXPOSED_MULT := 1.5
const ANTI_ARMOR_MULT := 1.5

enum Phase { PLAYER, ENEMY, WON, LOST }

var rng := RandomNumberGenerator.new()
var field := Battlefield.new()

var draw_pile: Array[CardInstance] = []
var hand: Array[CardInstance] = []
var discard_pile: Array[CardInstance] = []
var expended: Array[CardInstance] = []
## Cards whose units are currently on the field.
var deployed: Array[CardInstance] = []
var doctrines: Array[CardInstance] = []

var resolve := 0
var max_resolve := 0
var cp := 0
var turn := 0
var phase := Phase.PLAYER
var events: Array[Dictionary] = []


## enemy_layout: {"front": [enemy ids], "back": [enemy ids]}, as in an encounter.
func _init(deck: Array[CardInstance], enemy_layout: Dictionary, start_resolve: int, max_res: int, seed_value: int) -> void:
	rng.seed = seed_value
	resolve = start_resolve
	max_resolve = max_res
	draw_pile = deck.duplicate()
	_shuffle(draw_pile)
	for row in [Battlefield.FRONT, Battlefield.BACK]:
		var ids: Array = enemy_layout.get(row, [])
		for i in ids.size():
			var e := CardDB.make_enemy(ids[i])
			field.place(e, Battlefield.ENEMY, row, i)
	_emit("combat_started", {})
	_roll_intents()
	_start_player_turn()


static func from_encounter(deck: Array[CardInstance], encounter_id: String, start_resolve: int, max_res: int, seed_value: int) -> Combat:
	return Combat.new(deck, CardDB.encounters()[encounter_id], start_resolve, max_res, seed_value)


func is_over() -> bool:
	return phase == Phase.WON or phase == Phase.LOST


## Cards to remove from the deck after this combat (casualty limit reached).
func lost_cards() -> Array[CardInstance]:
	var out: Array[CardInstance] = []
	for c in draw_pile + hand + discard_pile + expended + deployed + doctrines:
		if c.casualties >= MAX_CASUALTIES and not out.has(c):
			out.append(c)
	return out


# --- Player actions -------------------------------------------------------

## Plays a card from hand. Returns "" on success or an error code.
## target: {"row": String, "slot": int} for units, {"unit": Unit} for ally/enemy targeted orders.
func play_card(card: CardInstance, target := {}) -> String:
	if phase != Phase.PLAYER:
		return "not_your_turn"
	if not hand.has(card):
		return "not_in_hand"
	var d := card.def
	if d.get("unplayable", false):
		return "unplayable"
	if card.cost() > cp:
		return "not_enough_cp"
	var err := validate_target(card, target)
	if err != "":
		return err

	cp -= card.cost()
	hand.erase(card)
	_emit("card_played", {"card": card})
	match card.type():
		"unit":
			_deploy(card, target["row"], int(target["slot"]))
		"doctrine":
			doctrines.append(card)
			_apply_effects(d.get("effects", []), Battlefield.PLAYER, null, null)
		_:
			_apply_effects(d.get("effects", []), Battlefield.PLAYER, null, target.get("unit"))
			if d.get("expend", false):
				expended.append(card)
			else:
				discard_pile.append(card)
	return ""


func validate_target(card: CardInstance, target: Dictionary) -> String:
	var d := card.def
	if card.type() == "unit":
		var row: String = target.get("row", "")
		if not d["unit"]["rows"].has(row):
			return "bad_row"
		if not field.is_free(Battlefield.PLAYER, row, int(target.get("slot", -1))):
			return "slot_taken"
		return ""
	match d.get("target", "none"):
		"ally":
			var u = target.get("unit")
			if not (u is Unit and u.side == Battlefield.PLAYER and u.is_on_field()):
				return "need_ally"
			var req: String = d.get("requires_row", "")
			if req != "":
				if u.row != req:
					return "wrong_row"
				var dest := Battlefield.BACK if req == Battlefield.FRONT else Battlefield.FRONT
				if field.first_free(Battlefield.PLAYER, dest) < 0:
					return "row_full"
		"enemy":
			var u = target.get("unit")
			if not (u is Unit and u.side == Battlefield.ENEMY and u.is_on_field()):
				return "need_enemy"
			var attack := _card_attack_type(d)
			if not field.legal_targets(attack, Battlefield.ENEMY).has(u):
				return "out_of_reach"
	return ""


func end_turn() -> void:
	if phase != Phase.PLAYER:
		return
	discard_pile.append_array(hand)
	hand.clear()

	# Your units fire, then medics patch people up.
	for u in field.units(Battlefield.PLAYER):
		if not u.is_on_field():
			continue
		if u.status("pinned") > 0:
			u.statuses.erase("pinned")
			_emit("pinned_skip", {"unit": u})
			continue
		_fire_unit(u)
		if is_over():
			return
	for u in field.units(Battlefield.PLAYER):
		var heal_amt := int(u.traits.get("medic", 0))
		if heal_amt > 0:
			for a in field.adjacent(u):
				var healed := a.heal(heal_amt)
				if healed > 0:
					_emit("healed", {"unit": a, "amount": healed})
	_end_of_side_turn(Battlefield.PLAYER)
	if is_over():
		return

	_enemy_phase()
	if is_over():
		return
	_roll_intents()
	_start_player_turn()


# --- Turn flow -------------------------------------------------------------

func _start_player_turn() -> void:
	phase = Phase.PLAYER
	turn += 1
	cp = CP_PER_TURN
	field.clear_smoke(Battlefield.PLAYER)
	for u in field.units(Battlefield.PLAYER):
		if not u.entrenched:
			u.cover = 0
	_emit("turn_started", {"turn": turn})
	for d in doctrines:
		_apply_effects(d.def.get("triggers", {}).get("turn_start", []), Battlefield.PLAYER, null, null)
	draw(HAND_SIZE)


func _enemy_phase() -> void:
	phase = Phase.ENEMY
	field.clear_smoke(Battlefield.ENEMY)
	var acting := field.units(Battlefield.ENEMY)
	for e in acting:
		if not e.entrenched:
			e.cover = 0
	for e in acting:
		if not e.is_on_field():
			continue
		if e.status("pinned") > 0:
			e.statuses.erase("pinned")
			_emit("pinned_skip", {"unit": e})
			continue
		_emit("intent_executed", {"unit": e, "intent": e.intent})
		_apply_effects(e.intent.get("ops", []), Battlefield.ENEMY, e, null)
		if is_over():
			return
	_end_of_side_turn(Battlefield.ENEMY)


func _end_of_side_turn(side: int) -> void:
	for u in field.units(side):
		var bleed := u.status("bleeding")
		if bleed > 0:
			u.add_status("bleeding", -1)
			_lose_hp(u, bleed)
			if is_over():
				return
		u.tick_timed_statuses()


func _roll_intents() -> void:
	for e in field.units(Battlefield.ENEMY):
		if e.intents.is_empty():
			e.intent = {}
			continue
		e.intent = e.intents[e.intent_index % e.intents.size()]
		e.intent_index += 1
		_emit("intent_set", {"unit": e, "intent": e.intent})


func draw(n: int) -> void:
	for i in n:
		if hand.size() >= MAX_HAND:
			return
		if draw_pile.is_empty():
			if discard_pile.is_empty():
				return
			draw_pile = discard_pile.duplicate()
			discard_pile.clear()
			_shuffle(draw_pile)
			_emit("reshuffled", {})
		var c: CardInstance = draw_pile.pop_back()
		hand.append(c)
		_emit("card_drawn", {"card": c})


# --- Combat resolution -----------------------------------------------------

func _deploy(card: CardInstance, row: String, slot: int) -> void:
	var u := CardDB.unit_from_card(card)
	field.place(u, Battlefield.PLAYER, row, slot)
	deployed.append(card)
	_emit("unit_deployed", {"unit": u})
	for d in doctrines:
		_apply_effects(d.def.get("triggers", {}).get("on_deploy", []), Battlefield.PLAYER, null, u)


func _fire_unit(u: Unit, bonus := 0) -> void:
	if u.firepower <= 0 and bonus <= 0:
		return
	_attack(u, u.firepower + bonus, u.shots, u.attack, "")


## One attack of `hits` shots. `rule` overrides automatic target picking (see Battlefield.pick_target).
func _attack(source: Unit, amount: int, hits: int, attack: String, rule: String) -> void:
	var target_side := Battlefield.other(source.side)
	for i in hits:
		var t := field.pick_target(source, attack, target_side, rule)
		var dmg := attack_damage(source, amount, attack, t)
		if t == null:
			if target_side == Battlefield.PLAYER:
				_hit_hq(dmg, source)
			else:
				return
		else:
			_damage_unit(t, dmg, attack, source)
		if is_over():
			return


## Final damage of one hit after all modifiers (not counting Cover or Smoke).
func attack_damage(source: Unit, amount: int, attack: String, target: Unit) -> int:
	var dmg := float(amount)
	if source != null:
		if source.side == Battlefield.PLAYER and attack == "direct" and source.row == Battlefield.FRONT:
			dmg += FRONT_BONUS
		if source.status("suppressed") > 0:
			dmg = floor(dmg * SUPPRESSED_MULT)
	var anti_armor := source != null and source.traits.has("anti_armor")
	if target != null:
		if target.status("exposed") > 0:
			dmg = floor(dmg * EXPOSED_MULT)
		if target.armor > 0:
			if anti_armor:
				dmg = floor(dmg * ANTI_ARMOR_MULT)
			elif attack == "direct":
				dmg -= target.armor
	return maxi(0, int(dmg))


func _damage_unit(t: Unit, dmg: int, attack: String, source: Unit) -> void:
	if attack == "direct" and field.has_smoke(t.side, t.row):
		_emit("missed", {"target": t, "source": source})
		return
	var blocked := mini(t.cover, dmg)
	t.cover -= blocked
	_emit("damaged", {"target": t, "source": source, "amount": dmg - blocked, "blocked": blocked})
	_lose_hp(t, dmg - blocked)


func _lose_hp(t: Unit, amount: int) -> void:
	if amount <= 0:
		return
	t.hp -= amount
	if t.hp <= 0:
		_kill(t)


func _kill(t: Unit) -> void:
	var side := t.side
	if side == Battlefield.PLAYER:
		t.card.casualties += 1
		deployed.erase(t.card)
		discard_pile.append(t.card)
	_emit("unit_killed", {"unit": t})
	field.remove(t)
	if side == Battlefield.ENEMY and field.units(Battlefield.ENEMY).is_empty():
		phase = Phase.WON
		_emit("combat_won", {})


func _hit_hq(dmg: int, source: Unit) -> void:
	resolve -= dmg
	_emit("hq_damaged", {"amount": dmg, "source": source})
	if resolve <= 0:
		resolve = 0
		phase = Phase.LOST
		_emit("combat_lost", {})


# --- Effect interpreter ----------------------------------------------------

## Runs data-defined effects for `side`. `source` is the acting unit (enemy intents) or null (cards).
## Each effect may set "scope": target (default), self, ally_front, all_allies, enemy_front, all_enemies, row.
func _apply_effects(effects: Array, side: int, source: Unit, target: Unit) -> void:
	for eff in effects:
		var op: String = eff["op"]
		var amount := int(eff.get("amount", 0))
		match op:
			"attack":
				_attack(source, amount, int(eff.get("hits", 1)), eff.get("attack", "direct"), eff.get("target", ""))
			"draw":
				draw(amount)
			"cp":
				cp += amount
			"smoke":
				field.set_smoke(side, eff.get("row", Battlefield.FRONT), true)
				_emit("smoke", {"side": side, "row": eff.get("row", Battlefield.FRONT)})
			_:
				for u in _scope(eff, side, source, target):
					if not u.is_on_field():
						continue
					_apply_unit_effect(eff, op, amount, u)
					if is_over():
						return
		if is_over():
			return


func _apply_unit_effect(eff: Dictionary, op: String, amount: int, u: Unit) -> void:
	match op:
		"fire":
			_fire_unit(u, int(eff.get("bonus", 0)))
		"cover":
			u.cover += amount
			_emit("cover_gained", {"unit": u, "amount": amount})
		"damage":
			var attack: String = eff.get("attack", "indirect")
			_damage_unit(u, attack_damage(null, amount, attack, u), attack, null)
		"heal":
			var healed := u.heal(amount)
			_emit("healed", {"unit": u, "amount": healed})
		"status":
			u.add_status(eff["status"], amount)
			_emit("status_applied", {"unit": u, "status": eff["status"], "amount": amount})
		"move":
			if field.move(u, eff["to"]):
				_emit("unit_moved", {"unit": u})
		_:
			push_error("Unknown effect op: " + op)


func _scope(eff: Dictionary, side: int, source: Unit, target: Unit) -> Array[Unit]:
	var foe := Battlefield.other(side)
	match eff.get("scope", "target"):
		"self":
			return _one(source)
		"ally_front":
			return field.row_units(side, Battlefield.FRONT)
		"all_allies":
			return field.units(side)
		"enemy_front":
			return field.row_units(foe, Battlefield.FRONT)
		"all_enemies":
			return field.units(foe)
		"row":
			return field.row_units(foe, eff.get("row", Battlefield.FRONT))
		_:
			# Enemy ops with no explicit target (e.g. cover) apply to the acting unit.
			return _one(target if target != null else source)


func _one(u: Unit) -> Array[Unit]:
	var out: Array[Unit] = []
	if u != null:
		out.append(u)
	return out


func _card_attack_type(d: Dictionary) -> String:
	for eff in d.get("effects", []):
		if eff.has("attack"):
			return eff["attack"]
	return "indirect"


## Human-readable summary of an enemy's current intent, for the UI.
func describe_intent(e: Unit) -> String:
	var parts: PackedStringArray = []
	for op in e.intent.get("ops", []):
		match op["op"]:
			"attack":
				var dmg := attack_damage(e, int(op["amount"]), op.get("attack", "direct"), null)
				var hits := int(op.get("hits", 1))
				parts.append("Attack %d%s (%s)" % [dmg, ("x%d" % hits) if hits > 1 else "", op.get("attack", "direct")])
			"cover":
				parts.append("Cover %d" % int(op["amount"]))
			"status":
				parts.append("%s %d" % [String(op["status"]).capitalize(), int(op["amount"])])
			_:
				parts.append(String(op["op"]).capitalize())
	return ", ".join(parts)


func _shuffle(pile: Array[CardInstance]) -> void:
	for i in range(pile.size() - 1, 0, -1):
		var j := rng.randi_range(0, i)
		var tmp := pile[i]
		pile[i] = pile[j]
		pile[j] = tmp


## Events about a unit record where it stood ("at": [side, row, slot]) so the UI can place effects
## even after the unit has moved or died.
func _emit(type: String, data: Dictionary) -> void:
	data["type"] = type
	var u = data.get("target", data.get("unit"))
	if u is Unit and u.row != "":
		data["at"] = [u.side, u.row, u.slot]
	events.append(data)
	event_emitted.emit(data)
