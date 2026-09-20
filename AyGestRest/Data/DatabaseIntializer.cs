// Helpers/DatabaseInitializer.cs  (podes criar esta classe)
using AyGestRest.Data;
using Microsoft.EntityFrameworkCore;

namespace AyGestRest.Helpers
{
    public static class DatabaseInitializer
    {
        public static void Initialize()
        {
            using var context = new AyGestRestContext();
            context.Database.EnsureCreated();  // Cria a BD e tabelas se não existirem
        }
    }
}