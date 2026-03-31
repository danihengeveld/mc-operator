import { createServerFn } from "@tanstack/react-start";

interface RconStatus {
  serverName: string;
  online: boolean;
  playerCount?: number;
  playerList?: string[];
  latencyMs?: number;
  error?: string;
}

interface RconQueryInput {
  host: string;
  port: number;
  password: string;
}

async function queryRcon(input: RconQueryInput): Promise<RconStatus> {
  const start = Date.now();

  try {
    const { Rcon } = await import("rcon-client");
    const rcon = await Rcon.connect({
      host: input.host,
      port: input.port,
      password: input.password,
      timeout: 5000,
    });

    try {
      // Get player list
      const listResponse = await rcon.send("list");
      const latencyMs = Date.now() - start;

      // Parse: "There are X of a max of Y players online: player1, player2"
      const match = listResponse.match(
        /There are (\d+) of a max of \d+ players online:(.*)/i,
      );

      const playerCount = match ? parseInt(match[1], 10) : 0;
      const playerList =
        match && match[2]
          ? match[2]
              .split(",")
              .map((p) => p.trim())
              .filter(Boolean)
          : [];

      return {
        serverName: input.host,
        online: true,
        playerCount,
        playerList,
        latencyMs,
      };
    } finally {
      await rcon.end();
    }
  } catch (error) {
    return {
      serverName: input.host,
      online: false,
      error: error instanceof Error ? error.message : "Unknown error",
      latencyMs: Date.now() - start,
    };
  }
}

export const fetchRconStatus = createServerFn({ method: "GET" })
  .validator(
    (input: {
      servers: Array<{ name: string; host: string; rconPort: number }>;
    }) => input,
  )
  .handler(async ({ data }): Promise<Record<string, RconStatus>> => {
    const password = process.env.RCON_PASSWORD ?? "minecraft";
    const results: Record<string, RconStatus> = {};

    const promises = data.servers.map(async (server) => {
      const status = await queryRcon({
        host: server.host,
        port: server.rconPort,
        password,
      });
      results[server.name] = status;
    });

    await Promise.allSettled(promises);
    return results;
  });
