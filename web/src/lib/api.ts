export type ProjectType = "docker" | "node" | "python" | "php" | "just" | "raw";

export interface Project {
  id: string;
  name: string;
  type: ProjectType;
  group: string | null;
}

export interface ProjectStatus {
  running: boolean;
  pid: number | null;
  ports: number[];
  uptime_seconds: number | null;
  cpu_percent: number | null;
  memory_mb: number | null;
  trust_status: "approved" | "pending_review" | "never_approved";
}

export interface HistoryEvent {
  project_id: string;
  type: string;
  message: string;
  created_at: string;
}

export interface ActionResult {
  exit_code: number;
  output: string;
}

export interface Action {
  name: string;
  interactive: boolean;
}

export interface GitCommit {
  hash: string;
  subject: string;
  author: string;
  relative: string;
}

export interface GitInfo {
  branch: string;
  dirty: boolean;
  dirty_count: number;
  ahead: number | null;
  behind: number | null;
  has_remote: boolean;
  last_commit: GitCommit | null;
}

export type GitVerb = "fetch" | "sync" | "pull" | "push";

export interface GitCommandResult {
  ok: boolean;
  exit_code: number;
  output: string;
  refused: boolean;
}

export interface ApiConfig {
  baseUrl: string;
  token: string;
}

export interface StartConfig {
  cmd: string[];
  cwd: string | null;
  capture_stdout: boolean;
}

export interface NotifyConfig {
  on_error: boolean;
  on_finished: boolean;
  on_git_behind: boolean;
}

export interface GitWatchConfig {
  watch: boolean;
  interval: number;
  remote: string;
}

export interface LogSourceConfig {
  name: string;
  type: "stdout" | "file" | "docker";
  path: string | null;
  service: string | null;
  error_patterns: string[];
}

export interface ProjectAction {
  name: string;
  cmd: string[];
  interactive: boolean;
  destructive: boolean;
}

export interface ProjectConfigPayload {
  id: string;
  name: string | null;
  group: string | null;
  path: string;
  type: ProjectType;
  compose_file: string | null;
  start: StartConfig | null;
  notify: NotifyConfig;
  git: GitWatchConfig;
  log_sources: LogSourceConfig[];
  actions: ProjectAction[];
}

export interface DiscoveredProject {
  name: string;
  path: string;
  type: ProjectType;
}

export interface ConfigPreview {
  config: ProjectConfigPayload;
  toml: string;
}

export interface BrowseEntry {
  name: string;
  path: string;
}

export interface BrowseResult {
  path: string;
  parent: string | null;
  entries: BrowseEntry[];
}

export interface SystemVitals {
  cpu_percent: number;
  memory_percent: number;
  memory_used_mb: number;
  memory_total_mb: number;
  disk_percent: number;
  disk_used_gb: number;
  disk_total_gb: number;
}

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}

async function request<T>(config: ApiConfig, path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${config.baseUrl}${path}`, {
    ...init,
    headers: {
      ...(init?.headers ?? {}),
      Authorization: `Bearer ${config.token}`,
    },
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new ApiError(res.status, body.detail ?? res.statusText);
  }
  return res.json();
}

export const api = {
  listProjects: (c: ApiConfig) => request<Project[]>(c, "/v1/projects"),

  start: (c: ApiConfig, id: string) =>
    request<{ ok: boolean }>(c, `/v1/projects/${id}/start`, { method: "POST" }),

  stop: (c: ApiConfig, id: string) =>
    request<{ ok: boolean }>(c, `/v1/projects/${id}/stop`, { method: "POST" }),

  status: (c: ApiConfig, id: string) => request<ProjectStatus>(c, `/v1/projects/${id}/status`),

  logs: (c: ApiConfig, id: string, tail = 200) =>
    request<{ lines: string[] }>(c, `/v1/projects/${id}/logs?tail=${tail}`),

  history: (c: ApiConfig, id: string, limit = 50) =>
    request<HistoryEvent[]>(c, `/v1/projects/${id}/history?limit=${limit}`),

  git: (c: ApiConfig, id: string) => request<GitInfo | null>(c, `/v1/projects/${id}/git`),

  gitCommand: (c: ApiConfig, id: string, verb: GitVerb, confirm = false) =>
    request<GitCommandResult>(
      c,
      `/v1/projects/${id}/git/${verb}${confirm ? "?confirm=true" : ""}`,
      { method: "POST" },
    ),

  listActions: (c: ApiConfig, id: string) => request<Action[]>(c, `/v1/projects/${id}/actions`),

  runAction: (c: ApiConfig, id: string, action: string) =>
    request<ActionResult>(c, `/v1/projects/${id}/actions/${action}`, { method: "POST" }),

  /** Hub SignalR do Warden.Agent — auth via `access_token` na query (browser não permite header custom em WS), ver Warden.Agent/Hubs/LogsHub.cs. */
  hubUrl: (c: ApiConfig) => `${c.baseUrl}/hubs/logs`,

  services: (c: ApiConfig, id: string) =>
    request<{ services: string[]; error_patterns: string[] }>(c, `/v1/projects/${id}/services`),

  languages: (c: ApiConfig, id: string) =>
    request<{ languages: string[] }>(c, `/v1/projects/${id}/languages`),

  getScanPaths: (c: ApiConfig) => request<{ scan_paths: string[] }>(c, "/v1/scan-paths"),

  addScanPath: (c: ApiConfig, path: string) =>
    request<{ scan_paths: string[] }>(c, "/v1/scan-paths", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ path }),
    }),

  removeScanPath: (c: ApiConfig, path: string) =>
    request<{ scan_paths: string[] }>(c, "/v1/scan-paths", {
      method: "DELETE",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ path }),
    }),

  discover: (c: ApiConfig) => request<{ projects: DiscoveredProject[] }>(c, "/v1/discover"),

  browse: (c: ApiConfig, path?: string) =>
    request<BrowseResult>(c, `/v1/browse${path ? `?path=${encodeURIComponent(path)}` : ""}`),

  systemVitals: (c: ApiConfig) => request<SystemVitals>(c, "/v1/system/vitals"),

  previewConfig: (c: ApiConfig, path: string, id?: string) =>
    request<ConfigPreview>(c, "/v1/discover/preview", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ path, id: id ?? null }),
    }),

  applyConfig: (c: ApiConfig, config: ProjectConfigPayload) =>
    request<ConfigPreview>(c, "/v1/discover/apply", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(config),
    }),

  getProjectConfig: (c: ApiConfig, id: string) =>
    request<ProjectConfigPayload>(c, `/v1/projects/${id}/config`),
};
