extends Control
## Root of the game: owns the RunState, the run bar, and switches between screens.

const CombatScreen := preload("res://ui/combat_screen.gd")
const MapScreen := preload("res://ui/map_screen.gd")
const RewardScreen := preload("res://ui/reward_screen.gd")
const RestScreen := preload("res://ui/rest_screen.gd")
const ShopScreen := preload("res://ui/shop_screen.gd")
const EventScreen := preload("res://ui/event_screen.gd")
const EndScreen := preload("res://ui/end_screen.gd")
const DeckPicker := preload("res://ui/deck_picker.gd")

const NODE_NAMES := {"skirmish": "Skirmish", "elite": "Strongpoint", "aid": "Field Aid Station",
	"quartermaster": "Quartermaster", "intel": "Intel", "boss": "Boss"}

var run: RunState
var screen: Control
## The fight in progress, if any.
var combat: Combat
var _tier := ""

var _floor_label: Label
var _resolve_label: Label
var _supply_label: Label
var _relic_box: HBoxContainer
var _deck_btn: Button
var _holder: Control


func _ready() -> void:
	_build()
	new_run(randi())


func new_run(seed_value: int) -> void:
	run = RunState.create(seed_value)
	combat = null
	show_map()


func show_map() -> void:
	_set_screen(MapScreen.new().setup(self))


func enter_node(node: RunMap.MapNode) -> void:
	run.enter(node)
	match node.type:
		"skirmish", "elite", "boss":
			_start_fight(node.type)
		"aid":
			_set_screen(RestScreen.new().setup(self))
		"quartermaster":
			_set_screen(ShopScreen.new().setup(self))
		"intel":
			_set_screen(EventScreen.new().setup(self, run.pick_event()))


func _start_fight(node_type: String) -> void:
	_tier = run.tier_for(node_type)
	var enc := run.pick_encounter(_tier)
	combat = run.start_combat(enc)
	combat.event_emitted.connect(func(_e): refresh_bar())
	var title := "Floor %d - %s: %s" % [run.floor_number(), NODE_NAMES[node_type], CardDB.encounters()[enc]["name"]]
	var cs := CombatScreen.new().setup(combat, title)
	cs.finished.connect(_on_combat_finished)
	_set_screen(cs)


func _on_combat_finished(c: Combat) -> void:
	var rewards := run.finish_combat(c, _tier)
	combat = null
	if run.is_over():
		show_end()
	else:
		_set_screen(RewardScreen.new().setup(self, rewards))


func show_end() -> void:
	_set_screen(EndScreen.new().setup(self))


## Opens the deck overlay. With on_pick, only cards passing `filter` are shown and one can be chosen.
func open_deck(heading := "Your deck", filter := Callable(), on_pick := Callable()) -> void:
	var cards := run.deck.filter(filter) if filter.is_valid() else run.deck.duplicate()
	add_child(DeckPicker.new().setup(cards, heading, on_pick))


func refresh_bar() -> void:
	_floor_label.text = "Floor %d/%d" % [run.floor_number(), RunMap.FLOORS + 1]
	var res := combat.resolve if combat != null else run.resolve
	_resolve_label.text = "Resolve %d/%d" % [res, run.max_resolve]
	_supply_label.text = "Supply %d" % run.supply
	_deck_btn.text = "Deck (%d)" % run.deck.size()
	for c in _relic_box.get_children():
		c.queue_free()
	for id in run.relics:
		var tag := PanelContainer.new()
		tag.add_theme_stylebox_override("panel", Palette.box(Palette.PANEL, Palette.COVER, 1, 4))
		tag.tooltip_text = Widgets.relic_text(id)
		tag.mouse_filter = Control.MOUSE_FILTER_STOP
		var l := Palette.label(CardDB.relics()[id]["name"], 12, Palette.COVER)
		l.mouse_filter = Control.MOUSE_FILTER_IGNORE
		tag.add_child(l)
		_relic_box.add_child(tag)


func _set_screen(s: Control) -> void:
	if screen != null:
		screen.queue_free()
	screen = s
	s.set_anchors_preset(Control.PRESET_FULL_RECT)
	_holder.add_child(s)
	refresh_bar()


func _build() -> void:
	var bg := ColorRect.new()
	bg.color = Palette.PANEL_DARK
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(bg)
	var col := VBoxContainer.new()
	col.set_anchors_preset(Control.PRESET_FULL_RECT)
	col.add_theme_constant_override("separation", 0)
	add_child(col)

	var bar_margin := MarginContainer.new()
	for side in ["left", "right"]:
		bar_margin.add_theme_constant_override("margin_" + side, 12)
	for side in ["top", "bottom"]:
		bar_margin.add_theme_constant_override("margin_" + side, 4)
	col.add_child(bar_margin)
	var bar := HBoxContainer.new()
	bar.add_theme_constant_override("separation", 20)
	bar_margin.add_child(bar)
	_floor_label = Palette.label("", 15, Palette.TEXT_DIM)
	_resolve_label = Palette.label("", 15, Palette.TEXT)
	_supply_label = Palette.label("", 15, Palette.HIGHLIGHT)
	for l in [_floor_label, _resolve_label, _supply_label]:
		bar.add_child(l)
	_relic_box = HBoxContainer.new()
	_relic_box.add_theme_constant_override("separation", 6)
	_relic_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	bar.add_child(_relic_box)
	_deck_btn = Button.new()
	_deck_btn.pressed.connect(func(): open_deck())
	bar.add_child(_deck_btn)

	_holder = Control.new()
	_holder.size_flags_vertical = Control.SIZE_EXPAND_FILL
	col.add_child(_holder)
