using Warden.Domain.Watch;
using Xunit;

namespace Warden.Domain.Tests;

public sealed class CronScheduleTests
{
    // ---- TryParse: expressões válidas ----

    [Fact]
    public void TryParse_ExpressaoValida_RetornaTrue()
    {
        Assert.True(CronSchedule.TryParse("0 2 * * *", out var schedule));
        Assert.NotNull(schedule);
    }

    [Theory]
    [InlineData("")]
    [InlineData("* * * *")]        // só 4 campos
    [InlineData("* * * * * *")]   // 6 campos
    [InlineData("abc * * * *")]   // não-numérico
    public void TryParse_ExpressaoInvalida_RetornaFalse(string expression)
    {
        Assert.False(CronSchedule.TryParse(expression, out var schedule));
        Assert.Null(schedule);
    }

    // ---- Matches: wildcard ----

    [Fact]
    public void Matches_TodosWildcard_SempreVerdadeiro()
    {
        CronSchedule.TryParse("* * * * *", out var schedule);
        var dt = new DateTime(2026, 7, 23, 14, 35, 0, DateTimeKind.Utc);
        Assert.True(schedule!.Matches(dt));
    }

    // ---- Matches: valor literal ----

    [Fact]
    public void Matches_HorarioExato_Verdadeiro()
    {
        CronSchedule.TryParse("30 8 15 6 *", out var schedule);
        var dt = new DateTime(2026, 6, 15, 8, 30, 0, DateTimeKind.Utc); // 2026-06-15 08:30 = segunda
        Assert.True(schedule!.Matches(dt));
    }

    [Fact]
    public void Matches_MinutoErrado_Falso()
    {
        CronSchedule.TryParse("0 8 * * *", out var schedule);
        var dt = new DateTime(2026, 7, 23, 8, 5, 0, DateTimeKind.Utc);
        Assert.False(schedule!.Matches(dt));
    }

    // ---- Matches: lista ----

    [Fact]
    public void Matches_Lista_VerdadeiroParaQualquerValorDaLista()
    {
        CronSchedule.TryParse("0 9,17 * * *", out var schedule);
        Assert.True(schedule!.Matches(new DateTime(2026, 7, 23, 9, 0, 0, DateTimeKind.Utc)));
        Assert.True(schedule.Matches(new DateTime(2026, 7, 23, 17, 0, 0, DateTimeKind.Utc)));
        Assert.False(schedule.Matches(new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc)));
    }

    // ---- Matches: range ----

    [Fact]
    public void Matches_Range_VerdadeiroParaValoresDentroDoRange()
    {
        CronSchedule.TryParse("* * * * 1-5", out var schedule); // segunda a sexta
        Assert.True(schedule!.Matches(new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc)));  // segunda
        Assert.True(schedule.Matches(new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc)));   // sexta
        Assert.False(schedule.Matches(new DateTime(2026, 7, 19, 10, 0, 0, DateTimeKind.Utc)));  // domingo (0)
        Assert.False(schedule.Matches(new DateTime(2026, 7, 25, 10, 0, 0, DateTimeKind.Utc)));  // sábado (6)
    }

    // ---- Matches: step ----

    [Fact]
    public void Matches_Step_CorrespondeApenasAosMultiplos()
    {
        CronSchedule.TryParse("*/15 * * * *", out var schedule); // a cada 15 minutos
        Assert.True(schedule!.Matches(new DateTime(2026, 7, 23, 10, 0, 0, DateTimeKind.Utc)));
        Assert.True(schedule.Matches(new DateTime(2026, 7, 23, 10, 15, 0, DateTimeKind.Utc)));
        Assert.True(schedule.Matches(new DateTime(2026, 7, 23, 10, 30, 0, DateTimeKind.Utc)));
        Assert.True(schedule.Matches(new DateTime(2026, 7, 23, 10, 45, 0, DateTimeKind.Utc)));
        Assert.False(schedule.Matches(new DateTime(2026, 7, 23, 10, 7, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Matches_StepComRange_LimitadoAoRange()
    {
        CronSchedule.TryParse("0-30/10 * * * *", out var schedule);
        Assert.True(schedule!.Matches(new DateTime(2026, 7, 23, 10, 0, 0, DateTimeKind.Utc)));
        Assert.True(schedule.Matches(new DateTime(2026, 7, 23, 10, 10, 0, DateTimeKind.Utc)));
        Assert.True(schedule.Matches(new DateTime(2026, 7, 23, 10, 20, 0, DateTimeKind.Utc)));
        Assert.True(schedule.Matches(new DateTime(2026, 7, 23, 10, 30, 0, DateTimeKind.Utc)));
        Assert.False(schedule.Matches(new DateTime(2026, 7, 23, 10, 40, 0, DateTimeKind.Utc))); // fora do range 0-30
    }

    // ---- CronActionWatcher: executa no horário correto ----

    [Fact]
    public void CronActionWatcher_Executa_QuandoHorarioBate()
    {
        CronSchedule.TryParse("* * * * *", out var schedule); // todo minuto
        var executed = false;
        var cts = new CancellationTokenSource();

        // Clock retorna um instante fixo — garante que o schedule bate
        var fixedTime = new DateTime(2026, 7, 23, 10, 0, 0, DateTimeKind.Utc);
        var callCount = 0;
        var watcher = new CronActionWatcher("proj", "action", schedule!,
            () => { executed = true; },
            () =>
            {
                // Na primeira chamada retorna 59.5s antes do próximo minuto para que o watcher
                // espere ~500ms e dispare imediatamente; nas chamadas seguintes retorna o mesmo tempo.
                callCount++;
                return callCount == 1
                    ? fixedTime.AddSeconds(-0.5)  // 500ms até o próximo "minuto"
                    : fixedTime;
            });

        watcher.Start();
        Thread.Sleep(TimeSpan.FromSeconds(3));
        watcher.Stop();

        Assert.True(executed);
    }

    [Fact]
    public void CronActionWatcher_NaoExecuta_QuandoHorarioNaoBate()
    {
        CronSchedule.TryParse("0 3 1 1 *", out var schedule); // 1º de janeiro às 03:00
        var executed = false;

        var fixedTime = new DateTime(2026, 7, 23, 10, 0, 0, DateTimeKind.Utc); // julho, não janeiro
        var callCount = 0;
        var watcher = new CronActionWatcher("proj", "action", schedule!,
            () => { executed = true; },
            () =>
            {
                callCount++;
                return callCount == 1 ? fixedTime.AddSeconds(-0.5) : fixedTime;
            });

        watcher.Start();
        Thread.Sleep(TimeSpan.FromSeconds(3));
        watcher.Stop();

        Assert.False(executed);
    }
}
