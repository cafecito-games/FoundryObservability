namespace foundry.observability

## Explicit provider-neutral application user identity.
class_name ObservabilityUser
extends RefCounted

final var _application_user_id: String
final var _display_name: String
final var _contact_email: String


func _init(
		p_application_user_id: String = "",
		p_display_name: String = "",
		p_contact_email: String = "",
) -> void:
	_application_user_id = p_application_user_id
	_display_name = p_display_name
	_contact_email = p_contact_email


func application_user_id() -> String:
	return _application_user_id


func display_name() -> String:
	return _display_name


func contact_email() -> String:
	return _contact_email


func is_valid() -> bool:
	if _application_user_id.is_empty() \
			and _display_name.is_empty() \
			and _contact_email.is_empty():
		return false
	return _is_valid_optional_identity(_application_user_id) \
			and _is_valid_optional_identity(_display_name) \
			and _is_valid_optional_identity(_contact_email)


func _is_valid_optional_identity(value: String) -> bool:
	if value.is_empty():
		return true
	if value.strip_edges() != value or _has_surrounding_whitespace(value):
		return false
	return not _has_control_character(value)


func _has_surrounding_whitespace(value: String) -> bool:
	return _is_whitespace(value.unicode_at(0)) \
			or _is_whitespace(value.unicode_at(value.length() - 1))


func _is_whitespace(codepoint: int) -> bool:
	return (codepoint >= 9 and codepoint <= 13) \
			or codepoint == 32 \
			or codepoint == 133 \
			or codepoint == 160 \
			or codepoint == 5760 \
			or (codepoint >= 8192 and codepoint <= 8202) \
			or codepoint == 8232 \
			or codepoint == 8233 \
			or codepoint == 8239 \
			or codepoint == 8287 \
			or codepoint == 12288


func _has_control_character(value: String) -> bool:
	for index: int in range(value.length()):
		var codepoint: int = value.unicode_at(index)
		if codepoint < 32 or (codepoint >= 127 and codepoint <= 159):
			return true
	return false
