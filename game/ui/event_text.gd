class_name EventText
extends RefCounted
## Turns combat events into log lines. Returns "" for events not worth logging.


static func describe(e: Dictionary) -> String:
	match e["type"]:
		"turn_started":
			return "[b]— Turn %d —[/b]" % e["turn"]
		"card_played":
			return "You play [b]%s[/b]." % e["card"].display_name()
		"unit_deployed":
			return "%s deployed to the %s row." % [e["unit"].name, e["unit"].row]
		"damaged":
			var blocked := " (%d blocked)" % e["blocked"] if e["blocked"] > 0 else ""
			return "%s hits %s for %d%s." % [_who(e["source"]), e["target"].name, e["amount"], blocked]
		"missed":
			return "%s fires into smoke and misses %s." % [_who(e["source"]), e["target"].name]
		"hq_damaged":
			return "[color=#ff6b5a]%s hits your HQ for %d.[/color]" % [_who(e["source"]), e["amount"]]
		"unit_killed":
			var u: Unit = e["unit"]
			if u.side == Battlefield.PLAYER:
				var lost := " Card lost!" if u.card.casualties >= Combat.MAX_CASUALTIES else ""
				return "[color=#ff6b5a]%s is down (casualties %d/%d).%s[/color]" % [u.name, u.card.casualties, Combat.MAX_CASUALTIES, lost]
			return "[color=#8fd16a]%s destroyed.[/color]" % u.name
		"healed":
			return "%s healed %d." % [e["unit"].name, e["amount"]] if e["amount"] > 0 else ""
		"cover_gained":
			return "%s gains %d Cover." % [e["unit"].name, e["amount"]]
		"status_applied":
			return "%s is %s (%d)." % [e["unit"].name, String(e["status"]).capitalize(), e["amount"]]
		"unit_moved":
			return "%s moves to the %s row." % [e["unit"].name, e["unit"].row]
		"smoke":
			return "Smoke covers the %s row." % e["row"]
		"pinned_skip":
			return "%s is pinned and can't act." % e["unit"].name
		"zippo":
			return "[color=#d9b44a]%s should be dead, but the Lucky Zippo stopped the bullet![/color]" % e["unit"].name
		"summoned":
			return "[color=#ff6b5a]Enemy reinforcements: %s arrives.[/color]" % e["unit"].name
		"half_hp":
			return "%s is badly damaged!" % e["unit"].name
		"morale":
			return "[color=#ff6b5a]Losing men shakes you: -%d Resolve.[/color]" % e["amount"]
		"withdrawn":
			return "%s is pulled off the line." % e["unit"].name
		"reshuffled":
			return "Discard pile shuffled into the draw pile."
		"combat_won":
			return "[color=#8fd16a][b]Victory![/b][/color]"
		"combat_lost":
			return "[color=#ff6b5a][b]Your line has broken.[/b][/color]"
	return ""


static func _who(source: Variant) -> String:
	return source.name if source is Unit else "Your order"
