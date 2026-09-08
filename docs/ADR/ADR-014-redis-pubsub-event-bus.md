# ADR-014: Redis Pub/Sub Event Bus for API–Agent Decoupling

## Status
Accepted

## Context

Aveline's agentic workflow is currently synchronous: the ASP.NET Core API calls the
Python agent service over internal HTTP (ADR-009) and waits for the full LangGraph
workflow to finish before responding. As the platform grows (WhatsApp inbound, agent
status updates, workflow completion, VIP at-risk detection, approval-required signals),
this request/response coupling creates problems:

- **Latency:** a webhook or client request blocks for the entire agent workflow.
- **Tight coupling:** the API depends on the agent service being online and fast.
- **No fan-out:** a single event (e.g. "workflow completed") must reach several
  consumers (API persistence, SignalR realtime, notification gateway) but today each
  consumer would need its own direct call.

We need an asynchronous, decoupled event bus so producers publish once and any number of
consumers react, without the producer blocking or knowing who the consumers are.

## Options Considered

### 1. Redis Pub/Sub (chosen)
Redis is already in the stack as the distributed cache (ADR-001). Pub/Sub adds a
lightweight, sub-millisecond-latency broadcast channel with no new infrastructure.

- Pros: Zero new infrastructure; already deployed; extremely low latency; natural
  fan-out to many subscribers; fits agentic "event bus" coordination patterns.
- Cons: Fire-and-forget — if a subscriber is offline, messages are lost. No replay,
  no consumer groups, no durable backlog.

### 2. Redis Streams
Durable, replayable, consumer-group semantics on the same Redis instance.

- Pros: Durable; supports acknowledgement and replay; scales to reliable processing.
- Cons: Heavier client semantics; overkill for real-time coordination where a missed
  transient status update is acceptable; more moving parts for a 3-student team.

### 3. Kafka / RabbitMQ
A dedicated message broker.

- Pros: Durable, transactional, battle-tested for reliable eventing.
- Cons: A separate cluster to operate; significant operational complexity; not justified
  for the current real-time coordination use cases.

### 4. Direct HTTP fan-out
The API calls each consumer directly.

- Pros: No new abstraction.
- Cons: Producer must know every consumer; no broadcast; reintroduces coupling and
  latency; poor for many-to-many.

## Decision

1. **Redis Pub/Sub is the event bus.** A reusable, transport-agnostic event bus
   abstraction is introduced so call sites depend on `IEventBus`, not on Redis directly.
   The default implementation is backed by Redis Pub/Sub.

2. **Channel naming is org-scoped.** Channels follow `aveline:<org_id>:<event_type>`.
   Because a single agent deployment serves all organizations, subscribers use the
   pattern `aveline:*:<event_type>` (Redis `PSUBSCRIBE`) to receive events for every org
   while the channel name still carries the org for routing/audit.

3. **A shared event envelope.** Every event is a JSON envelope with a stable snake_case
   contract mirrored between C# (`System.Text.Json`) and Python (Pydantic):
   `event_id`, `event_type`, `timestamp`, `org_id`, `trace_id`, `payload`.

4. **Fire-and-forget is accepted for real-time coordination.** Transient status and
   notification events may be dropped if a subscriber is offline. Critical transactional
   operations continue to use the existing internal HTTP path (ADR-009) as a fallback and
   are not routed solely through the bus.

5. **The bus is infrastructure-first.** This ADR covers the reusable publisher/subscriber
   abstractions, envelope contract, subscription host, and monitoring. Concrete business
   triggers (WhatsApp webhook, agent workflow completion) are wired in later slices once
   the bus exists.

6. **Monitoring is log- and health-based.** Redis connectivity is surfaced through health
   checks on both services; publish/consume/error counters are emitted as structured JSON
   logs (already shippable to ELK / Application Insights). No separate metrics
   infrastructure is introduced.

7. **Redis-dependent code is unit-tested with Moq.** The `StackExchange.Redis`
   `IConnectionMultiplexer` / `ISubscriber` interfaces are large and impractical to fake by
   hand (the project otherwise uses hand-written fakes for small interfaces). Moq is added
   to the test project to mock these interfaces for `RedisEventBus.PublishAsync`,
   `RedisSubscriptionService`, and `RedisHealthCheck`. Pure logic (envelope, channel,
   serializer, handler dispatch) remains covered by hand-written tests without Moq.

## Consequences

- A cross-cutting `Infrastructure/Eventing` module is introduced in the API following the
  established module pattern (ADR-001), exposing `IEventBus` for publish and subscribe.
- The agent service gains an `app/events` module using `redis.asyncio`, with a subscriber
  started/stopped on the FastAPI lifespan.
- A single shared `IConnectionMultiplexer` is registered so the cache and the event bus
  reuse one Redis connection.
- Events are best-effort; consumers must tolerate loss and duplicate delivery (at-least
  semantics are not guaranteed by Pub/Sub).
- Redis Streams or a dedicated broker remain a future option if durable, replayable
  eventing is ever required; the `IEventBus` abstraction isolates call sites from that
  change.
- A Redis SignalR backplane for horizontal scale-out remains deferred (single API
  instance today), consistent with ADR-013.
- `Moq` is a test-only dependency of `Aveline.Api.Tests`; it is not referenced by the
  runtime API project.
