# Template events consumer — operator notes

`TemplateEventsConsumerBackgroundService` consumes `wa.template.status_changed.v1` /
`wa.template.category_changed.v1` from the shared `wavio.events` topic exchange (published by
wa-ingest-svc). See its XML doc comment and `TransientRetryPolicy` for the full ack/retry design;
this file is the short operator-facing summary.

## What lands in the DLQ, and what doesn't

Queue: `wa-admin.template-events` (durable, `x-dead-letter-exchange = wavio.events.dlx`).
Dead-letter queue: `wa-admin.template-events.dlq` (fanout-bound, durable).

A message is dead-lettered (never redelivered automatically) when:
- The payload is malformed (not valid JSON for the expected event shape).
- The command handler returns a deterministic "parked" outcome: unresolvable tenant
  (`TenantId == Guid.Empty`), unknown template (no match for the event's `MetaTemplateId` in that
  tenant), or an invalid state-machine transition (see `TemplateStatusTransitions`).

A message is instead **requeued** (stays live, gets redelivered, no operator action needed) when
processing hit a transient infra failure (`TransientRetryPolicy.IsTransient`: `DbException`,
`TimeoutException`, `SocketException`, `IOException`, or an EF `DbUpdateException` wrapping one of
those) and exhausted its in-process retries (~7s of backoff). This is the fix for security-review
finding S1 (2026-07): a brief Postgres/RabbitMQ blip must not permanently lose a legitimate Meta
status transition.

## Inspecting the DLQ

Via the RabbitMQ management UI/API (`http://localhost:15672` in dev, `guest:guest`):

```bash
# Message count
curl -s -u guest:guest http://localhost:15672/api/queues/%2f/wa-admin.template-events.dlq \
  | python3 -c "import json,sys;print(json.load(sys.stdin)['messages'])"

# Peek at messages without consuming them (requeue: true puts them straight back)
curl -s -u guest:guest -X POST \
  http://localhost:15672/api/queues/%2f/wa-admin.template-events.dlq/get \
  -H 'Content-Type: application/json' \
  -d '{"count":10,"ackmode":"ack_requeue_true","encoding":"auto"}'
```

## Re-injecting a dead-lettered message

There is no automated redrive tool yet (Wave 1 scope). To manually re-drive one message: read its
body via the `get` API above (`ackmode: "ack_requeue_false"` to consume it this time), then
re-publish it to `wavio.events` with its original routing key:

```bash
curl -s -u guest:guest -X POST \
  http://localhost:15672/api/exchanges/%2f/wavio.events/publish \
  -H 'Content-Type: application/json' \
  -d '{"properties":{"content_type":"application/json"},
       "routing_key":"wa.template.status_changed.v1",
       "payload":"<paste the dequeued payload here>",
       "payload_encoding":"string"}'
```

Only do this after confirming the underlying cause is actually fixed (e.g. the template now
exists, or the transition is now legal) — re-publishing a message that will just be parked again
only adds noise.
