namespace foundry.observability.processing

## Opaque capability whose object identity authorizes one pipeline session owner.
## Tokens carry no forgeable scalar authority; only the exact claiming object matches.
final class_name ObservabilityPipelineClaimToken extends RefCounted
