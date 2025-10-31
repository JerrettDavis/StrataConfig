# Merge Engine

## Precedence

Default order (low → high):

`Global < Org < Division < Site < Device < User < App < Environment < AdHocOverride`

Later layers override earlier ones on key conflicts.

## Process

1. Gather all documents for the namespace across applicable layers.
2. Validate each against its template.
3. Apply rules (include/exclude/override) per layer.
4. Merge JSON payloads in precedence order into a single object.
5. Produce flattened key/value pairs (dot-notated keys).

## Guarantees

- Deterministic ordering and result for a given input set.
- Idempotent merge given same revisions.

