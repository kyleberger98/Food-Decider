extends TestCase


func _unit(hp := 5) -> Unit:
	var u := Unit.new()
	u.max_hp = hp
	u.hp = hp
	return u


func test_place_and_slot_rules() -> void:
	var f := Battlefield.new()
	var a := _unit()
	check(f.place(a, Battlefield.PLAYER, "front", 1))
	check(not f.place(_unit(), Battlefield.PLAYER, "front", 1), "slot already taken")
	check(not f.is_free(Battlefield.PLAYER, "back", 2), "back row has only 2 slots")
	eq(f.first_free(Battlefield.PLAYER, "front"), 0)
	f.remove(a)
	check(f.is_free(Battlefield.PLAYER, "front", 1))
	eq(a.row, "")


func test_direct_fire_hits_front_then_back() -> void:
	var f := Battlefield.new()
	var front := _unit()
	var back := _unit()
	f.place(front, Battlefield.ENEMY, "front", 0)
	f.place(back, Battlefield.ENEMY, "back", 0)
	eq(f.legal_targets("direct", Battlefield.ENEMY), [front] as Array[Unit])
	eq(f.legal_targets("indirect", Battlefield.ENEMY).size(), 2)
	f.remove(front)
	eq(f.legal_targets("direct", Battlefield.ENEMY), [back] as Array[Unit], "back row exposed once front is empty")


func test_lane_targeting() -> void:
	var f := Battlefield.new()
	var shooter := _unit()
	f.place(shooter, Battlefield.PLAYER, "front", 2)
	var left := _unit()
	var right := _unit()
	f.place(left, Battlefield.ENEMY, "front", 0)
	f.place(right, Battlefield.ENEMY, "front", 2)
	check(f.pick_target(shooter, "direct", Battlefield.ENEMY) == right, "hits the unit in its lane")
	f.remove(right)
	check(f.pick_target(shooter, "direct", Battlefield.ENEMY) == left, "falls back to leftmost")


func test_indirect_and_precision_rules() -> void:
	var f := Battlefield.new()
	var tough := _unit(9)
	var weak := _unit(2)
	f.place(tough, Battlefield.ENEMY, "front", 0)
	f.place(weak, Battlefield.ENEMY, "back", 1)
	check(f.pick_target(null, "indirect", Battlefield.ENEMY) == tough)
	check(f.pick_target(null, "precision", Battlefield.ENEMY) == weak)
	check(f.pick_target(null, "precision", Battlefield.PLAYER) == null, "nothing to hit")


func test_move_between_rows() -> void:
	var f := Battlefield.new()
	var u := _unit()
	f.place(u, Battlefield.PLAYER, "back", 1)
	check(f.move(u, "front"))
	eq(u.row, "front")
	eq(u.slot, 0)
	check(f.is_free(Battlefield.PLAYER, "back", 1))


func test_adjacent() -> void:
	var f := Battlefield.new()
	var a := _unit()
	var b := _unit()
	var c := _unit()
	f.place(a, Battlefield.PLAYER, "front", 0)
	f.place(b, Battlefield.PLAYER, "front", 1)
	f.place(c, Battlefield.PLAYER, "back", 1)
	eq(f.adjacent(b), [a] as Array[Unit], "adjacency is within a row")
