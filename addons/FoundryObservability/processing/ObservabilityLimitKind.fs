namespace foundry.observability.processing

## Stable signal admission limit families.
enum_name ObservabilityLimitKind:
	NONE = 0
	PER_FRAME = 1
	REPEATED = 2
	WINDOW = 3
	LEGACY_LOG_WINDOW = 4
