namespace foundry.observability.processing

## Stable payload-free processing reasons.
enum_name ObservabilityProcessingReason:
	NONE = 0
	PROCESSOR = 1
	SAMPLED = 2
	RATE_LIMITED = 3
	RECURSIVE = 4
	INVALID_PROCESSOR_RESULT = 5
	REDACTION_FAILED = 6
	INVALID_PAYLOAD = 7
	PROVIDER_REJECTED = 8
	STALE_GENERATION = 9
