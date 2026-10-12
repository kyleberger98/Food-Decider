extends Control
## Quartermaster: buy cards and Commendations, or pay to remove a card.

var game
var stock: Dictionary
var _remove_used := false
var _col: VBoxContainer


func setup(g) -> Control:
	game = g
	stock = g.run.shop_stock()
	return self


func _ready() -> void:
	_col = Widgets.centered_column(self, 14)
	_rebuild()


func _rebuild() -> void:
	for c in _col.get_children():
		c.queue_free()
	var run: RunState = game.run
	_col.add_child(Widgets.title("Quartermaster", Palette.HIGHLIGHT))
	_col.add_child(Widgets.body("Supply is tight, but everything has a price."))
	var row := Widgets.row(12)
	for item in stock["cards"]:
		var box := VBoxContainer.new()
		var cv := CardView.new().setup(CardDB.make_card(item["id"]))
		cv.draggable = false
		box.add_child(cv)
		var buy := Widgets.button("Sold" if item.get("sold") else "Buy %d" % item["price"], func():
			if run.buy_card(item["id"], item["price"]):
				item["sold"] = true
				_changed(), 120)
		buy.disabled = item.get("sold", false) or not run.can_afford(item["price"])
		box.add_child(buy)
		row.add_child(box)
	_col.add_child(row)
	for item in stock["attachments"]:
		var att: Dictionary = CardDB.attachments()[item["id"]]
		var aline := Widgets.row(12)
		aline.add_child(Widgets.body("Attachment: %s. %s" % [att["name"], att["text"]], Palette.TYPE_COLORS["doctrine"].lightened(0.3), 520))
		var abuy := Widgets.button("Sold" if item.get("sold") else "Buy %d" % item["price"], func():
			if run.buy_attachment(item["id"], item["price"]):
				item["sold"] = true
				_changed(), 120)
		abuy.disabled = item.get("sold", false) or not run.can_afford(item["price"])
		aline.add_child(abuy)
		_col.add_child(aline)
	for item in stock["relics"]:
		var line := Widgets.row(12)
		line.add_child(Widgets.body(Widgets.relic_text(item["id"]), Palette.COVER, 520))
		var buy := Widgets.button("Sold" if item.get("sold") else "Buy %d" % item["price"], func():
			if run.buy_relic(item["id"], item["price"]):
				item["sold"] = true
				_changed(), 120)
		buy.disabled = item.get("sold", false) or not run.can_afford(item["price"])
		line.add_child(buy)
		_col.add_child(line)
	var remove := Widgets.button("Remove a card from your deck (%d)" % stock["remove_price"], func():
		game.open_deck("Choose a card to remove", Callable(), func(c):
			if run.remove_card(c, stock["remove_price"]):
				_remove_used = true
				_changed()), 360)
	remove.disabled = _remove_used or not run.can_afford(stock["remove_price"])
	_col.add_child(remove)
	_col.add_child(Widgets.button("Leave", game.show_map, 160))


func _changed() -> void:
	game.refresh_bar()
	_rebuild()
