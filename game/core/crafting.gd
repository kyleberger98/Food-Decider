class_name Crafting
extends RefCounted
## Workshop rules: fitting attachments to cards and merging two cards into one.
##
## Merging: a recipe pair makes a named card. Otherwise two cards of the same type merge
## generically: A's stats plus half of B's (units), or both cards' effects (orders/doctrines).
## A card that came out of a merge can't be merged again.


static func attachment_fits(card: CardInstance, att_id: String) -> bool:
	var d := card.def
	match CardDB.attachments()[att_id]["fits"]:
		"any":
			return true
		"unit":
			return card.type() == "unit"
		"unit_direct":
			return card.type() == "unit" and d["unit"].get("attack", "direct") == "direct" and int(d["unit"].get("fp", 0)) > 0
		"unit_fp2":
			return card.type() == "unit" and int(d["unit"].get("fp", 0)) >= 2
	return false


static func recipe_for(a: CardInstance, b: CardInstance) -> String:
	for r in CardDB.recipes():
		if (r["a"] == a.id and r["b"] == b.id) or (r["a"] == b.id and r["b"] == a.id):
			return r["result"]
	return ""


## "" if the two cards can merge, otherwise the reason.
static func merge_error(a: CardInstance, b: CardInstance) -> String:
	if a == b:
		return "Pick two different cards."
	if a.is_merged() or b.is_merged():
		return "Merged cards can't be merged again."
	if recipe_for(a, b) != "":
		return ""
	if a.type() != b.type():
		return "Only cards of the same type merge (unless they're a known recipe)."
	if a.type() != "unit":
		var ta: String = a.def.get("target", "none")
		var tb: String = b.def.get("target", "none")
		if ta != "none" and tb != "none" and ta != tb:
			return "These two cards need different kinds of targets."
		var ra: String = a.def.get("requires_row", "")
		var rb: String = b.def.get("requires_row", "")
		if ra != "" and rb != "" and ra != rb:
			return "These two cards need units in different rows."
	return ""


## Builds the merged card (does not touch the deck). Casualties carry over (the worse of the two).
## Attachments are handled by RunState.
static func merge(a: CardInstance, b: CardInstance) -> CardInstance:
	assert(merge_error(a, b) == "")
	var recipe := recipe_for(a, b)
	var card: CardInstance
	if recipe != "":
		card = CardDB.make_card(recipe, a.upgraded or b.upgraded)
	else:
		card = build_generic(a.id, a.upgraded, b.id, b.upgraded)
	card.components = [[a.id, a.upgraded], [b.id, b.upgraded]]
	card.casualties = maxi(a.casualties, b.casualties)
	return card


static func build_generic(a_id: String, a_up: bool, b_id: String, b_up: bool) -> CardInstance:
	var a := CardDB.card_def(a_id, a_up)
	var b := CardDB.card_def(b_id, b_up)
	var d := {
		"name": "%s & %s" % [a["name"], b["name"]],
		"type": a["type"],
		"cost": maxi(int(a["cost"]), int(b["cost"])),
		"rarity": "merged",
	}
	if a["type"] == "unit":
		var ua: Dictionary = a["unit"]
		var ub: Dictionary = b["unit"]
		var rows: Array = ua["rows"].filter(func(r): return ub["rows"].has(r))
		var traits: Dictionary = ua.get("traits", {}).duplicate()
		for t in ub.get("traits", {}):
			traits[t] = maxi(int(traits.get(t, 0)), int(ub["traits"][t]))
		var u := {
			"hp": int(ua["hp"]) + int(ub["hp"]) / 2,
			"fp": int(ua.get("fp", 0)) + int(ub.get("fp", 0)) / 2,
			"shots": maxi(int(ua.get("shots", 1)), int(ub.get("shots", 1))),
			"attack": ua.get("attack", "direct"),
			"rows": rows if not rows.is_empty() else ua["rows"],
			"armor": maxi(int(ua.get("armor", 0)), int(ub.get("armor", 0))),
			"traits": traits,
		}
		d["unit"] = u
		d["text"] = unit_text(u)
	else:
		var target: String = a.get("target", "none")
		if target == "none":
			target = b.get("target", "none")
		d["target"] = target
		d["effects"] = a.get("effects", []) + b.get("effects", [])
		var req: String = a.get("requires_row", b.get("requires_row", ""))
		if req != "":
			d["requires_row"] = req
		d["expend"] = a.get("expend", false) or b.get("expend", false)
		var triggers := {}
		for src in [a, b]:
			for k in src.get("triggers", {}):
				triggers[k] = triggers.get(k, []) + src["triggers"][k]
		if not triggers.is_empty():
			d["triggers"] = triggers
		d["text"] = "%s Then: %s" % [a["text"], b["text"]]
	var card := CardInstance.new("merge:%s+%s" % [a_id, b_id], d, a_up and b_up)
	return card


static func unit_text(u: Dictionary) -> String:
	var rows: Array = u["rows"]
	var where := "" if rows.size() > 1 else " to %s row" % rows[0]
	var shots := int(u.get("shots", 1))
	var t := "Deploy%s. %d HP" % [where, int(u["hp"])]
	if int(u.get("armor", 0)) > 0:
		t += ", Armor %d" % int(u["armor"])
	if int(u.get("fp", 0)) > 0:
		t += ", fires %d %s%s" % [int(u["fp"]), String(u["attack"]).capitalize(), (" x%d" % shots) if shots > 1 else ""]
	t += "."
	var traits: Dictionary = u.get("traits", {})
	if traits.has("medic"):
		t += " End of turn: heal adjacent units %d." % int(traits["medic"])
	if traits.has("anti_armor"):
		t += " Anti-armor."
	return t


## The upgraded version of a merged card, keeping run state.
static func upgrade_merged(card: CardInstance) -> CardInstance:
	var a: Array = card.components[0]
	var b: Array = card.components[1]
	var up: CardInstance
	if card.id.begins_with("merge:"):
		up = build_generic(a[0], true, b[0], true)
	else:
		up = CardDB.make_card(card.id, true)
	up.components = [[a[0], true], [b[0], true]]
	return up
