namespace foundry.observability.processing

## Decides how a bounded structured-value traversal handles each value.
trait_name ObservabilityValuePolicy

abstract func visit(
		path: PackedStringArray,
		value: Variant,
) -> ObservabilityValueVisitDecision


## Container values must use DESCEND; KEEP is limited to non-container leaves.
## Dictionary keys are separate from the item budget, support KEEP and REJECT
## decisions only, and are preserved by default. Kept original or replacement
## keys must be non-container values.
func visit_dictionary_key(_path: PackedStringArray, key: Variant) -> ObservabilityValueVisitDecision:
	return ObservabilityValueVisitDecision.keep(key)


## Strict policies fail the whole walk when a value is rejected.
func reject_is_failure() -> bool:
	return true


## Value rejection can depend on whether the value is a dictionary field.
func value_rejection_is_failure(
		_path: PackedStringArray,
		_value: Variant,
		_parent_is_dictionary: bool,
) -> bool:
	return reject_is_failure()


## Strict policy rejections are payload-free unless a policy identifies a rule.
func value_rejection_rule_index(
		_path: PackedStringArray,
		_value: Variant,
		_parent_is_dictionary: bool,
) -> int:
	return -1


## Strict policies fail the whole walk on depth overflow or a container cycle.
func invalid_container_is_failure() -> bool:
	return true


## Strict policies fail the whole walk when the shared item budget is exhausted.
func item_limit_is_failure() -> bool:
	return true
