extends Control
## After a won fight: supply, any Commendation, lost cards, and a pick-1-of-3 card reward.

var game
var rewards: Dictionary


func setup(g, r: Dictionary) -> Control:
	game = g
	rewards = r
	return self


func _ready() -> void:
	var col := Widgets.centered_column(self)
	col.add_child(Widgets.title("Victory", Palette.HEAL, 36))
	col.add_child(Widgets.body("+%d Supply" % rewards["supply"], Palette.HIGHLIGHT))
	if rewards["relic"] != "":
		col.add_child(Widgets.body("Commendation earned: " + Widgets.relic_text(rewards["relic"]), Palette.COVER))
	if not rewards["lost_cards"].is_empty():
		var names := PackedStringArray(rewards["lost_cards"].map(func(c): return c.display_name()))
		col.add_child(Widgets.body("Lost for good (3 casualties): " + ", ".join(names), Palette.DAMAGE))
	col.add_child(Widgets.body("Choose a card to add to your deck:", Palette.TEXT))
	var row := Widgets.row()
	for id in rewards["cards"]:
		var cv := CardView.new().setup(CardDB.make_card(id))
		cv.draggable = false
		cv.pressed.connect(func(v: CardView):
			game.run.add_card(v.card.id)
			game.show_map())
		row.add_child(cv)
	col.add_child(row)
	col.add_child(Widgets.button("Skip", game.show_map, 160))
