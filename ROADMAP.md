# Warden — Roadmap de melhorias pós-migração

Fonte da verdade de arquitetura: [NEW_CONTEXT.md](NEW_CONTEXT.md)  
Todas as issues: https://github.com/valb-mig/app.warden/issues

---

## Estado atual (2026-07-23)

Migração C# concluída (fases 1-9). **207 testes xUnit verdes.**  
Issues #7–#12 implementadas nesta sessão.

---

## Sequência recomendada das issues abertas

| # | Issue | Tipo | Status |
|---|-------|------|--------|
| **7** | Trust status no Console | ux/bug | ✅ feito |
| **8** | Scoped tokens (CLI + middleware) | feature | ✅ feito |
| **9** | Decomposição do `Engine.cs` (God Object) | arquitetura | ✅ feito |
| **10** | Log streaming real via `Channel<string>` | feature | ✅ feito |
| **11** | Cron Actions | feature | ✅ feito |
| **12** | Editor de `.env` (só Admin socket) | feature | ✅ feito |
| **13** | Dashboard cross-machine agregado | estratégico | pendente |
| **14** | Web Push API | feature | pendente |
| **15** | Windows: named pipe + Service | plataforma | pendente |
| **16** | Warden Gateway | backlog | pendente |
