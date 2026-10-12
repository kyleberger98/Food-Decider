extends SceneTree
## Renders one screen of the game to a PNG (needs a display, e.g. xvfb-run).
## godot --path game -s res://tools/screenshot.gd -- out.png [map|combat|boss|reward|rest|shop|event|workshop|merge|end] [seed]


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var out: String = args[0] if args.size() > 0 else "screenshot.png"
	var mode: String = args[1] if args.size() > 1 else "combat"
	var seed_value := int(args[2]) if args.size() > 2 else 1
	var game = load("res://ui/main.tscn").instantiate()
	root.add_child(game)
	await process_frame
	game.new_run(seed_value)
	var run: RunState = game.run
	match mode:
		"map":
			# Walk a few floors so the map shows a travelled path.
			for i in 4:
				run.enter(run.available_nodes()[0])
			game.show_map()
		"combat", "boss":
			if mode == "boss":
				for i in RunMap.FLOORS:
					run.enter(run.available_nodes()[0])
			game.enter_node(run.available_nodes()[0])
			await process_frame
			_autoplay(game.screen)
			game.screen._on_end_turn()
			_autoplay(game.screen)
			await create_timer(2.5).timeout
			if not game.combat.hand.is_empty():
				game.screen._select(game.combat.hand[0])
		"reward":
			run.enter(run.available_nodes()[0])
			var c := run.start_combat("bunker_line")
			for e in c.field.units(Battlefield.ENEMY):
				c._lose_hp(e, 999)
			game._set_screen(game.RewardScreen.new().setup(game, run.finish_combat(c, "elite")))
		"rest":
			run.resolve = 31
			game._set_screen(game.RestScreen.new().setup(game))
		"shop":
			run.supply = 180
			game._set_screen(game.ShopScreen.new().setup(game))
		"event":
			game._set_screen(game.EventScreen.new().setup(game, "resistance"))
		"workshop", "merge":
			run.attachments = ["scope", "sandbags", "field_radio"]
			run.add_card("bazooka")
			game._set_screen(game.WorkshopScreen.new().setup(game))
			if mode == "merge":
				await process_frame
				var rifle = run.deck.filter(func(c): return c.id == "rifle_squad")[0]
				var bz = run.deck.filter(func(c): return c.id == "bazooka")[0]
				rifle.attachment = "bayonet"
				game.screen._show_preview(rifle, bz)
		"end":
			run.won = true
			game.show_end()
	for i in 20:
		await process_frame
	root.get_texture().get_image().save_png(out)
	print("saved ", out)
	quit()


## Deploy whatever units fit, then cover the first unit.
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
