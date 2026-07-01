"use client";

import { useEffect, useState } from "react";
import { History } from "lucide-react";

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

export function HistoryTable({ config, projectId }: { config: ApiConfig; projectId: string }) {
  const [events, setEvents] = useState<HistoryEvent[] | null>(null);

  useEffect(() => {
    let cancelled = false;
    api
      .history(config, projectId, 20)
      .then((res) => {
        if (!cancelled) setEvents(res);
      })
      .catch(() => {
        if (!cancelled) setEvents([]);
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.baseUrl, config.token, projectId]);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <History className="size-4" />
          Histórico
        </CardTitle>
      </CardHeader>
      <CardContent>
        {!events || events.length === 0 ? (
          <p className="text-sm text-muted-foreground">sem eventos ainda</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Quando</TableHead>
                <TableHead>Evento</TableHead>
                <TableHead>Mensagem</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {events.map((event, i) => (
                <TableRow key={`${event.created_at}-${i}`}>
                  <TableCell className="text-muted-foreground">{event.created_at}</TableCell>
                  <TableCell>
                    <Badge variant={EVENT_VARIANT[event.type] ?? "outline"}>{event.type}</Badge>
                  </TableCell>
                  <TableCell className="text-muted-foreground">{event.message || "—"}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}
