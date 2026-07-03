"use client";

import { useEffect, useState } from "react";
import { History, Loader2 } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { api, type ApiConfig, type HistoryEvent } from "@/lib/api";

const EVENT_VARIANT: Record<string, "default" | "secondary" | "outline" | "destructive"> = {
  started: "default",
  stopped: "secondary",
  finished: "outline",
  error: "destructive",
};

function formatWhen(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return new Intl.DateTimeFormat("pt-BR", {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  }).format(date);
}

export function HistoryTable({ config, projectId }: { config: ApiConfig; projectId: string }) {
  const [events, setEvents] = useState<HistoryEvent[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [expanded, setExpanded] = useState<Set<number>>(new Set());

  useEffect(() => {
    let cancelled = false;
    api
      .history(config, projectId, 20)
      .then((res) => {
        if (!cancelled) setEvents(res);
      })
      .catch(() => {
        if (!cancelled) {
          setEvents([]);
          setFailed(true);
        }
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.baseUrl, config.token, projectId]);

  const toggleExpanded = (i: number) => {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(i)) {
        next.delete(i);
      } else {
        next.add(i);
      }
      return next;
    });
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <History className="size-4" />
          Histórico
        </CardTitle>
      </CardHeader>
      <CardContent>
        {events === null ? (
          <p className="flex items-center gap-2 text-sm text-muted-foreground">
            <Loader2 className="size-3.5 animate-spin" />
            carregando...
          </p>
        ) : events.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            {failed ? "falha ao carregar histórico" : "sem eventos ainda"}
          </p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-36">Quando</TableHead>
                <TableHead className="w-28">Evento</TableHead>
                <TableHead>Mensagem</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {events.map((event, i) => {
                const message = event.message || "—";
                const isLong = message.length > 80;
                const isExpanded = expanded.has(i);
                return (
                  <TableRow key={`${event.created_at}-${i}`}>
                    <TableCell
                      className="whitespace-nowrap text-muted-foreground"
                      title={event.created_at}
                    >
                      {formatWhen(event.created_at)}
                    </TableCell>
                    <TableCell>
                      <Badge variant={EVENT_VARIANT[event.type] ?? "outline"}>{event.type}</Badge>
                    </TableCell>
                    <TableCell
                      className={`max-w-md font-mono text-xs text-muted-foreground ${
                        isExpanded ? "wrap-break-word whitespace-pre-wrap" : "truncate"
                      } ${isLong ? "cursor-pointer" : ""}`}
                      onClick={isLong ? () => toggleExpanded(i) : undefined}
                      title={isLong && !isExpanded ? message : undefined}
                    >
                      {message}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}
