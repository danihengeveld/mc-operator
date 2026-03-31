import { createFileRoute } from "@tanstack/react-router";
import { DashboardHeader } from "~/components/dashboard-header";
import { ServerCard, type ServerInfo } from "~/components/server-card";
import { ClusterCard, type ClusterInfo } from "~/components/cluster-card";
import { Separator } from "~/components/ui/separator";
import { fetchServers, fetchClusters } from "~/lib/kubernetes";

export const Route = createFileRoute("/")({
  loader: async () => {
    const [servers, clusters] = await Promise.all([
      fetchServers(),
      fetchClusters(),
    ]);
    return { servers, clusters };
  },
  component: DashboardPage,
});

function DashboardPage() {
  const { servers, clusters } = Route.useLoaderData();

  return (
    <div className="min-h-screen">
      <DashboardHeader
        serverCount={servers.length}
        clusterCount={clusters.length}
      />

      <main className="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
        {/* Servers Section */}
        <section>
          <div className="mb-4 flex items-center justify-between">
            <h2 className="text-xl font-semibold text-foreground">Servers</h2>
            <span className="text-sm text-muted-foreground">
              {servers.filter((s: ServerInfo) => s.phase === "Running").length}{" "}
              running
            </span>
          </div>
          {servers.length === 0 ? (
            <EmptyState message="No Minecraft servers found. Deploy a MinecraftServer resource to get started." />
          ) : (
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {servers.map((server: ServerInfo) => (
                <ServerCard
                  key={`${server.namespace}/${server.name}`}
                  server={server}
                />
              ))}
            </div>
          )}
        </section>

        <Separator className="my-8" />

        {/* Clusters Section */}
        <section>
          <div className="mb-4 flex items-center justify-between">
            <h2 className="text-xl font-semibold text-foreground">Clusters</h2>
            <span className="text-sm text-muted-foreground">
              {
                clusters.filter((c: ClusterInfo) => c.phase === "Running")
                  .length
              }{" "}
              running
            </span>
          </div>
          {clusters.length === 0 ? (
            <EmptyState message="No Minecraft server clusters found. Deploy a MinecraftServerCluster resource to get started." />
          ) : (
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {clusters.map((cluster: ClusterInfo) => (
                <ClusterCard
                  key={`${cluster.namespace}/${cluster.name}`}
                  cluster={cluster}
                />
              ))}
            </div>
          )}
        </section>
      </main>

      {/* Footer */}
      <footer className="border-t border-border py-6 text-center text-sm text-muted-foreground">
        <p>
          mc-operator Dashboard — Monitoring {servers.length} server
          {servers.length !== 1 ? "s" : ""} and {clusters.length} cluster
          {clusters.length !== 1 ? "s" : ""}
        </p>
      </footer>
    </div>
  );
}

function EmptyState({ message }: { message: string }) {
  return (
    <div className="rounded-lg border border-dashed border-border p-8 text-center">
      <p className="text-sm text-muted-foreground">{message}</p>
    </div>
  );
}
