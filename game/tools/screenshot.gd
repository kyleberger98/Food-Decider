extends SceneTree
## Renders the combat screen after a few scripted plays and saves a PNG (needs a display, e.g. xvfb-run).
## godot --path game -s res://tools/screenshot.gd -- out.png [seed] [turns] [victory]


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var out: String = args[0] if args.size() > 0 else "screenshot.png"
	var seed_value := int(args[1]) if args.size() > 1 else 1
	var turns := int(args[2]) if args.size() > 2 else 1
	var screen = load("res://ui/main.tscn").instantiate()
	root.add_child(screen)
	await process_frame
	screen.start_run(seed_value)
	for t in turns:
		_autoplay(screen)
		if t < turns - 1:
			screen._on_end_turn()
	await create_timer(2.5).timeout  # let damage numbers fade
	if args.size() > 3 and args[3] == "victory":
		for e in screen.combat.field.units(Battlefield.ENEMY):
			screen.combat._lose_hp(e, 999)
		screen._after_action()
	elif not screen.combat.hand.is_empty():
		# Leave a card selected so target highlights show.
		screen._select(screen.combat.hand[0])
	for i in 10:
		await process_frame
	root.get_texture().get_image().save_png(out)
	print("saved ", out)
	quit()


## Deploy whatever units fit, then play cover on the first unit.
func _autoplay(screen) -> void:
	var c: Combat = screen.combat
	for card in c.hand.duplicate():
		if card.type() == "unit" and card.cost() <= c.cp:
			for row in card.def["unit"]["rows"]:
				var s := c.field.first_free(Battlefield.PLAYER, row)
				if s >= 0:
					screen._play(card, {"row": row, "slot": s})
					break
	for card in c.hand.duplicate():
		if card.id == "dig_in" and card.cost() <= c.cp and not c.field.units(Battlefield.PLAYER).is_empty():
			screen._play(card, {"unit": c.field.units(Battlefield.PLAYER)[0]})
