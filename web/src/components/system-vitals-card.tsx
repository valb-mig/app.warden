"use client";

import { useEffect, useState } from "react";
import { Cpu } from "lucide-react";

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { api, type ApiConfig, type SystemVitals } from "@/lib/api";

const POLL_MS = 3000;
const CRITICAL_THRESHOLD = 90;

function Meter({ label, percent, detail }: { label: string; percent: number; detail: string }) {
  const critical = percent >= CRITICAL_THRESHOLD;
  return (
    <div className="flex flex-1 flex-col gap-1">
      <div className="flex items-baseline justify-between text-sm">
        <span className="text-muted-foreground">{label}</span>
        <span className="font-semibold">{percent.toFixed(0)}%</span>
      </div>
      <div className="h-2 w-full overflow-hidden rounded-full bg-muted">
        <div
          className={`h-full rounded-full transition-all ${critical ? "bg-destructive" : "bg-foreground"}`}
          style={{ width: `${Math.min(percent, 100)}%` }}
        />
      </div>
      <span className="text-xs text-muted-foreground">{detail}</span>
    </div>
  );
}

export function SystemVitalsCard({ config }: { config: ApiConfig }) {
  const [vitals, setVitals] = useState<SystemVitals | null>(null);

  useEffect(() => {
    let cancelled = false;
    const poll = () => {
      api
        .systemVitals(config)
        .then((v) => !cancelled && setVitals(v))
        .catch(() => {});
    };
    poll();
    const interval = setInterval(poll, POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.baseUrl, config.token]);

  if (!vitals) return null;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Cpu className="size-4" />
          Máquina
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-4 sm:flex-row sm:gap-6">
        <Meter label="CPU" percent={vitals.cpu_percent} detail="uso agregado" />
        <Meter
          label="RAM"
          percent={vitals.memory_percent}
          detail={`${vitals.memory_used_mb.toFixed(0)} / ${vitals.memory_total_mb.toFixed(0)} MB`}
        />
        <Meter
          label="Disco"
          percent={vitals.disk_percent}
          detail={`${vitals.disk_used_gb.toFixed(1)} / ${vitals.disk_total_gb.toFixed(1)} GB`}
        />
      </CardContent>
    </Card>
  );
}
