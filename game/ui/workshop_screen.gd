extends Control
## Workshop: fit an attachment from your kit to a card, or merge two cards into one.
## One job per visit (two with the Spare Parts Crate).

var game
var jobs_left := 1
var _col: VBoxContainer
var _preview: Control


func setup(g) -> Control:
	game = g
	jobs_left = g.run.workshop_actions()
	return self


func _ready() -> void:
	_col = Widgets.centered_column(self, 10)
	_rebuild()


func _rebuild() -> void:
	for c in _col.get_children():
		c.queue_free()
	var run: RunState = game.run
	_col.add_child(Widgets.title("Workshop", Color("9a7fb8")))
	if jobs_left <= 0:
		_col.add_child(Widgets.body("The armorers have done what they can."))
		_col.add_child(Widgets.button("Continue", game.show_map, 200))
		return
	_col.add_child(Widgets.body("A battalion armorer and a jeep full of spare parts. They have time for %d job%s." % [
		jobs_left, "" if jobs_left == 1 else "s"]))

	_col.add_child(Widgets.title("Fit an attachment", Palette.TEXT, 18))
	if run.attachments.is_empty():
		_col.add_child(Widgets.body("Your kit is empty. Attachments drop from fights and events, or can be bought at the Quartermaster."))
	var seen := {}
	for att in run.attachments:
		if seen.has(att):
			continue
		seen[att] = true
		var info: Dictionary = CardDB.attachments()[att]
		var count: int = run.attachments.count(att)
		var label := "%s%s: %s" % [info["name"], (" x%d" % count) if count > 1 else "", info["text"]]
		var fits := func(c): return Crafting.attachment_fits(c, att)
		var b := Widgets.button(label, func():
			game.open_deck("Fit %s to which card?" % info["name"], fits, func(card):
				if run.attach(card, att):
					_job_done()), 620)
		b.disabled = run.deck.filter(fits).is_empty()
		_col.add_child(b)

	_col.add_child(Widgets.title("Merge two cards", Palette.TEXT, 18))
	var recipes := run.available_recipes()
	if not recipes.is_empty():
		var lines := PackedStringArray(recipes.map(func(r): return "%s + %s = %s" % [r["a"], r["b"], r["result"]]))
		var rl := Widgets.body("Recipes you can make now:  " + "   |   ".join(lines), Palette.HIGHLIGHT, 900)
		rl.add_theme_font_size_override("font_size", 14)
		_col.add_child(rl)
	var explain := Widgets.body("Any two cards of the same type can also merge: a unit gets the first card's stats plus half of the second's; orders and doctrines do both effects. Merging thins your deck; merged cards can't merge again.", Palette.TEXT_DIM, 900)
	explain.add_theme_font_size_override("font_size", 14)
	_col.add_child(explain)
	var mergeable := func(c): return not c.is_merged()
	_col.add_child(Widgets.button("Choose two cards to merge", func():
		game.open_deck("Merge: choose the first card", mergeable, _pick_second), 300))
	_col.add_child(Widgets.button("Leave", game.show_map, 160))


func _pick_second(first: CardInstance) -> void:
	var ok := func(c): return Crafting.merge_error(first, c) == ""
	game.open_deck("Merge %s with...?" % first.display_name(), ok, func(second): _show_preview(first, second))


func _show_preview(a: CardInstance, b: CardInstance) -> void:
	var preview: Dictionary = game.run.preview_merge(a, b)
	var result: CardInstance = preview["card"]
	_preview = ColorRect.new()
	_preview.color = Color(0, 0, 0, 0.85)
	_preview.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(_preview)
	var col := Widgets.centered_column(_preview, 16, false)
	var recipe := Crafting.recipe_for(a, b) != ""
	col.add_child(Widgets.title("Known recipe!" if recipe else "Field modification", Palette.HIGHLIGHT if recipe else Palette.TEXT, 24))
	var row := Widgets.row(16)
	for item in [a, "+", b, "=", result]:
		if item is String:
			row.add_child(Widgets.title(item, Palette.TEXT, 40))
		else:
			var cv := CardView.new().setup(item)
			cv.draggable = false
			row.add_child(cv)
	col.add_child(row)
	var note := "The two cards are removed from your deck and replaced by the new one."
	if not preview["returned"].is_empty():
		note += " Returned to your kit: " + ", ".join(PackedStringArray(preview["returned"].map(func(x): return CardDB.attachments()[x]["name"])))
	col.add_child(Widgets.body(note))
	var buttons := Widgets.row(16)
	buttons.add_child(Widgets.button("Merge", func():
		game.run.merge_cards(a, b)
		_preview.queue_free()
		_job_done(), 160))
	buttons.add_child(Widgets.button("Cancel", _preview.queue_free, 160))
	col.add_child(buttons)


func _job_done() -> void:
	jobs_left -= 1
	game.refresh_bar()
	_rebuild()
