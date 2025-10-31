# Observability

## Telemetry

- OpenTelemetry is wired for ASP.NET Core and HTTP client instrumentation.
- Exporter: OTLP (configurable).

## Traces

- API endpoints emit request spans; outbound calls include context.

## Metrics & Logs

- Standard ASP.NET Core counters and structured logs.
- Extend with domain-specific metrics (e.g., resolve latency, cache hit rate).

