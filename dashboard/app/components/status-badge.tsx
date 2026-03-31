import { Badge } from "~/components/ui/badge";

type ServerPhase =
  | "Pending"
  | "Provisioning"
  | "Running"
  | "Paused"
  | "Failed"
  | "Terminating"
  | "Degraded";

const phaseConfig: Record<
  ServerPhase,
  {
    variant: "default" | "secondary" | "destructive" | "success" | "warning";
    label: string;
  }
> = {
  Running: { variant: "success", label: "Running" },
  Pending: { variant: "secondary", label: "Pending" },
  Provisioning: { variant: "warning", label: "Provisioning" },
  Paused: { variant: "secondary", label: "Paused" },
  Failed: { variant: "destructive", label: "Failed" },
  Terminating: { variant: "warning", label: "Terminating" },
  Degraded: { variant: "warning", label: "Degraded" },
};

export function StatusBadge({ phase }: { phase: string }) {
  const config = phaseConfig[phase as ServerPhase] ?? {
    variant: "secondary" as const,
    label: phase,
  };
  return <Badge variant={config.variant}>{config.label}</Badge>;
}
