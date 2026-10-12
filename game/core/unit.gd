class_name Unit
extends RefCounted
## A combatant on the battlefield: a deployed player squad or an enemy.

## Statuses that count down by 1 at the end of their owner's turn.
const TIMED_STATUSES := ["suppressed", "exposed"]

var id := ""
var name := ""
var side := -1
var row := ""
var slot := -1
var max_hp := 1
var hp := 1
var firepower := 0
var shots := 1
var attack := "direct"
var armor := 0
var cover := 0
var entrenched := false
var traits := {}
## status name -> stacks. suppressed/exposed: turns; pinned: skip next action; bleeding: damage per turn.
var statuses := {}

## Player units: the card that deployed this unit.
var card: CardInstance = null

## Enemies: intent cycle from data, and the intent currently shown.
var intents: Array = []
var intent_index := 0
var intent: Dictionary = {}
## Enemies: ops run once when first brought to half HP or below.
var on_half_hp: Array = []
var half_hp_triggered := false


func is_on_field() -> bool:
	return row != "" and hp > 0


func status(status_name: String) -> int:
	return int(statuses.get(status_name, 0))


func add_status(status_name: String, amount: int) -> void:
	statuses[status_name] = status(status_name) + amount
	if statuses[status_name] <= 0:
		statuses.erase(status_name)


func heal(amount: int) -> int:
	var before := hp
	hp = mini(max_hp, hp + amount)
	return hp - before


func tick_timed_statuses() -> void:
	for s in TIMED_STATUSES:
		if statuses.has(s):
			add_status(s, -1)
