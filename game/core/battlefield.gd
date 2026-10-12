class_name Battlefield
extends RefCounted
## Two sides, each with a front row (3 slots) and back row (2 slots). Owns targeting rules.

const PLAYER := 0
const ENEMY := 1
const FRONT := "front"
const BACK := "back"
const ROW_SIZE := {FRONT: 3, BACK: 2}

var _rows: Array = []
var _smoke: Array = []


func _init() -> void:
	for side in [PLAYER, ENEMY]:
		_rows.append({FRONT: [null, null, null], BACK: [null, null]})
		_smoke.append({FRONT: false, BACK: false})


static func other(side: int) -> int:
	return ENEMY if side == PLAYER else PLAYER


func get_unit(side: int, row: String, slot: int) -> Unit:
	return _rows[side][row][slot]


func is_free(side: int, row: String, slot: int) -> bool:
	return ROW_SIZE.has(row) and slot >= 0 and slot < ROW_SIZE[row] and _rows[side][row][slot] == null


func first_free(side: int, row: String) -> int:
	for i in ROW_SIZE[row]:
		if _rows[side][row][i] == null:
			return i
	return -1


func place(u: Unit, side: int, row: String, slot: int) -> bool:
	if not is_free(side, row, slot):
		return false
	_rows[side][row][slot] = u
	u.side = side
	u.row = row
	u.slot = slot
	return true


func remove(u: Unit) -> void:
	if u.row != "" and _rows[u.side][u.row][u.slot] == u:
		_rows[u.side][u.row][u.slot] = null
	u.row = ""
	u.slot = -1


## Moves a unit to the first free slot of another row. Returns false if it can't.
func move(u: Unit, row: String) -> bool:
	if u.row == row or u.row == "":
		return false
	var s := first_free(u.side, row)
	if s < 0:
		return false
	var side := u.side
	remove(u)
	place(u, side, row, s)
	return true


func row_units(side: int, row: String) -> Array[Unit]:
	var out: Array[Unit] = []
	for u in _rows[side][row]:
		if u != null:
			out.append(u)
	return out


## Front row left-to-right, then back row.
func units(side: int) -> Array[Unit]:
	var out := row_units(side, FRONT)
	out.append_array(row_units(side, BACK))
	return out


func adjacent(u: Unit) -> Array[Unit]:
	var out: Array[Unit] = []
	for s in [u.slot - 1, u.slot + 1]:
		if s >= 0 and s < ROW_SIZE[u.row] and _rows[u.side][u.row][s] != null:
			out.append(_rows[u.side][u.row][s])
	return out


func set_smoke(side: int, row: String, on: bool) -> void:
	_smoke[side][row] = on


func has_smoke(side: int, row: String) -> bool:
	return _smoke[side][row]


func clear_smoke(side: int) -> void:
	for row in ROW_SIZE:
		_smoke[side][row] = false


## Units on target_side that an attack of this type may legally hit.
## Direct fire hits the front row, or the back row once the front is empty.
## "back" (enemy snipers) only hits the back row.
func legal_targets(attack: String, target_side: int) -> Array[Unit]:
	match attack:
		"direct":
			var front := row_units(target_side, FRONT)
			return front if not front.is_empty() else row_units(target_side, BACK)
		"back":
			return row_units(target_side, BACK)
		_:
			return units(target_side)


## Picks who an automatic attack hits. Returns null when there is nothing to hit
## (for enemy attacks that means the shot lands on the player's HQ).
## Rules: "lane" = the unit facing the attacker, else leftmost; "highest_hp"; "lowest_hp"; "back".
func pick_target(attacker: Unit, attack: String, target_side: int, rule := "") -> Unit:
	if rule == "":
		rule = {"direct": "lane", "indirect": "highest_hp", "precision": "lowest_hp"}.get(attack, "lane")
	var candidates := legal_targets("back" if rule == "back" else attack, target_side)
	if candidates.is_empty():
		return null
	match rule:
		"highest_hp":
			var best := candidates[0]
			for c in candidates:
				if c.hp > best.hp:
					best = c
			return best
		"lowest_hp":
			var best := candidates[0]
			for c in candidates:
				if c.hp < best.hp:
					best = c
			return best
		"lane":
			if attacker != null:
				for c in candidates:
					if c.row == FRONT and c.slot == attacker.slot:
						return c
	return candidates[0]
