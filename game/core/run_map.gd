class_name RunMap
extends RefCounted
## Slay-the-Spire-style branching map: FLOORS rows of nodes joined by paths, then the boss.

const FLOORS := 12
const WIDTH := 5
const PATHS := 4
## Weighted node types for floors without a fixed type.
const WEIGHTS := {"skirmish": 42, "intel": 18, "elite": 12, "aid": 10, "quartermaster": 8, "workshop": 10}
## These never appear before this floor (0-based).
const MIN_FLOOR := {"elite": 4, "aid": 4, "workshop": 2}
## Types that shouldn't appear twice in a row along a path.
const NO_REPEAT := ["elite", "aid", "quartermaster", "workshop"]


class MapNode:
	extends RefCounted
	var row: int
	var col: int
	var type := ""
	var next: Array[MapNode] = []
	var visited := false

	func _init(f: int, c: int) -> void:
		row = f
		col = c


## rows[floor] = nodes on that floor, sorted by column.
var rows: Array = []
var boss: MapNode


static func generate(rng: RandomNumberGenerator) -> RunMap:
	var m := RunMap.new()
	var grid := []
	for f in FLOORS:
		var row := []
		row.resize(WIDTH)
		grid.append(row)

	var starts := []
	for p in PATHS:
		var col := rng.randi_range(0, WIDTH - 1)
		# The first two paths start apart so there's always a real first choice.
		while p == 1 and col == starts[0]:
			col = rng.randi_range(0, WIDTH - 1)
		starts.append(col)
		var prev: MapNode = null
		for f in FLOORS:
			if grid[f][col] == null:
				grid[f][col] = MapNode.new(f, col)
			var node: MapNode = grid[f][col]
			if prev != null and not prev.next.has(node):
				prev.next.append(node)
			prev = node
			col = clampi(col + rng.randi_range(-1, 1), 0, WIDTH - 1)

	m.boss = MapNode.new(FLOORS, WIDTH / 2)
	m.boss.type = "boss"
	for f in FLOORS:
		var row: Array[MapNode] = []
		for node in grid[f]:
			if node != null:
				row.append(node)
		m.rows.append(row)
	for node in m.rows[FLOORS - 1]:
		node.next.append(m.boss)
	m._assign_types(rng)
	return m


func _assign_types(rng: RandomNumberGenerator) -> void:
	var parents := {}
	for f in FLOORS:
		for node in rows[f]:
			for n in node.next:
				parents[n] = parents.get(n, []) + [node]
	for f in FLOORS:
		for node in rows[f]:
			if f == 0:
				node.type = "skirmish"
			elif f == FLOORS - 1:
				node.type = "aid"
			else:
				node.type = _roll_type(rng, f, parents.get(node, []))


func _roll_type(rng: RandomNumberGenerator, f: int, parents: Array) -> String:
	var parent_types := parents.map(func(p): return p.type)
	for attempt in 20:
		var total := 0
		for t in WEIGHTS:
			total += WEIGHTS[t]
		var roll := rng.randi_range(1, total)
		var type := ""
		for t in WEIGHTS:
			roll -= WEIGHTS[t]
			if roll <= 0:
				type = t
				break
		if f < MIN_FLOOR.get(type, 0):
			continue
		# No rest right before the guaranteed rest floor.
		if type == "aid" and f == FLOORS - 2:
			continue
		if NO_REPEAT.has(type) and parent_types.has(type):
			continue
		return type
	return "skirmish"


func all_nodes() -> Array[MapNode]:
	var out: Array[MapNode] = []
	for row in rows:
		out.append_array(row)
	out.append(boss)
	return out
