extends Control
## Intel: a short text event with choices.

var game
var event_id: String
var _col: VBoxContainer


func setup(g, id: String) -> Control:
	game = g
	event_id = id
	return self


func _ready() -> void:
	var ev: Dictionary = CardDB.events()[event_id]
	_col = Widgets.centered_column(self)
	_col.add_child(Widgets.title(ev["name"], Palette.COVER))
	_col.add_child(Widgets.body(ev["text"], Palette.TEXT))
	var choices: Array = ev["choices"]
	for i in choices.size():
		var b := Widgets.button(choices[i]["label"], func(): _choose(i), 520)
		b.disabled = not game.run.choice_available(choices[i])
		_col.add_child(b)


func _choose(index: int) -> void:
	var lines: PackedStringArray = game.run.apply_event_choice(event_id, index)
	for c in _col.get_children():
		if c is Button:
			c.queue_free()
	_col.add_child(Widgets.body("\n".join(lines) if not lines.is_empty() else "Nothing happens.", Palette.HIGHLIGHT))
	game.refresh_bar()
	_col.add_child(Widgets.button("Continue", func():
		if game.run.is_over():
			game.show_end()
		else:
			game.show_map(), 160))
