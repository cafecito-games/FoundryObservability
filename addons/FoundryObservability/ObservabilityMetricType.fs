namespace foundry.observability

## Provider-neutral custom metric kinds.
class_name ObservabilityMetricType
extends RefCounted

## A monotonically accumulated occurrence count.
const COUNTER: int = 0
## A point-in-time measurement that may move up or down.
const GAUGE: int = 1
## A sampled measurement used for aggregate statistics.
const DISTRIBUTION: int = 2
