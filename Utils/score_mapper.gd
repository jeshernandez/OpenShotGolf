class_name ScoreMapper

static func map_score(strokes: int, par: int) -> String:
	var s := maxi(strokes, 1)
	var p := maxi(par, 1)
	if s == 1:
		return "Hole in One"
	var diff := s - p
	match diff:
		-4: return "Condor"
		-3: return "Albatross"
		-2: return "Eagle"
		-1: return "Birdie"
		0:  return "Par"
		1:  return "Bogey"
		2:  return "Double Bogey"
		3:  return "Triple Bogey"
		_:
			# diff <= -5 (beyond Condor) or diff >= 4 (worse than Triple Bogey).
			if diff < 0:
				return "%d Under Par" % (-diff)
			return "+%d" % diff
