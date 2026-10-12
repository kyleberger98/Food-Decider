extends Control
## The campaign map, drawn left (start) to right (boss). Click a highlighted node to move there.

const TYPE_STYLE := {
	"skirmish": ["Fight", Color("8a7f62")],
	"elite": ["Elite", Color("b0503f")],
	"aid": ["Aid", Color("5f9a54")],
	"quartermaster": ["Shop", Color("c49a3a")],
	"intel": ["Intel", Color("5d84a8")],
	"boss": ["TIGER", Color("d0402f")],
}
const NODE_SIZE := Vector2(64, 36)


class Lines:
	extends Control
	## [[from, to, color, width], ...]
	var segments: Array = []

	func _draw() -> void:
		for s in segments:
			draw_line(s[0], s[1], s[2], s[3], true)


var game
var _lines: Lines
var _area: Control
var buttons := {}  # MapNode -> Button


func setup(g) -> Control:
	game = g
	return self


func _ready() -> void:
	var bg := ColorRect.new()
	bg.color = Palette.BG
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(bg)
	var col := VBoxContainer.new()
	col.set_anchors_preset(Control.PRESET_FULL_RECT)
	col.add_theme_constant_override("separation", 8)
	add_child(col)
	col.add_child(Widgets.title("Normandy, June 1944", Palette.TEXT, 26))
	col.add_child(Widgets.body("Choose your route through the hedgerows. The Tiger waits at the end.", Palette.TEXT_DIM, 0))
	_area = Control.new()
	_area.size_flags_vertical = Control.SIZE_EXPAND_FILL
	col.add_child(_area)
	var legend := Widgets.row(18)
	for t in ["skirmish", "elite", "aid", "quartermaster", "intel"]:
		var swatch := ColorRect.new()
		swatch.color = TYPE_STYLE[t][1]
		swatch.custom_minimum_size = Vector2(14, 14)
		swatch.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		legend.add_child(swatch)
		legend.add_child(Palette.label("%s = %s" % [TYPE_STYLE[t][0], game.NODE_NAMES[t]], 13, Palette.TEXT_DIM))
	col.add_child(legend)
	var pad := Control.new()
	pad.custom_minimum_size = Vector2(0, 8)
	col.add_child(pad)

	_lines = Lines.new()
	_lines.set_anchors_preset(Control.PRESET_FULL_RECT)
	_lines.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_area.add_child(_lines)
	for node in game.run.map.all_nodes():
		var b := Button.new()
		b.text = TYPE_STYLE[node.type][0]
		b.custom_minimum_size = NODE_SIZE
		b.size = NODE_SIZE
		b.tooltip_text = game.NODE_NAMES[node.type]
		b.pressed.connect(func(): game.enter_node(node))
		_area.add_child(b)
		buttons[node] = b
	_area.resized.connect(_layout)
	_layout.call_deferred()


func _node_pos(node: RunMap.MapNode) -> Vector2:
	var w := _area.size.x
	var h := _area.size.y
	var x := 40.0 + node.row * (w - 80.0 - NODE_SIZE.x) / RunMap.FLOORS
	var y := h / 2 if node.type == "boss" else 20.0 + node.col * (h - 40.0 - NODE_SIZE.y) / (RunMap.WIDTH - 1)
	if node.type == "boss":
		y -= NODE_SIZE.y / 2
	return Vector2(x, y)


func _layout() -> void:
	var available: Array = game.run.available_nodes()
	var path_nodes := {}
	for node in buttons:
		var b: Button = buttons[node]
		b.position = _node_pos(node)
		var color: Color = TYPE_STYLE[node.type][1]
		var is_next := available.has(node)
		var is_here: bool = node == game.run.position
		var fill := color if (is_next or is_here or node.visited) else color.darkened(0.55)
		var border := Palette.HIGHLIGHT if is_next else (Palette.TEXT if is_here else fill.darkened(0.3))
		for state in ["normal", "hover", "pressed", "disabled", "focus"]:
			var sb := Palette.box(fill.lightened(0.15) if state == "hover" else fill, border, 3 if is_next or is_here else 1, 8)
			b.add_theme_stylebox_override(state, sb)
		b.add_theme_color_override("font_color", Palette.INK)
		b.add_theme_color_override("font_disabled_color", Palette.INK.lightened(0.3))
		b.disabled = not is_next
		if node.visited:
			path_nodes[node] = true
	_lines.segments.clear()
	for node in buttons:
		for n in node.next:
			var walked := path_nodes.has(node) and path_nodes.has(n)
			var color := Palette.TEXT if walked else Palette.PANEL.lightened(0.15)
			_lines.segments.append([_node_pos(node) + NODE_SIZE / 2, _node_pos(n) + NODE_SIZE / 2, color, 3.0 if walked else 2.0])
	_lines.queue_redraw()
