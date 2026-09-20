using AyGestRest.Data;
using Microsoft.EntityFrameworkCore;

public static class DbUpdater
{
    public static void AtualizarDb(AyGestRestContext context)
    {
        // Isso aplica todas as migrations que ainda não foram aplicadas
        context.Database.Migrate();
    }
}
