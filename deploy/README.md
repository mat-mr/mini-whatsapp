# Deployment Guide

## Phase 1–2: Local Development

### Run Locally

```bash
docker compose up --build
```

Then open:
- http://localhost:8080/?user=alice
- http://localhost:8081/?user=bob

**Ctrl+C** to stop.

---

## Phase 5: Local Kubernetes (kind)

### Prerequisites

- `kind` installed
- `helm` installed
- `kubectl` installed
- Docker running

### Setup

```bash
# 1. Create kind cluster
kind create cluster --name chat

# 2. Install Strimzi operator (Kafka on K8s)
helm repo add strimzi https://strimzi.io/charts
helm repo update
helm install strimzi-operator strimzi/strimzi-kafka-operator \
  --namespace kafka --create-namespace

# 3. Apply Kafka + topic
kubectl apply -f kafka/

# 4. Wait for Kafka to be ready
kubectl wait --for=condition=Ready pod \
  -l strimzi.io/name=chat-kafka \
  -n kafka --timeout=300s

# 5. Deploy Postgres
kubectl apply -f - <<EOF
apiVersion: v1
kind: Namespace
metadata:
  name: chat
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: postgres
  namespace: chat
spec:
  replicas: 1
  selector:
    matchLabels:
      app: postgres
  template:
    metadata:
      labels:
        app: postgres
    spec:
      containers:
      - name: postgres
        image: postgres:17
        ports:
        - containerPort: 5432
        env:
        - name: POSTGRES_USER
          value: chat
        - name: POSTGRES_PASSWORD
          value: chat
        - name: POSTGRES_DB
          value: chat
        volumeMounts:
        - name: data
          mountPath: /var/lib/postgresql/data
      volumes:
      - name: data
        emptyDir: {}
---
apiVersion: v1
kind: Service
metadata:
  name: postgres
  namespace: chat
spec:
  selector:
    app: postgres
  ports:
  - port: 5432
    targetPort: 5432
EOF

# 6. Create DB secret
kubectl create secret generic chat-db \
  --from-literal=connectionString='Host=postgres:5432;Database=chat;Username=chat;Password=chat' \
  -n chat

# 7. Install app via Helm
helm upgrade --install chat ./helm/chat \
  --namespace chat \
  --set image.tag=latest \
  --set kafka.bootstrapServers='chat-kafka-bootstrap:9092' \
  --wait

# 8. Port-forward to test
kubectl port-forward -n chat svc/chat 8080:80 &

# Open: http://localhost:8080/?user=alice
```

### Test

Open two browser windows:
- http://localhost:8080/?user=alice
- http://localhost:8080/?user=bob (same port, different user)

Messages should sync in real-time across two pods.

### Cleanup

```bash
helm uninstall chat -n chat
kubectl delete namespace kafka
kind delete cluster --name chat
```

---

## Phase 6: Azure Infrastructure

### Prerequisites

- Azure account
- Azure CLI
- Terraform CLI
- GitHub repo with this code

### One-Time Bootstrap

Run this **once** to set up Azure storage for Terraform state:

```bash
cd infra
bash bootstrap.sh
```

This creates:
- Resource group
- Storage account
- Entra app + service principal
- Federated credential (OIDC for GitHub Actions)
- Prints the IDs you need to add to GitHub

**Save the output. Add to GitHub Actions secrets:**
```
AZURE_SUBSCRIPTION_ID=<from output>
AZURE_TENANT_ID=<from output>
AZURE_CLIENT_ID=<from output>
```

### Deploy Manually

```bash
cd infra/terraform

# Initialize
terraform init -backend-config=backend.hcl

# Plan
terraform plan

# Apply (asks for confirmation)
terraform apply

# Get outputs
terraform output
```

### Deploy via GitHub Actions

Push to `main` branch:
```bash
git push origin main
```

GitHub Actions `.github/workflows/cd.yml` will:
1. Run tests
2. Build + push image to GHCR
3. Apply Terraform
4. Deploy via Helm

### Access the App

```bash
# Get AKS credentials
az aks get-credentials \
  --resource-group $(terraform output -raw resource_group_name) \
  --name $(terraform output -raw aks_cluster_name)

# Port-forward
kubectl port-forward -n chat svc/chat 8080:80
```

Open: http://localhost:8080/?user=alice

### Teardown

```bash
# Delete Helm release
helm uninstall chat -n chat

# Destroy infrastructure
terraform destroy

# Delete resource group (if not managed by Terraform)
az group delete --name <resource-group-name>
```

---

## Production Considerations

### Real Auth
Replace demo `?user=` with JWT. Use Azure AD or Auth0.

### Database
- Enable automated backups
- Use managed PostgreSQL (Azure Database for PostgreSQL Flexible Server)
- Consider read replicas

### Kafka
- Use managed service (Azure Event Hubs or Confluent Cloud)
- Set replication factor ≥ 3
- Monitor consumer lag

### Networking
- Use private AKS clusters
- Implement Network Policies
- TLS for all traffic (cert-manager + Gateway API)

### Monitoring
- OpenTelemetry for tracing
- Prometheus for metrics
- ELK or Azure Log Analytics for logs

### Autoscaling
- HPA for API pods
- KEDA for Kafka consumer scaling

---

## Troubleshooting

### Kafka not ready
```bash
kubectl get kafka -n kafka
kubectl describe kafka chat-kafka -n kafka
kubectl logs -n kafka -l strimzi.io/name=chat-kafka
```

### App can't connect to Kafka
```bash
# Check service
kubectl get svc -n kafka
# Check bootstrapServers in helm values
helm get values chat -n chat
```

### Pod crashing
```bash
kubectl logs -n chat -l app=chat
kubectl describe pod -n chat
```

### Terraform issues
```bash
terraform plan -refresh-only
terraform state list
```
