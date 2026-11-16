using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PetStar.Api.Data;

namespace PetStar.Api.Tests;

/// <summary>The card-system migration is additive, defaults existing cards to 1★ / +0, and can be rolled back and re-applied.</summary>
public class MigrationTests
{
    [Fact]
    public async Task Card_system_migration_applies_rolls_back_and_reapplies_on_a_database_with_old_rows()
    {
        var name = "petstar_mig_" + Guid.NewGuid().ToString("N")[..10];
        var conn = ApiFactory.BaseConn + ";Database=" + name;
        var opts = new DbContextOptionsBuilder<PetStarDb>().UseNpgsql(conn).UseSnakeCaseNamingConvention().Options;
        try
        {
            await using (var db = new PetStarDb(opts))
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync("20261116002310_WebPush");                  // schema before the expansion
                await db.Database.ExecuteSqlRawAsync("CREATE TABLE _probe (id int)");   // stands in for existing data
                await migrator.MigrateAsync();                                           // expansion
                var defaults = await db.Database.SqlQueryRaw<string>(
                    "SELECT column_name || '=' || column_default AS \"Value\" FROM information_schema.columns WHERE table_name='card_instances' AND column_name IN ('stars','enhance_level','breakthrough','awakened') ORDER BY column_name").ToListAsync();
                Assert.Equal(new[] { "awakened=false", "breakthrough=0", "enhance_level=0", "stars=1" }, defaults);
                foreach (var t in new[] { "membership_vip", "pack_type_versions", "pack_purchases", "limited_events", "star_invitations", "card_engravings" })
                    Assert.Equal(1, await db.Database.SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_name='{t}'").SingleAsync());
                await migrator.MigrateAsync("20261116002310_WebPush");                  // rollback plan (before any new data exists)
                Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_name='star_invitations'").SingleAsync());
                Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_name='_probe'").SingleAsync());
                await migrator.MigrateAsync();                                           // re-apply
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var c = new NpgsqlConnection(ApiFactory.BaseConn + ";Database=postgres");
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {name} WITH (FORCE)", c);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
