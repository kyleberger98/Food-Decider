extends Control
## Field Aid Station: heal, promote (upgrade) a card, or rotate troops (clear a card's casualties).

var game


func setup(g) -> Control:
	game = g
	return self


func _ready() -> void:
	var run: RunState = game.run
	var col := Widgets.centered_column(self)
	col.add_child(Widgets.title("Field Aid Station", Palette.HEAL))
	col.add_child(Widgets.body("A quiet farmhouse behind the lines. You have time for one thing."))
	col.add_child(Widgets.button("Rest: heal %d Resolve" % run.rest_heal_amount(), func():
		run.rest_heal()
		game.show_map(), 360))
	var promotable := func(c): return not c.upgraded
	var b := Widgets.button("Promote: upgrade a card", func():
		game.open_deck("Choose a card to promote", promotable, func(c):
			run.promote(c)
			game.show_map()), 360)
	b.disabled = run.deck.filter(promotable).is_empty()
	col.add_child(b)
	var worn := func(c): return c.casualties > 0
	var r := Widgets.button("Rotate troops: clear a card's casualties", func():
		game.open_deck("Choose a card to rotate out", worn, func(c):
			run.rotate_troops(c)
			game.show_map()), 360)
	r.disabled = run.deck.filter(worn).is_empty()
	col.add_child(r)
