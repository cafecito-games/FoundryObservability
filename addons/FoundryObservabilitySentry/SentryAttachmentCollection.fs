namespace foundry.observability.sentry

import foundry.observability

## Isolated native attachment payloads and typed failures.
final class_name SentryAttachmentCollection
extends RefCounted

final var _attachments: Array[Dictionary]
final var _failures: Array[ObservabilityAttachmentFailure]


func _init(
		p_attachments: Array[Dictionary] = [],
		p_failures: Array[ObservabilityAttachmentFailure] = [],
) -> void:
	_attachments = _copy_attachments(p_attachments)
	_failures = _copy_failures(p_failures)


func attachments() -> Array[Dictionary]:
	return _copy_attachments(_attachments)


func failures() -> Array[ObservabilityAttachmentFailure]:
	return _copy_failures(_failures)


func _copy_attachments(values: Array[Dictionary]) -> Array[Dictionary]:
	var copied: Array[Dictionary] = []
	for value: Dictionary in values:
		copied.append(value.duplicate(true))
	return copied


func _copy_failures(
		values: Array[ObservabilityAttachmentFailure],
) -> Array[ObservabilityAttachmentFailure]:
	var copied: Array[ObservabilityAttachmentFailure] = []
	for value: ObservabilityAttachmentFailure in values:
		copied.append(value.duplicate())
	return copied
