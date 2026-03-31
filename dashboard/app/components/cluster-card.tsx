import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "~/components/ui/card";
import { Badge } from "~/components/ui/badge";
import { StatusBadge } from "~/components/status-badge";

export interface ClusterInfo {
  name: string;
  namespace: string;
  phase: string;
  version: string;
  scalingMode: string;
  readyServers: number;
  totalServers: number;
  readyProxyReplicas: number;
  proxyEndpoint?: string;
  servers: Array<{
    name: string;
    phase: string;
  }>;
}

export function ClusterCard({ cluster }: { cluster: ClusterInfo }) {
  return (
    <Card className="transition-all hover:shadow-md">
      <CardHeader className="pb-3">
        <div className="flex items-start justify-between">
          <div className="space-y-1">
            <CardTitle className="text-lg">{cluster.name}</CardTitle>
            <p className="text-xs text-muted-foreground">
              {cluster.namespace}
            </p>
          </div>
          <StatusBadge phase={cluster.phase} />
        </div>
      </CardHeader>
      <CardContent>
        <div className="grid gap-3">
          {/* Version & Scaling */}
          <div className="flex items-center gap-2">
            <Badge variant="outline">Cluster</Badge>
            <Badge variant="outline">{cluster.scalingMode}</Badge>
            <span className="text-sm text-muted-foreground">
              {cluster.version}
            </span>
          </div>

          {/* Server Count */}
          <div className="flex items-center justify-between rounded-md bg-secondary/50 px-3 py-2">
            <span className="text-sm text-muted-foreground">
              Backend Servers
            </span>
            <span className="font-mono text-sm font-medium">
              {cluster.readyServers} / {cluster.totalServers} ready
            </span>
          </div>

          {/* Proxy Status */}
          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">Proxy</span>
            <Badge
              variant={
                cluster.readyProxyReplicas > 0 ? "success" : "warning"
              }
            >
              {cluster.readyProxyReplicas > 0 ? "Ready" : "Not Ready"}
            </Badge>
          </div>

          {/* Proxy Endpoint */}
          {cluster.proxyEndpoint && (
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">
                Proxy Endpoint
              </span>
              <code className="rounded bg-secondary px-2 py-0.5 font-mono text-xs">
                {cluster.proxyEndpoint}
              </code>
            </div>
          )}

          {/* Backend Server List */}
          {cluster.servers.length > 0 && (
            <div className="space-y-2">
              <span className="text-sm text-muted-foreground">Servers</span>
              <div className="space-y-1">
                {cluster.servers.map((server) => (
                  <div
                    key={server.name}
                    className="flex items-center justify-between rounded bg-secondary/30 px-2 py-1"
                  >
                    <span className="font-mono text-xs">{server.name}</span>
                    <StatusBadge phase={server.phase} />
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
