"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { Loader2, Search, TriangleAlert, X } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { api, type ApiConfig } from "@/lib/api";

const MAX_LINES = 500;
const ALL_SERVICES = "__all__";

// Cobre o comum quando o projeto não declara error_patterns próprios (config).
const DEFAULT_ERROR_PATTERNS = [
  "\\bERROR\\b",
  "\\bEXCEPTION\\b",
  "\\bFATAL\\b",
  "\\bTRACEBACK\\b",
  "\\bFAIL(?:ED)?\\b",
];

function compilePatterns(patterns: string[]): RegExp[] {
  const compiled: RegExp[] = [];
  for (const pattern of patterns) {
    try {
      compiled.push(new RegExp(pattern, "i"));
    } catch {
      // sintaxe de regex do Python que não é válida em JS (ex: lookbehind variável) — ignora
    }
  }
  return compiled;
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

export function LogViewer({ config, projectId }: { config: ApiConfig; projectId: string }) {
  const [services, setServices] = useState<string[]>([]);
  const [errorPatterns, setErrorPatterns] = useState<RegExp[]>(
    compilePatterns(DEFAULT_ERROR_PATTERNS),
  );
  const [selected, setSelected] = useState(ALL_SERVICES);
  const [query, setQuery] = useState("");
  const [onlyErrors, setOnlyErrors] = useState(false);

  useEffect(() => {
    let cancelled = false;
    api
      .services(config, projectId)
      .then((res) => {
        if (cancelled) return;
        setServices(res.services);
        if (res.error_patterns.length > 0) {
          setErrorPatterns(compilePatterns(res.error_patterns));
        }
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.baseUrl, config.token, projectId]);

  return (
    <Card className="flex flex-1 flex-col">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <span className="flex-1">Logs ao vivo</span>
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-1 flex-col gap-3">
        <div className="flex flex-wrap items-center gap-2">
          {services.length > 0 && (
            <Tabs value={selected} onValueChange={setSelected}>
              <TabsList>
                <TabsTrigger value={ALL_SERVICES}>todos</TabsTrigger>
                {services.map((service) => (
                  <TabsTrigger key={service} value={service}>
                    {service}
                  </TabsTrigger>
                ))}
              </TabsList>
            </Tabs>
          )}

          <div className="relative min-w-40 flex-1">
            <Search className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-muted-foreground" />
            <Input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="buscar nos logs..."
              className="h-8 pl-7"
            />
            {query && (
              <button
                type="button"
                onClick={() => setQuery("")}
                className="absolute top-1/2 right-2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
              >
                <X className="size-3.5" />
              </button>
            )}
          </div>

          <Button
            size="sm"
            variant={onlyErrors ? "default" : "outline"}
            onClick={() => setOnlyErrors((v) => !v)}
          >
            <TriangleAlert className="size-3.5" />
            só erros
          </Button>
        </div>

        <LogStream
          key={selected}
          config={config}
          projectId={projectId}
          service={selected === ALL_SERVICES ? undefined : selected}
          query={query}
          onlyErrors={onlyErrors}
          errorPatterns={errorPatterns}
        />
      </CardContent>
    </Card>
  );
}

function LogStream({
  config,
  projectId,
  service,
  query,
  onlyErrors,
  errorPatterns,
}: {
  config: ApiConfig;
  projectId: string;
  service?: string;
  query: string;
  onlyErrors: boolean;
  errorPatterns: RegExp[];
}) {
  const [lines, setLines] = useState<string[]>([]);
  const [connected, setConnected] = useState(false);
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    // WS manda o backlog inteiro do ring buffer assim que conecta — não precisa de fetch REST separado.
    const ws = new WebSocket(api.wsUrl(config, projectId, service));
    ws.onopen = () => setConnected(true);
    ws.onmessage = (event) => {
      setLines((prev) => [...prev, event.data as string].slice(-MAX_LINES));
    };

    return () => {
      ws.close();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [config.baseUrl, config.token, projectId, service]);

  const isError = useMemo(
    () => (line: string) => errorPatterns.some((p) => p.test(line)),
    [errorPatterns],
  );

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return lines.filter((line) => {
      if (onlyErrors && !isError(line)) return false;
      if (q && !line.toLowerCase().includes(q)) return false;
      return true;
    });
  }, [lines, query, onlyErrors, isError]);

  useEffect(() => {
    // com busca ativa o usuário provavelmente tá lendo algo específico — não força scroll.
    if (query) return;
    bottomRef.current?.scrollIntoView({ block: "end" });
  }, [filtered, query]);

  return (
    <ScrollArea className="h-80 rounded-md border bg-muted/30 p-3">
      {!connected ? (
        <p className="flex items-center gap-2 text-sm text-muted-foreground">
          <Loader2 className="size-3.5 animate-spin" />
          conectando...
        </p>
      ) : filtered.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          {lines.length === 0 ? "sem saída ainda" : "nenhuma linha bate com o filtro"}
        </p>
      ) : (
        <pre className="whitespace-pre-wrap font-mono text-xs leading-relaxed">
          {filtered.map((line, i) => (
            <LogLine key={i} line={line} query={query} isError={isError(line)} />
          ))}
        </pre>
      )}
      <div ref={bottomRef} />
    </ScrollArea>
  );
}

function LogLine({ line, query, isError }: { line: string; query: string; isError: boolean }) {
  const content = query ? highlight(line, query) : line;
  return <div className={isError ? "text-destructive" : undefined}>{content}</div>;
}

function highlight(line: string, query: string): React.ReactNode {
  const re = new RegExp(`(${escapeRegExp(query)})`, "ig");
  const parts = line.split(re);
  if (parts.length === 1) return line;
  return parts.map((part, i) =>
    i % 2 === 1 ? (
      <mark key={i} className="rounded-sm bg-yellow-300/60 text-inherit dark:bg-yellow-500/40">
        {part}
      </mark>
    ) : (
      part
    ),
  );
}
