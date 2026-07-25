namespace foundry.observability

## Stable results from the project-settings startup path.
class_name ObservabilityStartupStatus
extends RefCounted

const NOT_STARTED: StringName = &"not_started"
const INITIALIZED: StringName = &"initialized"
const DISABLED: StringName = &"disabled"
const SKIPPED_EDITOR: StringName = &"skipped_editor"
const SKIPPED_EDITOR_PLAY: StringName = &"skipped_editor_play"
const SKIPPED_DEBUG: StringName = &"skipped_debug"
const MISSING_DSN: StringName = &"missing_dsn"
const PROVIDER_UNAVAILABLE: StringName = &"provider_unavailable"
const CONFIGURATION_FAILED: StringName = &"configuration_failed"
