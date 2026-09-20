# Mini WhatsApp

A minimal 1:1 chat application built end-to-end with modern cloud-native tech. **Learning project**: each phase builds one layer, from a local app to Kubernetes + Terraform on Azure.

## Architecture

```
Phase 1: Local app (SignalR + PostgreSQL)
  ↓
Phase 2: Scale-out (+ Kafka)
  ↓
Phase 3: Automated tests (xUnit + Testcontainers)
  ↓
Phase 4: CI (GitHub Actions → GHCR)
  ↓
Phase 5: Local Kubernetes (kind + Helm + Strimzi)
  ↓
Phase 6: Azure infrastructure (Terraform + AKS)
  ↓
Phase 7: CD (GitHub Actions → AKS)
  ↓
Phase 8: Docs + next steps
```

## Tech Stack

- **Backend:** .NET 10, ASP.NET Core minimal APIs, SignalR (WebSockets)
- **Database:** PostgreSQL via EF Core (Npgsql)
- **Message Broker:** Kafka (Confluent.Kafka)
- **Frontend:** Plain HTML + vanilla JS (SignalR client from CDN)
- **Testing:** xUnit, WebApplicationFactory, Testcontainers
- **Containerization:** Docker, Docker Compose
- **Orchestration:** Kubernetes (kind locally, AKS on Azure)
- **Config Management:** Helm
- **Infrastructure:** Terraform (azurerm provider)
- **CI/CD:** GitHub Actions

## Quick Start

### Phase 1: Local Development

**Requirements:**
- .NET 10
- Docker + Docker Compose

**Run:**
```bash
# Start Postgres
docker run -d --name chat-db \
  -e POSTGRES_USER=chat \
  -e POSTGRES_PASSWORD=chat \
  -e POSTGRES_DB=chat \
  -p 5432:5432 \
  postgres:17

# Start the app
dotnet run --project src/Chat.Api

# Open in two browser windows
# Window 1: http://localhost:5000/?user=alice
# Window 2: http://localhost:5000/?user=bob
```

Messages sync in real-time within a single instance.

### Phase 2: Multi-Instance with Kafka

**Run:**
```bash
docker compose up --build
```

Then open:
- http://localhost:8080/?user=alice (api-1)
- http://localhost:8081/?user=bob (api-2)

Messages broadcast across instances via Kafka. Try refreshing—history persists in PostgreSQL.

## Project Layout

```
src/Chat.Api/
  ├── Hubs/ChatHub.cs              # SignalR hub: Send() invocation
  ├── Entities/Message.cs          # Message model
  ├── ChatDb.cs                    # EF Core DbContext
  ├── MessageBus/
  │   ├── IMessageBus.cs           # Fanout abstraction
  │   ├── LocalMessageBus.cs       # In-process broadcast (Phase 1)
  │   ├── KafkaMessageBus.cs       # Kafka producer (Phase 2)
  │   └── KafkaFanOut.cs           # Kafka consumer service (Phase 2)
  ├── Auth/DemoUserIdProvider.cs   # Query-string auth (demo-only)
  ├── Endpoints/MessagesEndpoint.cs # GET /api/messages
  ├── Program.cs                   # App startup + DI
  ├── appsettings.json             # Config
  └── wwwroot/index.html           # Chat UI

tests/Chat.Api.Tests/             # Integration tests (Phase 3)

deploy/
  ├── helm/chat/                   # Helm chart (Phase 5)
  ├── kafka/                       # Strimzi manifests (Phase 5)
  └── README.md                    # Deployment instructions

infra/terraform/                  # Azure infrastructure (Phase 6)
  ├── main.tf
  ├── variables.tf
  ├── outputs.tf
  └── backend.hcl

.github/workflows/
  ├── ci.yml                       # Test + build (Phase 4)
  └── cd.yml                       # Deploy (Phase 7)

Dockerfile                         # Multi-stage: sdk → aspnet
docker-compose.yml               # Local: Postgres, Kafka, api-1/2
Chat.sln
```

## Key Concepts

### Phase 1: LocalMessageBus
Messages flow: Client → Hub.Send() → DB save → LocalMessageBus → SignalR broadcast → Clients

**Limitation:** Only works within one process. If Alice is on :8080 and Bob is on :8081, they can't chat.

### Phase 2: KafkaMessageBus + KafkaFanOut
Messages flow: Client → Hub.Send() → DB save → Kafka (producer) → topic → consumers (both instances) → SignalR broadcast → Clients

**Key design:**
- **Message key:** `alice|bob` (sorted usernames) → ensures one conversation stays on the same partition → ordered messages
- **Consumer group:** `chat-fanout-{MachineName}` → unique per instance → each instance sees all messages (fanout, not load-balanced)
- **AutoOffsetReset:** Latest → new instances don't replay old messages

### Real-Time Pattern
1. Invoke is RPC (client waits for response)
2. SendAsync is one-way (server pushes to clients)
3. Broadcast uses `Clients.Users(to, from)` → targets both sender and recipient

## Testing

```bash
# Phase 3: Run integration tests
dotnet test -c Release
```

Tests use:
- **WebApplicationFactory:** In-memory test server
- **Testcontainers.PostgreSql:** Real Postgres in Docker for each test
- **SignalR client:** LongPolling transport (TestServer has no WebSockets)

## CI/CD

### Phase 4: Builds on GitHub
- Pushes to `main` auto-build and push to GHCR
- Image tagged with commit SHA + `latest`
- Runs tests on all PRs

### Phase 7: Deploys to Azure
- Applies Terraform (AKS, RDS, etc.)
- Applies Kafka manifests
- Deploys Helm chart

## Demo Auth (Phase 1–6)

Uses `?user=alice` query string. **Not production-ready.** Phase 7 replaces with JWT.

## Deployment

See [deploy/README.md](deploy/README.md) for:
- Local Kubernetes setup (kind + Helm)
- Azure deployment (Terraform + AKS)
- Rollback / teardown procedures

## Next Steps (Not Implemented)

- Real JWT authentication
- Transactional outbox pattern (handle dual-write properly)
- Database migrations (EF Migrations)
- TLS via Gateway API + cert-manager
- Read receipts
- OpenTelemetry observability
- HPA/KEDA autoscaling
- Workload Identity + Key Vault for secrets

## Cost Warning

Phase 6+ uses Azure resources (AKS, PostgreSQL, Kafka). Monitor billing.

## License

Learning project. Use freely for educational purposes.
