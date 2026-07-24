namespace foundry.observability

## Explicit player feedback payload kept separate from observability events.
class_name ObservabilityFeedback
extends RefCounted

var _message: String = ""
var _name: String = ""
var _contact_email: String = ""
var _associated_event_id: String = ""


## Creates feedback with a required message and optional identity and event context.
func _init(
		p_message: String = "",
		p_name: String = "",
		p_contact_email: String = "",
		p_associated_event_id: String = "",
) -> void:
	_message = p_message
	_name = p_name
	_contact_email = p_contact_email
	_associated_event_id = p_associated_event_id


## Returns the player-provided feedback message.
func message() -> String:
	return _message


## Returns the optional player-provided name.
func name() -> String:
	return _name


## Returns the optional player-provided contact email.
func contact_email() -> String:
	return _contact_email


## Returns the optional observability event ID associated by the caller.
func associated_event_id() -> String:
	return _associated_event_id
