# Redis Pub/Sub Event Bus — Architecture

> **Status:** Approved (see [ADR-014](../ADR/ADR-014-redis-pubsub-event-bus.md)).

This document describes the Redis Pub/Sub event bus that decouples the ASP.NET Core API
from the Python agent service (and future consumers). It covers the channel model, the
shared envelope contract, the publisher/subscriber components on each side, and monitoring.

---

## 1. Why an event bus?

Today the API calls the agent service synchronously over internal HTTP (ADR-009). As the
platform grows (WhatsApp inbound, agent status, workflow completion, VIP at-risk,
approval-required signals), request/response coupling causes latency, tight coupling, and
no fan-out. The event bus lets a producer publish once and any number of consumers react
without the producer blocking or knowing who the consumers are.

> **Fire-and-forget:** Redis Pub/Sub drops messages when no subscriber is connected. The
> bus is intended for real-time coordination and notifications. Critical transactional
> operations must continue to use the internal HTTP path (ADR-009) as a fallback.

---

## 2. Channel model

Channels are org-scoped so a channel name carries the owning organization for routing and
audit:

```
aveline:<org_id>:<event_type>
```

Because a single agent deployment serves every organization, subscribers use the pattern
`aveline:*:<event_type>` (Redis `PSUBSCRIBE`) to receive events for all orgs.

| Channel | Purpose | Publisher | Subscribers |
|---|---|---|---|
| `aveline:<org>:message.received` | New customer message | API | Agent service |
| `aveline:<org>:agent.status` | Agent state update | Agent service | API / SignalR |
| `aveline:<org>:workflow.completed` | Agent workflow finished | Agent service | API / SignalR |
| `aveline:<org>:notification` | Trigger frontend notification | API | SignalR bridge |

---

## 3. Envelope contract

Every event is a JSON envelope with a stable snake_case contract mirrored between C#
(`System.Text.Json`) and Python (Pydantic):

```json
{
  "event_id": "uuid",
  "event_type": "message.received",
  "timestamp": "2026-09-08T14:30:00Z",
  "org_id": "uuid",
  "trace_id": "uuid",
  "payload": { }
}
```

- `event_id` — unique per event instance.
- `event_type` — discriminator used to build the channel.
- `timestamp` — UTC production instant.
- `org_id` — owning organization (null for system-wide events).
- `trace_id` — correlation id linking the event to a broader request/workflow.
- `payload` — opaque event-specific data.

---

## 4. Components

### 4.1 ASP.NET Core API (`Aveline.Api/Infrastructure/Eventing`)

| Component | Responsibility |
|---|---|
| `IEventBus` | Transport-agnostic publish / subscribe abstraction. |
| `RedisEventBus` | Redis Pub/Sub implementation; publishes to `aveline:<org>:<event>` and fans received messages out to registered handlers. |
| `InMemoryEventBus` | In-process fallback used when no Redis is configured (e.g. the test suite). |
| `EventEnvelope` / `EventChannel` | Envelope model and channel/pattern helpers. |
| `IEventSerializer` / `SystemTextJsonEventSerializer` | snake_case JSON matching the Pydantic contract. |
| `RedisSubscriptionService` | `BackgroundService` owning the `PSUBSCRIBE` connections; dispatches into `RedisEventBus`. |
| `EventBusMetrics` / `EventingMetricsExporter` | `System.Diagnostics.Metrics` counters exported to JSON logs. |
| `RedisHealthCheck` | Surfaces Redis connectivity at `/health`. |

The bus is registered via `AddAvelineEventing(...)` in `Program.cs`. When a Redis
connection string is present it uses the shared `IConnectionMultiplexer` (also used by the
distributed cache); otherwise it falls back to `InMemoryEventBus`.

### 4.2 Python agent service (`agnet-service/app/events`)

| Component | Responsibility |
|---|---|
| `schemas.EventEnvelope` | Pydantic model mirroring the C# envelope. |
| `bus.RedisEventBus` | Async pub/sub (`redis.asyncio`); `publish`, `register`, background listener. |
| `bus.channel_for` / `bus.pattern_for` | Channel and pattern helpers. |

The subscriber is started/stopped on the FastAPI lifespan in `app/main.py`. The agent
subscribes to the event types in `SUBSCRIBE_EVENT_TYPES`.

---

## 5. Configuration

| Setting | Service | Purpose |
|---|---|---|
| `Redis:ConnectionString` / `ConnectionStrings:Redis` | API | Redis connection (shared with cache). |
| `Eventing:SubscribeEventTypes` | API | Event types the API consumes from the agent. |
| `Eventing:MetricsLogIntervalSeconds` | API | How often event metrics are logged (default 30s). |
| `REDIS_URL` | Agent | Redis connection URL. |
| `SUBSCRIBE_EVENT_TYPES` | Agent | Event types the agent consumes from the API. |

---

## 6. Monitoring

- **Health:** the API exposes `/health` (Redis `PING` via `RedisHealthCheck`); the agent's
  `/health` reports `redis: ok | unreachable | disabled`.
- **Metrics:** `EventBusMetrics` tracks `aveline.events.published`, `aveline.events.received`,
  `aveline.events.failed`, and publish latency. `EventingMetricsExporter` logs cumulative
  counters as structured JSON every `MetricsLogIntervalSeconds`, flowing through the existing
  ELK / Application Insights pipeline.
- **Redis ops:** use `redis-cli --stat` / `INFO` to monitor Redis CPU and memory.

---

## 7. Adding a new event

1. Publish from the producer via `IEventBus.PublishAsync(eventType, orgId, payload, traceId)`
   (API) or `RedisEventBus.publish(...)` (agent).
2. Register a handler on the consumer via `IEventBus.SubscribeAsync(eventType, handler)`.
3. Add the event type to the consumer's `Eventing:SubscribeEventTypes` / `SUBSCRIBE_EVENT_TYPES`
   so its subscription host listens on the matching pattern.
