namespace foundry.observability

## Optional provider capability for accepting diagnostic attachments.
trait_name ObservabilityAttachmentsProvider

## Adds an attachment and returns its provider-local handle, or an empty string on failure.
abstract func add_attachment(attachment: ObservabilityAttachment) -> String
## Removes an attachment by handle and returns an Error value.
abstract func remove_attachment(handle: String) -> int
## Clears all pending attachments and reports whether the provider accepted the operation.
abstract func clear_attachments() -> bool
## Returns isolated ObservabilityAttachmentFailure values from the latest attachment operation.
abstract func last_attachment_failures() -> Array
