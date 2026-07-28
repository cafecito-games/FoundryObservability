namespace foundry.observability.processing

import foundry.observability
import foundry.observability.runtime

## Immutable identity-preserving snapshot of one configured provider generation.
## Invalid public construction is reported and canonicalized to a disabled null snapshot.
final class_name ObservabilityProviderSnapshot extends RefCounted

final var _provider: ObservabilityProvider
final var _config: ObservabilityConfig
final var _pipeline: ObservabilityProcessingPipeline
final var _generation: int
final var _enabled: bool


func _init(
		p_provider: ObservabilityProvider?,
		p_config: ObservabilityConfig?,
		p_pipeline: ObservabilityProcessingPipeline?,
		p_generation: int,
) -> void:
	if p_provider != null and p_config != null and p_pipeline != null \
			and p_generation >= 0 \
			and p_pipeline.is_frozen_for(p_config):
		_provider = p_provider
		_config = p_config
		_pipeline = p_pipeline
		_generation = p_generation
		_enabled = p_config.enabled()
		return

	push_error("ObservabilityProviderSnapshot requires a prepared coherent snapshot.")
	_provider = NullObservabilityProvider.new()
	_config = ObservabilityConfig.new(p_enabled = false)
	_pipeline = ObservabilityProcessingPipeline.new(SystemObservabilityRuntime.new())
	var result: int = _pipeline.configure(_config)
	assert(result == Error.OK, "Fallback observability snapshot must be valid.")
	var fallback_token: ObservabilityPipelineClaimToken = ObservabilityPipelineClaimToken.new()
	result = _pipeline.claim(_config, fallback_token)
	assert(result == Error.OK, "Fallback observability snapshot must be claimed.")
	result = _pipeline.freeze(fallback_token)
	assert(result == Error.OK, "Fallback observability snapshot must be permanently frozen.")
	_generation = 0
	_enabled = false


func provider() -> ObservabilityProvider:
	return _provider


func config() -> ObservabilityConfig:
	return _config


func pipeline() -> ObservabilityProcessingPipeline:
	return _pipeline


func generation() -> int:
	return _generation


func enabled() -> bool:
	return _enabled
