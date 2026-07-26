namespace foundry.observability

## Immutable diagnostic information for an attachment rejected during capture.
class_name ObservabilityAttachmentFailure
extends RefCounted

const MISSING_FILE: StringName = &"missing_file"
const UNREADABLE_FILE: StringName = &"unreadable_file"
const OVERSIZED: StringName = &"oversized"
const PLATFORM_UNAVAILABLE: StringName = &"platform_unavailable"
const PROVIDER_REJECTED: StringName = &"provider_rejected"

final var _handle: String
final var _filename: String
final var _reason: StringName
final var _error: int


func _init(
		p_handle: String = "",
		p_filename: String = "",
		p_reason: StringName = PROVIDER_REJECTED,
		p_error: int = Error.FAILED,
) -> void:
	_handle = p_handle
	_filename = p_filename
	_reason = p_reason
	_error = p_error


func handle() -> String:
	return _handle


func filename() -> String:
	return _filename


func reason() -> StringName:
	return _reason


func error() -> int:
	return _error


func duplicate() -> ObservabilityAttachmentFailure:
	return ObservabilityAttachmentFailure.new(
			_handle,
			_filename,
			_reason,
			_error,
		)
