export function DashboardHeader({
  serverCount,
  clusterCount,
}: {
  serverCount: number;
  clusterCount: number;
}) {
  return (
    <header className="border-b border-border bg-card/50 backdrop-blur-sm">
      <div className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-bold tracking-tight text-foreground">
              ⛏️ mc-operator
            </h1>
            <p className="mt-1 text-sm text-muted-foreground">
              Minecraft Server Dashboard
            </p>
          </div>
          <div className="flex gap-4">
            <div className="text-center">
              <p className="text-2xl font-bold text-foreground">
                {serverCount}
              </p>
              <p className="text-xs text-muted-foreground">Servers</p>
            </div>
            <div className="text-center">
              <p className="text-2xl font-bold text-foreground">
                {clusterCount}
              </p>
              <p className="text-xs text-muted-foreground">Clusters</p>
            </div>
          </div>
        </div>
      </div>
    </header>
  );
}
