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
  listProjects: (c: ApiConfig) => request<Project[]>(c, "/projects"),

  start: (c: ApiConfig, id: string) =>
    request<{ ok: boolean }>(c, `/projects/${id}/start`, { method: "POST" }),

  stop: (c: ApiConfig, id: string) =>
    request<{ ok: boolean }>(c, `/projects/${id}/stop`, { method: "POST" }),

  status: (c: ApiConfig, id: string) => request<ProjectStatus>(c, `/projects/${id}/status`),

  logs: (c: ApiConfig, id: string, tail = 200) =>
    request<{ lines: string[] }>(c, `/projects/${id}/logs?tail=${tail}`),

  history: (c: ApiConfig, id: string, limit = 50) =>
    request<HistoryEvent[]>(c, `/projects/${id}/history?limit=${limit}`),

  git: (c: ApiConfig, id: string) => request<GitInfo | null>(c, `/projects/${id}/git`),

  gitCommand: (c: ApiConfig, id: string, verb: GitVerb, confirm = false) =>
    request<GitCommandResult>(
      c,
      `/projects/${id}/git/${verb}${confirm ? "?confirm=true" : ""}`,
      { method: "POST" },
    ),

  listActions: (c: ApiConfig, id: string) => request<Action[]>(c, `/projects/${id}/actions`),

  runAction: (c: ApiConfig, id: string, action: string) =>
    request<ActionResult>(c, `/projects/${id}/actions/${action}`, { method: "POST" }),

  wsUrl: (c: ApiConfig, id: string, service?: string) => {
    const url = new URL(`${c.baseUrl.replace(/^http/, "ws")}/ws/projects/${id}/logs`);
    url.searchParams.set("token", c.token);
    if (service) url.searchParams.set("service", service);
    return url.toString();
  },

  services: (c: ApiConfig, id: string) =>
    request<{ services: string[]; error_patterns: string[] }>(c, `/projects/${id}/services`),

  languages: (c: ApiConfig, id: string) =>
    request<{ languages: string[] }>(c, `/projects/${id}/languages`),
};
