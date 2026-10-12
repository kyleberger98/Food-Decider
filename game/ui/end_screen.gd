extends Control
## Run over: victory or defeat summary.

var game


func setup(g) -> Control:
	game = g
	return self


func _ready() -> void:
	var run: RunState = game.run
	var col := Widgets.centered_column(self)
	if run.won:
		col.add_child(Widgets.title("The Tiger is burning", Palette.HEAL, 40))
		col.add_child(Widgets.body("Your company holds the crossroads. Normandy is one hedgerow closer to freedom."))
	else:
		col.add_child(Widgets.title("Your line has broken", Palette.DAMAGE, 40))
		col.add_child(Widgets.body("The company falls back. There will be another push."))
	col.add_child(Widgets.body("Floor reached: %d   Fights won: %d   Deck: %d cards   Commendations: %d" % [
		run.floor_number(), run.fights_won, run.deck.size(), run.relics.size()], Palette.TEXT))
	col.add_child(Widgets.button("New Run", func(): game.new_run(randi()), 200))
