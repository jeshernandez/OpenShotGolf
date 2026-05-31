class_name GolfUnits
extends RefCounted
## Single source of truth for distance unit conversions shared across the ball,
## player, HUD formatter, and course play. Keep the literals here so a future
## correction is a one-line change instead of editing every call site.

const FEET_PER_METER := 3.28084
const YARDS_PER_METER := 1.09361
