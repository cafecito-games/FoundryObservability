namespace foundry.observability

## Bit flags selecting automatic engine diagnostics and messages.
class_name ObservabilityCaptureMask
extends RefCounted

const NONE: int = 0
const ERROR: int = 1 << 0
const WARNING: int = 1 << 1
const SCRIPT: int = 1 << 2
const SHADER: int = 1 << 3
const MESSAGE: int = 1 << 7
const ALL_ERRORS: int = ERROR | WARNING | SCRIPT | SHADER
const ALL: int = ALL_ERRORS | MESSAGE
const DEFAULT_EVENTS: int = ERROR | SCRIPT | SHADER
const DEFAULT_BREADCRUMBS: int = ALL
