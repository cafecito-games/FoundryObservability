@autoload
namespace foundry.observability.tests

import foundry.observability

## Records whether observability startup completed before this later autoload.
class_name ObservabilityStartupOrderProbe
extends Node

var observed_status: StringName = ObservabilityStartupStatus.NOT_STARTED


func _init() -> void:
	observed_status = FoundryObservability.startup_status()
