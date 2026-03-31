import { createServerFn } from "@tanstack/react-start";
import type { ServerInfo } from "~/components/server-card";
import type { ClusterInfo } from "~/components/cluster-card";

// Types matching the CRD structures
interface MinecraftServerResource {
  metadata: { name: string; namespace: string };
  spec: {
    server: { type: string; version: string };
    properties: { maxPlayers: number };
    replicas: number;
  };
  status?: {
    phase: string;
    readyReplicas: number;
    currentVersion?: string;
    endpoint?: { host?: string; port: number; connectionString?: string };
    storage?: { name?: string; phase?: string; capacity?: string };
    message?: string;
  };
}

interface MinecraftServerClusterResource {
  metadata: { name: string; namespace: string };
  spec: {
    template: { server: { type: string; version: string } };
    scaling: { mode: string; replicas: number };
  };
  status?: {
    phase: string;
    readyServers: number;
    totalServers: number;
    readyProxyReplicas: number;
    proxyEndpoint?: { host?: string; port: number; connectionString?: string };
    servers: Array<{ name: string; phase: string }>;
    message?: string;
  };
}

interface KubeResourceList<T> {
  items: T[];
}

const API_GROUP = "mc-operator.dhv.sh";
const API_VERSION = "v1alpha1";

function getKubeApiBase(): string {
  // In-cluster: use the service account token
  if (process.env.KUBERNETES_SERVICE_HOST) {
    const host = process.env.KUBERNETES_SERVICE_HOST;
    const port = process.env.KUBERNETES_SERVICE_PORT ?? "443";
    return `https://${host}:${port}`;
  }
  // Out-of-cluster: use kubectl proxy or configured API server
  return process.env.KUBE_API_URL ?? "http://localhost:8001";
}

async function kubeRequest<T>(path: string): Promise<T> {
  const base = getKubeApiBase();
  const url = `${base}${path}`;

  const headers: Record<string, string> = {
    Accept: "application/json",
  };

  // In-cluster: use the service account token
  if (process.env.KUBERNETES_SERVICE_HOST) {
    try {
      const { readFileSync } = await import("node:fs");
      const token = readFileSync(
        "/var/run/secrets/kubernetes.io/serviceaccount/token",
        "utf-8",
      ).trim();
      headers["Authorization"] = `Bearer ${token}`;
    } catch {
      // Token not available — will proceed without auth
    }
  }

  const response = await fetch(url, { headers });

  if (!response.ok) {
    throw new Error(
      `Kubernetes API error: ${response.status} ${response.statusText}`,
    );
  }

  return response.json() as Promise<T>;
}

export const fetchServers = createServerFn({ method: "GET" }).handler(
  async (): Promise<ServerInfo[]> => {
    try {
      const list = await kubeRequest<
        KubeResourceList<MinecraftServerResource>
      >(`/apis/${API_GROUP}/${API_VERSION}/minecraftservers`);

      return list.items.map((item) => ({
        name: item.metadata.name,
        namespace: item.metadata.namespace,
        phase: item.status?.phase ?? "Pending",
        serverType: item.spec.server.type,
        version: item.spec.server.version,
        currentVersion: item.status?.currentVersion,
        maxPlayers: item.spec.properties?.maxPlayers ?? 20,
        endpoint: item.status?.endpoint?.connectionString,
        readyReplicas: item.status?.readyReplicas ?? 0,
        storage: item.status?.storage
          ? {
              name: item.status.storage.name,
              phase: item.status.storage.phase,
              capacity: item.status.storage.capacity,
            }
          : undefined,
      }));
    } catch (error) {
      console.error("Failed to fetch Minecraft servers:", error);
      return [];
    }
  },
);

export const fetchClusters = createServerFn({ method: "GET" }).handler(
  async (): Promise<ClusterInfo[]> => {
    try {
      const list = await kubeRequest<
        KubeResourceList<MinecraftServerClusterResource>
      >(`/apis/${API_GROUP}/${API_VERSION}/minecraftserverclusters`);

      return list.items.map((item) => ({
        name: item.metadata.name,
        namespace: item.metadata.namespace,
        phase: item.status?.phase ?? "Pending",
        version: item.spec.template.server.version,
        scalingMode: item.spec.scaling.mode,
        readyServers: item.status?.readyServers ?? 0,
        totalServers: item.status?.totalServers ?? 0,
        readyProxyReplicas: item.status?.readyProxyReplicas ?? 0,
        proxyEndpoint: item.status?.proxyEndpoint?.connectionString,
        servers: item.status?.servers ?? [],
      }));
    } catch (error) {
      console.error("Failed to fetch Minecraft server clusters:", error);
      return [];
    }
  },
);
