---
title: Troubleshooting & FAQ
description: Common problems and their solutions when running mc-operator.
sidebar:
  order: 3
---

import { Aside } from '@astrojs/starlight/components';

## Diagnosing problems

Start with these commands to get a quick overview of what's happening:

```bash
# Operator pod status and logs
kubectl get pods -n mc-operator-system
kubectl logs -n mc-operator-system -l app.kubernetes.io/name=mc-operator --tail=100

# Server resource status
kubectl describe minecraftserver <name> -n <namespace>
kubectl get events -n <namespace> --sort-by='.lastTimestamp'

# Child resources
kubectl get statefulset,service,configmap,pvc -n <namespace> -l app.kubernetes.io/instance=<name>
```

## Common issues

### Server is stuck in `Provisioning`

**Cause**: The pod hasn't started, or the Minecraft server jar is still downloading.

**Check**: Look at the pod logs:

```bash
kubectl logs -n minecraft <name>-0 -f
```

The first startup can take 2–5 minutes while `itzg/minecraft-server` downloads the server jar. If the pod is in `ContainerCreating` or `Pending`, the issue is likely storage-related.

```bash
kubectl describe pod -n minecraft <name>-0
kubectl get pvc -n minecraft
```

A PVC in `Pending` state usually means no StorageClass matches, or the cluster has no available persistent storage provisioner.

---

### Webhook admission errors

**Error example**: `Error from server: admission webhook "validate.minecraftserver.mc-operator.dhv.sh.v1alpha1" denied the request: ...`

**Cause**: The validating webhook rejected your spec.

**Fix**: Read the error message — it describes exactly which field is invalid. Common cases:
- `acceptEula` is `false` → Set `acceptEula: true`
- `jvm.maxMemory` is smaller than `jvm.initialMemory` → Fix the sizes
- `storage.size` is not a valid Kubernetes quantity → Use a value like `10Gi`, not `10gb`
- `service.nodePort` is set but `service.type` is `ClusterIP` → Remove `nodePort` or change `type` to `NodePort`

If the webhook pod is unavailable, Kubernetes uses the `failurePolicy: Fail` policy and rejects all operations. Check the operator pod health first.

---

### Cannot change storage settings after creation

**Error**: `storage.enabled is immutable`, `storage.size is immutable`

**Cause**: Storage fields are intentionally immutable to match PVC semantics. Once a PVC is bound, these cannot change through the operator.

**Fix for storage size**: Resize the PVC directly:

```bash
kubectl patch pvc data-<name>-0 -n <namespace> \
  --type merge -p '{"spec": {"resources": {"requests": {"storage": "50Gi"}}}}'
```

Note: The underlying StorageClass must support volume expansion.

---

### Pod is `OOMKilled`

**Cause**: The container memory limit is too small relative to the JVM heap.

**Fix**: Increase `resources.memoryLimit` to at least `jvm.maxMemory` plus 512 Mi for JVM overhead:

```yaml
jvm:
  maxMemory: "2G"
resources:
  memoryLimit: "2500Mi"   # 2G + 500Mi overhead
```

---

### TLS/Webhook certificate errors

**Error**: `x509: certificate signed by unknown authority`

**Cause**: The `caBundle` in the webhook configuration doesn't match the certificate served by the operator.

**Fix**: If using cert-manager, ensure the annotation is correctly injecting the CA bundle. If managing certificates manually, re-export the CA and patch the webhook configurations:

```bash
CA_BUNDLE=$(kubectl get secret webhook-ca -n mc-operator-system -o jsonpath='{.data.ca\.pem}')

kubectl patch validatingwebhookconfiguration mc-operator-validators \
  --type='json' \
  -p="[{\"op\": \"replace\", \"path\": \"/webhooks/0/clientConfig/caBundle\", \"value\": \"${CA_BUNDLE}\"}]"

kubectl patch mutatingwebhookconfiguration mc-operator-mutators \
  --type='json' \
  -p="[{\"op\": \"replace\", \"path\": \"/webhooks/0/clientConfig/caBundle\", \"value\": \"${CA_BUNDLE}\"}]"
```

---

### CRD group mismatch after upgrade

**Error**: `no matches for kind "MinecraftServer" in version "minecraft.operator.io/v1alpha1"`

**Cause**: The API group changed from `minecraft.operator.io` to `mc-operator.dhv.sh` in this version.

**Fix**: Update your manifests and existing resources to use the new API group:

```yaml
# Old (pre-rename)
apiVersion: minecraft.operator.io/v1alpha1

# New
apiVersion: mc-operator.dhv.sh/v1alpha1
```

If you have existing resources in the cluster under the old group, you need to export their specs, delete them, apply the new CRD, and recreate them under the new API group.

<Aside type="caution">
The PVC names (`data-<name>-0`) do not depend on the API group, so world data is preserved as long as the PVC isn't deleted.
</Aside>

---

## FAQ

### Can I run multiple Minecraft servers in the same namespace?

Yes. Each `MinecraftServer` resource creates its own set of child resources (StatefulSet, Service, PVC) named after the `MinecraftServer` metadata name. Deploy as many as your cluster resources support.

### Can I run more than one replica?

No. `spec.replicas` is constrained to `0` or `1`. Minecraft is not horizontally scalable — it maintains mutable world state on disk that cannot be safely shared between processes.

### How do I exec into a running server?

```bash
kubectl exec -it -n minecraft <name>-0 -- bash
```

Note: The chiseled base image (`itzg/minecraft-server`) does have a shell available for runtime use (unlike the operator image itself). If your custom image is also chiseled, use `/bin/sh` cautiously.

### Where is the server console?

Attach to the running container:

```bash
kubectl attach -it -n minecraft <name>-0
```

This attaches to the Minecraft server stdin/stdout, giving you the server console. Press Ctrl+C carefully — it may send a signal to the container. Use `kubectl exec` to run individual commands.

### Can I install plugins?

Not automatically in v1. Options:
1. **Init container**: Download plugin JARs to the data volume before the server starts
2. **Custom image**: Build a custom image that extends `itzg/minecraft-server` with plugins pre-baked in
3. **Exec**: Manually copy JARs into the running pod with `kubectl cp`

### How do I migrate from another operator/deployment?

1. Stop the existing server
2. Create a new namespace or use the same namespace
3. Create the PVC with your data (or use an existing PVC)
4. Create the `MinecraftServer` resource with `spec.storage.enabled: false` if the PVC already exists as a pre-provisioned volume

Note: If using StatefulSet VolumeClaimTemplates, the PVC name must match `data-<server-name>-0` for automatic reattachment.

### Is ARM64 supported?

Yes. The container image is built for both `linux/amd64` and `linux/arm64` via multi-arch builds in the release pipeline.

### How do I check the operator version?

```bash
helm list -n mc-operator-system
```

Or check the image tag:

```bash
kubectl get deployment mc-operator -n mc-operator-system \
  -o jsonpath='{.spec.template.spec.containers[0].image}'
```
