# Local Kubernetes Setup (Phase 5)

This guide walks through deploying the Mini WhatsApp app on a local Kubernetes cluster using `kind` (Kubernetes in Docker).

## Prerequisites

Install these tools:

- **Docker Desktop** — https://www.docker.com/products/docker-desktop
- **kind** — https://kind.sigs.k8s.io/docs/user/quick-start/
- **kubectl** — included with Docker Desktop, or https://kubernetes.io/docs/tasks/tools/
- **Helm** — https://helm.sh/docs/intro/install/

Verify installation:
```bash
kind version
kubectl version
helm version
docker --version
```

## Architecture

```
┌─────────────────────────────────────────┐
│         kind Cluster (local K8s)        │
├─────────────────────────────────────────┤
│                                         │
│  kafka namespace:                       │
│  ├─ Strimzi Operator                   │
│  ├─ Kafka (1 broker, 1Gi storage)      │
│  ├─ Zookeeper (500Mi storage)          │
│  └─ Topic: chat-messages (3 partitions)│
│                                         │
│  chat namespace:                        │
│  ├─ PostgreSQL (emptyDir, 1 user)      │
│  ├─ Chat Pod 1 (50m CPU, 128Mi RAM)   │
│  └─ Service (LoadBalancer → :8080)     │
│                                         │
└─────────────────────────────────────────┘

Your Machine:
  localhost:8080 → K8s Service → Chat Pods
```

## Step-by-Step Setup

### 1. Create kind Cluster

```bash
kind create cluster --name chat
```

Verify:
```bash
kubectl cluster-info
kubectl get nodes
```

### 2. Install Strimzi Operator (Kafka on K8s)

```bash
helm repo add strimzi https://strimzi.io/charts
helm repo update

helm install strimzi-operator strimzi/strimzi-kafka-operator \
  --namespace kafka \
  --create-namespace \
  --set installCRDs=true \
  --wait
```

Verify CRDs are installed:
```bash
kubectl get crd | grep strimzi
# Should show: kafkas.kafka.strimzi.io, kafkatopics.kafka.strimzi.io, etc.
```

### 3. Deploy Kafka + Topic

```bash
kubectl apply -f deploy/postgres.yaml
kubectl apply -f deploy/kafka/kafka.yaml
kubectl apply -f deploy/kafka/topic.yaml
```

Wait for Kafka to be ready:
```bash
kubectl wait --for=condition=Ready pod -l strimzi.io/name=chat-kafka -n kafka --timeout=300s
```

Verify:
```bash
kubectl get kafka -n kafka
kubectl get kafkatopic -n kafka
kubectl get pods -n kafka
```

### 4. Deploy PostgreSQL

PostgreSQL is already applied in step 3 (postgres.yaml creates the chat namespace and Postgres).

Wait for it:
```bash
kubectl wait --for=condition=Ready pod -l app=postgres -n chat --timeout=60s
```

Verify:
```bash
kubectl get pods -n chat
```

### 5. Deploy Chat App with Helm

```bash
helm install chat deploy/helm/chat \
  --namespace chat \
  --set image.tag=latest \
  --set replicaCount=1 \
  --wait
```

Verify:
```bash
kubectl get pods -n chat
kubectl get svc -n chat
kubectl logs -n chat -l app.kubernetes.io/instance=chat
```

### 6. Access the App

Port-forward the service to your machine:
```bash
kubectl port-forward -n chat svc/chat 8080:80
```

The service is now accessible at `http://localhost:8080`.

Open in two browser windows:
- **Window 1:** http://localhost:8080/?user=alice
- **Window 2:** http://localhost:8080/?user=bob

### 7. Test Messaging

1. **Alice sends "hello"** to Bob
2. **Bob sees it instantly** (via Kafka fanout across pods)
3. **Refresh the page** — message persists in PostgreSQL
4. **Send back and forth** to test bidirectional chat

## Monitoring & Debugging

### View Logs

```bash
# Chat app logs
kubectl logs -n chat -l app.kubernetes.io/instance=chat

# Kafka operator logs
kubectl logs -n kafka -l app.kubernetes.io/name=strimzi-cluster-operator

# PostgreSQL logs
kubectl logs -n chat -l app=postgres
```

### Check Pod Status

```bash
# All pods
kubectl get pods -A

# Specific namespace
kubectl get pods -n chat
kubectl get pods -n kafka

# Pod details
kubectl describe pod <pod-name> -n <namespace>
```

### Port-Forward Issues

If port 8080 is already in use:
```bash
# Use a different port
kubectl port-forward -n chat svc/chat 9000:80
# Then access: http://localhost:9000/?user=alice
```

### Kafka Consumer Groups

View consumer groups (KafkaFanOut creates one per pod):
```bash
kubectl port-forward -n kafka svc/chat-kafka-bootstrap 9092:9092 &
# In another terminal, use a Kafka CLI tool to inspect
```

### Database Connection

Connect directly to PostgreSQL:
```bash
kubectl exec -it -n chat pod/<postgres-pod-name> -- psql -U chat -d chat
```

List messages:
```sql
SELECT * FROM "Messages" ORDER BY "SentAt" DESC;
```

## Cleanup

### Stop Port-Forward
```bash
# Kill the port-forward (Ctrl+C in the terminal running it)
```

### Delete Everything

```bash
# Uninstall Helm release
helm uninstall chat -n chat

# Delete namespaces (everything in them is deleted)
kubectl delete namespace chat
kubectl delete namespace kafka

# Delete the cluster
kind delete cluster --name chat
```

All local data is permanently deleted. ✅

## Resource Usage

This setup uses minimal resources suitable for testing:

| Component | CPU | Memory | Storage |
|-----------|-----|--------|---------|
| Kafka broker | 100m | 256Mi | 1Gi |
| Zookeeper | 100m | 256Mi | 500Mi |
| Chat app (per pod) | 50m | 128Mi | — |
| PostgreSQL | — | — | emptyDir |

**Total:** ~250m CPU, ~900Mi RAM, ~1.5Gi storage

## What's Happening Under the Hood

### Message Flow

1. **Alice sends "hello"** via `connection.invoke("Send", "bob", "hello")`
2. **ChatHub.Send()** saves to PostgreSQL and publishes to Kafka
3. **KafkaFanOut** (running in each pod) consumes the message
4. **SignalR broadcast** sends to both users via `Clients.Users(to, from)`
5. **Both browsers** receive the message event and display it

### Kafka Topic Partitioning

Messages between Alice and Bob always go to the same partition (message key = `alice|bob`), ensuring order. Multiple conversations can use different partitions in parallel.

### Pod Restart Behavior

If a pod dies:
1. Kubernetes automatically restarts it
2. KafkaFanOut reconnects to Kafka
3. Browser auto-reconnects via SignalR's `withAutomaticReconnect()`
4. Chat continues without missing messages (Kafka retains them)

## Next Steps

Once you've tested locally:

1. **Commit Phase 5** → git push
2. **Phase 6** → Terraform + Azure infrastructure
3. **Phase 7** → CI/CD pipeline to deploy to Azure

See the main [README.md](../README.md) for the full project architecture.
