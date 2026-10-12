extends SceneTree
## Headless test runner. Use tests/run.sh, which also fails on runtime script errors.


func _init() -> void:
	var total := 0
	var failed: PackedStringArray = []
	var dir := DirAccess.open("res://tests")
	var files := Array(dir.get_files()).filter(func(f): return f.begins_with("test_") and f.ends_with(".gd") and f != "test_case.gd")
	files.sort()
	for f in files:
		var script: GDScript = load("res://tests/" + f)
		if script == null or not script.can_instantiate():
			failed.append("%s: failed to load" % f)
			continue
		for m in script.get_script_method_list():
			var name: String = m["name"]
			if not name.begins_with("test_"):
				continue
			var t: TestCase = script.new()
			t.current_test = f.get_basename() + "." + name
			t.call(name)
			total += 1
			if t.failures.is_empty():
				print("  ok   ", t.current_test)
			else:
				print("  FAIL ", t.current_test)
				failed.append_array(t.failures)
	print("")
	for msg in failed:
		print("  - ", msg)
	print("%d tests, %d failures" % [total, failed.size()])
	quit(1 if not failed.is_empty() else 0)
