extends ColorRect
## Modal overlay showing cards. With on_pick set, clicking a card picks it and closes.

var cards: Array = []
var heading := ""
var on_pick: Callable


func setup(card_list: Array, title_text: String, pick: Callable = Callable()) -> Control:
	cards = card_list
	heading = title_text
	on_pick = pick
	return self


func _ready() -> void:
	color = Color(0, 0, 0, 0.85)
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var margin := MarginContainer.new()
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]:
		margin.add_theme_constant_override("margin_" + side, 32)
	add_child(margin)
	var col := VBoxContainer.new()
	col.add_theme_constant_override("separation", 12)
	margin.add_child(col)
	col.add_child(Widgets.title(heading, Palette.TEXT, 24))
	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	col.add_child(scroll)
	var center := CenterContainer.new()
	center.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(center)
	var grid := GridContainer.new()
	grid.columns = 8
	grid.add_theme_constant_override("h_separation", 10)
	grid.add_theme_constant_override("v_separation", 10)
	center.add_child(grid)
	for card in cards:
		var cv := CardView.new().setup(card)
		cv.draggable = false
		if on_pick.is_valid():
			cv.pressed.connect(func(v: CardView):
				queue_free()
				on_pick.call(v.card))
		grid.add_child(cv)
	if cards.is_empty():
		col.add_child(Widgets.body("No cards."))
	col.add_child(Widgets.button("Close" if not on_pick.is_valid() else "Cancel", queue_free, 160))
