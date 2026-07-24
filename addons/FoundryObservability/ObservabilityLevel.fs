namespace foundry.observability

## Shared severity values for provider-neutral observability events.
class_name ObservabilityLevel
extends RefCounted

## Most verbose severity; numeric value 10.
const TRACE: int = 10
## Debug severity; numeric value 20.
const DEBUG: int = 20
## Informational severity; numeric value 30.
const INFO: int = 30
## Warning severity; numeric value 40.
const WARN: int = 40
## Error severity; numeric value 50.
const ERROR: int = 50
## Fatal severity; numeric value 60.
const FATAL: int = 60


## Returns an uppercase severity name, or LEVEL(value) for an unknown value.
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
