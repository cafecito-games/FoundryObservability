namespace games.cafecito.foundryobservability

## Shared severity values for provider-neutral observability events.
class_name ObservabilityLevel
extends RefCounted

const TRACE: int = 10
const DEBUG: int = 20
const INFO: int = 30
const WARN: int = 40
const ERROR: int = 50
const FATAL: int = 60


## Returns the display name for a severity value.
static func name(level: int) -> String:
	match level:
		TRACE:
			return "TRACE"
		DEBUG:
			return "DEBUG"
		INFO:
			return "INFO"
		WARN:
			return "WARN"
		ERROR:
			return "ERROR"
		FATAL:
			return "FATAL"
	return "LEVEL(%s)" % level
