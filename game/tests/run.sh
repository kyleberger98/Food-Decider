#!/usr/bin/env bash
# Runs the headless test suite. Usage: GODOT=/path/to/godot game/tests/run.sh
set -uo pipefail
cd "$(dirname "$0")/.."
GODOT="${GODOT:-godot}"
"$GODOT" --headless --path . --import >/dev/null 2>&1
out="$("$GODOT" --headless --path . -s res://tests/run_tests.gd 2>&1)"
code=$?
echo "$out" | grep -v '^Godot Engine'
# GDScript runtime errors don't fail a test on their own, so treat any as a failure.
if echo "$out" | grep -q 'SCRIPT ERROR'; then
	echo "Script errors detected"
	exit 1
fi
exit $code
