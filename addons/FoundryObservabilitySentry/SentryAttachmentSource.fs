namespace foundry.observability.sentry

## Provides engine-owned values needed for built-in Sentry attachments.
trait_name SentryAttachmentSource

abstract func is_main_thread() -> bool

abstract func is_headless() -> bool

abstract func frames_drawn() -> int

abstract func scene_root() -> Node?

abstract func screenshot_png() -> PackedByteArray

abstract func game_log_path() -> String
