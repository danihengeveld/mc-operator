import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "~/components/ui/card";
import { Badge } from "~/components/ui/badge";
import { StatusBadge } from "~/components/status-badge";

export interface ServerInfo {
  name: string;
  namespace: string;
  phase: string;
  serverType: string;
  version: string;
  currentVersion?: string;
  maxPlayers: number;
  endpoint?: string;
  readyReplicas: number;
  storage?: {
    name?: string;
    phase?: string;
    capacity?: string;
  };
  rcon?: {
    online: boolean;
    playerCount?: number;
    playerList?: string[];
    latencyMs?: number;
  };
}

export function ServerCard({ server }: { server: ServerInfo }) {
  return (
    <Card className="transition-all hover:shadow-md">
      <CardHeader className="pb-3">
        <div className="flex items-start justify-between">
          <div className="space-y-1">
            <CardTitle className="text-lg">{server.name}</CardTitle>
            <p className="text-xs text-muted-foreground">{server.namespace}</p>
          </div>
          <StatusBadge phase={server.phase} />
        </div>
      </CardHeader>
      <CardContent>
        <div className="grid gap-3">
          {/* Server Type & Version */}
          <div className="flex items-center gap-2">
            <Badge variant="outline">{server.serverType}</Badge>
            <span className="text-sm text-muted-foreground">
              {server.currentVersion ?? server.version}
            </span>
          </div>

          {/* Player Count (from RCON) */}
          {server.rcon?.online && (
            <div className="flex items-center justify-between rounded-md bg-secondary/50 px-3 py-2">
              <span className="text-sm text-muted-foreground">Players</span>
              <span className="font-mono text-sm font-medium">
                {server.rcon.playerCount ?? 0} / {server.maxPlayers}
              </span>
            </div>
          )}

          {/* Connection Info */}
          {server.endpoint && (
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">Endpoint</span>
              <code className="rounded bg-secondary px-2 py-0.5 font-mono text-xs">
                {server.endpoint}
              </code>
            </div>
          )}

          {/* RCON Latency */}
          {server.rcon?.online && server.rcon.latencyMs !== undefined && (
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">Latency</span>
              <span className="font-mono text-sm">{server.rcon.latencyMs}ms</span>
            </div>
          )}

          {/* Storage */}
          {server.storage?.capacity && (
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">Storage</span>
              <span className="text-sm">{server.storage.capacity}</span>
            </div>
          )}

          {/* Ready Replicas */}
          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">Replicas</span>
            <span className="font-mono text-sm">
              {server.readyReplicas}/1
            </span>
          </div>

          {/* Player List */}
          {server.rcon?.online &&
            server.rcon.playerList &&
            server.rcon.playerList.length > 0 && (
              <div className="space-y-1">
                <span className="text-sm text-muted-foreground">
                  Online Players
                </span>
                <div className="flex flex-wrap gap-1">
                  {server.rcon.playerList.map((player) => (
                    <Badge key={player} variant="secondary" className="text-xs">
                      {player}
                    </Badge>
                  ))}
                </div>
              </div>
            )}
        </div>
      </CardContent>
    </Card>
  );
}
