---
title: Installation
description: Full installation guide for mc-operator — Helm, Kustomize, prerequisites, and webhook setup.
sidebar:
  order: 3
---

import { Tabs, TabItem, Aside } from '@astrojs/starlight/components';

## Prerequisites

| Requirement | Version |
|-------------|---------|
| Kubernetes  | 1.25+   |
| Helm        | 3.14+ (for OCI chart support) |
| kubectl     | Matching cluster version |

For production webhook support, you also need:

- [cert-manager](https://cert-manager.io/) or a custom TLS provisioning setup to serve the webhook endpoint over HTTPS.

## Install the CRD

The `MinecraftServer` CRD must be installed before the operator:

```bash
kubectl apply -f https://raw.githubusercontent.com/danihengeveld/mc-operator/main/manifests/crd/minecraftservers.yaml
```

<Aside type="caution">
Always install the CRD that matches your operator version. The CRD and operator are versioned together.
</Aside>

## Install the operator

<Tabs>
<TabItem label="Helm (recommended)">

The Helm chart is published as an OCI artifact to GHCR:

```bash
helm install mc-operator oci://ghcr.io/danihengeveld/charts/mc-operator \
  --version 1.0.0 \
  --namespace mc-operator-system \
  --create-namespace
```

To see available versions:

```bash
helm show chart oci://ghcr.io/danihengeveld/charts/mc-operator
```

To upgrade:

```bash
helm upgrade mc-operator oci://ghcr.io/danihengeveld/charts/mc-operator \
  --version <new-version> \
  --namespace mc-operator-system
```

</TabItem>
<TabItem label="Kustomize">

Apply the bundled Kustomize manifests directly:

```bash
kubectl apply -f manifests/crd/minecraftservers.yaml
kubectl apply -f manifests/rbac/
kubectl apply -k manifests/operator/
```

You can use Kustomize overlays to customize the deployment for your environment.

</TabItem>
</Tabs>

## Verify the installation

```bash
# Operator pod should be Running
kubectl get pods -n mc-operator-system

# CRD should be established
kubectl get crd minecraftservers.minecraft.dhv.sh
```

## Helm values reference

Key values you may want to override:

| Key | Default | Description |
|-----|---------|-------------|
| `image.repository` | `ghcr.io/danihengeveld/mc-operator` | Operator container image |
| `image.tag` | Chart `appVersion` | Image tag |
| `replicaCount` | `1` | Number of operator replicas |
| `resources.requests.cpu` | `100m` | CPU request |
| `resources.requests.memory` | `64Mi` | Memory request |
| `resources.limits.cpu` | `200m` | CPU limit |
| `resources.limits.memory` | `128Mi` | Memory limit |
| `webhooks.enabled` | `true` | Enable admission webhooks |
| `webhooks.port` | `5001` | HTTPS port for webhooks |
| `logLevel` | `Information` | Log verbosity |
| `leaderElection.enabled` | `true` | Enable leader election |

Full values reference: `helm show values oci://ghcr.io/danihengeveld/charts/mc-operator`

## Webhooks setup

The operator runs validating and mutating admission webhooks on port `5001` (HTTPS). The Kubernetes API server must be able to reach the operator service over TLS.

### Using cert-manager (recommended)

1. Install [cert-manager](https://cert-manager.io/docs/installation/).

2. Configure Helm to create an `Issuer` and inject the CA bundle automatically:

   ```yaml
   # values-prod.yaml
   webhooks:
     enabled: true
     certManager:
       enabled: true
       issuerRef:
         name: my-cluster-issuer
         kind: ClusterIssuer
   ```

3. Annotate the `ValidatingWebhookConfiguration` and `MutatingWebhookConfiguration` with:

   ```yaml
   cert-manager.io/inject-ca-from: mc-operator-system/mc-operator-webhook-cert
   ```

   These annotations are already present in the `manifests/operator/validators.yaml` and `manifests/operator/mutators.yaml` as comments — uncomment them and fill in the correct certificate resource name.

### Manual TLS

If you are not using cert-manager, you need to:

1. Generate a TLS certificate for the webhook service (CN: `mc-operator.mc-operator-system.svc`).
2. Create two Kubernetes Secrets:
   - `webhook-cert` — the certificate and private key (`svc.pem`, `svc-key.pem`)
   - `webhook-ca` — the CA certificate (`ca.pem`)
3. Patch the `caBundle` field in the webhook configurations with the base64-encoded CA cert.

<Aside type="tip">
For development, you can disable webhooks entirely: `helm install ... --set webhooks.enabled=false`
</Aside>
