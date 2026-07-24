# Warden — Roadmap de melhorias pós-migração

Fonte da verdade de arquitetura: [NEW_CONTEXT.md](NEW_CONTEXT.md)  
Todas as issues: https://github.com/valb-mig/app.warden/issues

---

## Estado atual (2026-07-24)

Migração C# concluída (fases 1-9). 181 testes xUnit verdes.  
PR aberto: [#17 — GET /health + prefixo /v1/](https://github.com/valb-mig/app.warden/pull/17) (aguardando merge)

---

## Próxima issue: #7 — Trust status invisível no Console

**O problema:** quando um projeto entra em `PendingReview` (ex: `git pull` muda o manifesto), o Console recebe 403 ao tentar iniciar — sem indicação do porquê.

**O que fazer:**
1. `Warden.Contracts/Projects/ProjectDtos.cs` — adicionar `TrustStatus` no `StatusDto`
2. `Warden.Domain/Engine.cs` — preencher o campo a partir de `ManifestRegistry.GetStatus(id)`  
3. `Warden.Agent/Api/ProjectEndpoints.cs` — mapear o campo na resposta de `/v1/projects/{id}/status`
4. `web/src/app/projects/[id]/page.tsx` — badge "Aguardando aprovação no Admin" quando `trust_status != "approved"`, botão de start desabilitado com razão explícita

---

## Sequência recomendada das issues abertas

| # | Issue | Tipo |
|---|-------|------|
| **7** | Trust status no Console | ux/bug |
| **8** | Scoped tokens (CLI + middleware) | feature |
| **9** | Decomposição do `Engine.cs` (God Object) | arquitetura — **fazer antes das features seguintes** |
| **10** | Log streaming real via `Channel<string>` | feature |
| **11** | Cron Actions | feature |
| **12** | Editor de `.env` (só Admin socket) | feature |
| **13** | Dashboard cross-machine agregado | estratégico |
| **14** | Web Push API | feature |
| **15** | Windows: named pipe + Service | plataforma |
| **16** | Warden Gateway | backlog |
