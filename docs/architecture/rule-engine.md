# Rule Engine

Rules shape evaluation by including, excluding, or overriding configuration at/under a node.

## Effects

- Include: ensure a matching document participates in merge.
- Exclude: remove a document or subtree from consideration.
- Override: apply a JSON patch/object to mutate content.

## Evaluation

- Evaluate in a deterministic order (by priority, then path).
- Scope-aware: rules attach to nodes; effects apply to that subtree.
- Predicates can reference scope dimensions, environment, app name/version, and tags.

## Examples

- Exclude experimental dashboards in Production.
- Override theme color for a VIP tag.

