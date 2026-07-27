namespace foundry.observability.runtime

## Supplies all engine runtime facts used by provider-neutral observability.
trait_name ObservabilityRuntime

## Returns monotonic engine time in milliseconds.
abstract func monotonic_time_msec() -> int
## Returns Unix epoch time in milliseconds.
abstract func unix_time_msec() -> int
## Returns the current processed frame index.
abstract func process_frame() -> int
## Returns the current execution owner identifier.
abstract func caller_id() -> int
## Returns the engine main-thread identifier.
abstract func main_thread_id() -> int
