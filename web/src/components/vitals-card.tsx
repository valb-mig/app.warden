"use client";

import { useMemo, useState } from "react";

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Activity } from "lucide-react";

export interface VitalSample {
  t: number;
  cpu: number;
  mem: number;
}

const WIDTH = 300;
const HEIGHT = 56;
const PADDING = 6;

function pointsFor(values: number[]): string {
  if (values.length < 2) return "";
  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = max - min || 1;
  const step = WIDTH / (values.length - 1);
  return values
    .map((v, i) => {
      const x = i * step;
      const y = HEIGHT - PADDING - ((v - min) / span) * (HEIGHT - PADDING * 2);
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(" ");
}

function yFor(values: number[], index: number): number {
  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = max - min || 1;
  return HEIGHT - PADDING - ((values[index] - min) / span) * (HEIGHT - PADDING * 2);
}

function Sparkline({
  label,
  values,
  timestamps,
  format,
}: {
  label: string;
  values: number[];
  timestamps: number[];
  format: (v: number) => string;
}) {
  const [hoverIndex, setHoverIndex] = useState<number | null>(null);
  const points = useMemo(() => pointsFor(values), [values]);
  const current = values[values.length - 1];

  function handleMove(e: React.MouseEvent<SVGSVGElement>) {
    const rect = e.currentTarget.getBoundingClientRect();
    const ratio = (e.clientX - rect.left) / rect.width;
    const index = Math.round(ratio * (values.length - 1));
    setHoverIndex(Math.min(Math.max(index, 0), values.length - 1));
  }

  const shown = hoverIndex ?? values.length - 1;
  const hoverX = values.length > 1 ? (shown * WIDTH) / (values.length - 1) : 0;
  const hoverY = yFor(values, shown);
  const ago = Math.round((timestamps[timestamps.length - 1] - timestamps[shown]) / 1000);

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-baseline justify-between">
        <span className="text-xs text-muted-foreground">{label}</span>
        <span className="font-semibold">{format(current)}</span>
      </div>
      <svg
        viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
        preserveAspectRatio="none"
        className="h-14 w-full cursor-crosshair text-foreground"
        onMouseMove={handleMove}
        onMouseLeave={() => setHoverIndex(null)}
      >
        <polyline
          points={`0,${HEIGHT} ${points} ${WIDTH},${HEIGHT}`}
          fill="currentColor"
          opacity={0.08}
          stroke="none"
        />
        <polyline points={points} fill="none" stroke="currentColor" strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
        {values.length > 1 && (
          <line
            x1={hoverX}
            y1={0}
            x2={hoverX}
            y2={HEIGHT}
            stroke="currentColor"
            strokeWidth={1}
            opacity={0.2}
          />
        )}
        <circle cx={hoverX} cy={hoverY} r={4} fill="currentColor" stroke="var(--card)" strokeWidth={2} />
      </svg>
      {hoverIndex !== null && (
        <p className="text-right text-xs text-muted-foreground">
          {format(values[hoverIndex])} · {ago === 0 ? "agora" : `${ago}s atrás`}
        </p>
      )}
    </div>
  );
}

export function VitalsCard({ samples }: { samples: VitalSample[] }) {
  if (samples.length < 2) return null;

  const timestamps = samples.map((s) => s.t);
  const cpuValues = samples.map((s) => s.cpu);
  const memValues = samples.map((s) => s.mem);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <Activity className="size-4" />
          Vitals
        </CardTitle>
      </CardHeader>
      <CardContent className="grid grid-cols-1 gap-6 sm:grid-cols-2">
        <Sparkline
          label="CPU"
          values={cpuValues}
          timestamps={timestamps}
          format={(v) => `${v.toFixed(0)}%`}
        />
        <Sparkline
          label="RAM"
          values={memValues}
          timestamps={timestamps}
          format={(v) => `${v.toFixed(0)} MB`}
        />
      </CardContent>
    </Card>
  );
}
