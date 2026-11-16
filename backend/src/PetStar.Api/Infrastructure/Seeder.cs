using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Features;

namespace PetStar.Api.Infrastructure;

/// <summary>Applies migrations (additive only — never drops player data) and upserts design data + PS-1.</summary>
public static class Seeder
{
    public static async Task Run(IServiceProvider sp, IConfiguration cfg, ILogger log)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetStarDb>();
        var data = scope.ServiceProvider.GetRequiredService<GameData>();
        if (cfg.GetValue("Database:AutoMigrate", true)) await db.Database.MigrateAsync();

        if (!await db.Servers.AnyAsync())
        {
            db.Servers.Add(new GameServer { Id = 1, Code = "PS-1", Name = "Global", Status = ServerStatus.Online, RegistrationOpen = true, CreatedAt = DateTimeOffset.UtcNow, OpenedAt = DateTimeOffset.UtcNow });
            log.LogInformation("Created server PS-1 Global");
        }

        var cat = data.Catalog;
        var fams = await db.PetFamilies.ToDictionaryAsync(f => f.Id);
        foreach (var f in cat.Families)
        {
            if (!fams.TryGetValue(f.Id, out var row)) db.PetFamilies.Add(row = new PetFamilyRow { Id = f.Id });
            row.Element = f.Element; row.Role = f.Role; row.Stages = f.Stages;
        }
        await db.SaveChangesAsync();
        var forms = await db.PetForms.ToDictionaryAsync(f => f.Id);
        foreach (var f in cat.Forms.Values)
        {
            if (!forms.TryGetValue(f.Id, out var row)) db.PetForms.Add(row = new PetFormRow { Id = f.Id });
            row.FamilyId = f.FamilyId; row.Stage = f.Stage; row.Name = f.Name;
            row.DataJson = JsonSerializer.Serialize(f, IdempotencyService.Json);
        }
        var skills = await db.PetSkills.ToDictionaryAsync(s => s.Id);
        foreach (var s in cat.Skills.Values)
        {
            if (!skills.TryGetValue(s.Id, out var row)) db.PetSkills.Add(row = new PetSkillRow { Id = s.Id });
            row.Kind = s.Kind; row.DataJson = JsonSerializer.Serialize(s, IdempotencyService.Json);
        }

        // Pack configs: a changed seed creates a new version. Odds changes in production go through the
        // admin approval flow; the seed sync only runs when Seed:SyncPackTypes is true (default in dev).
        bool syncPacks = cfg.GetValue("Seed:SyncPackTypes", true);
        var packs = await db.PackTypes.ToDictionaryAsync(p => p.Id);
        foreach (var p in cat.PackTypes)
        {
            var json = JsonSerializer.Serialize(p, IdempotencyService.Json);
            if (!packs.TryGetValue(p.Id, out var row))
                db.PackTypes.Add(new PackTypeRow { Id = p.Id, Version = 1, Enabled = p.Enabled, DataJson = json, UpdatedAt = DateTimeOffset.UtcNow });
            else if (syncPacks && Normalize(row.DataJson) != Normalize(json))
            {
                row.Version++; row.DataJson = json; row.Enabled = p.Enabled; row.UpdatedAt = DateTimeOffset.UtcNow;
                log.LogInformation("Pack {Id} updated to version {V}", p.Id, row.Version);
            }
        }
        await db.SaveChangesAsync();
        // immutable odds/price history: every current version is recorded once (openings reference the version)
        foreach (var row in await db.PackTypes.AsNoTracking().ToListAsync())
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO pack_type_versions (pack_type_id, version, enabled, data_json, reason, created_at) VALUES ({0}, {1}, {2}, {3}::jsonb, 'seed', now()) " +
                "ON CONFLICT (pack_type_id, version) DO NOTHING", row.Id, row.Version, row.Enabled, row.DataJson);

        // Bootstrap owner role from configuration (e.g. Bootstrap__OwnerEmail env var)
        var ownerEmail = cfg["Bootstrap:OwnerEmail"];
        if (!string.IsNullOrWhiteSpace(ownerEmail))
        {
            var acc = await db.Accounts.Include(a => a.Roles).FirstOrDefaultAsync(a => a.EmailNormalized == ownerEmail.Trim().ToUpperInvariant());
            if (acc != null && acc.Roles.All(r => r.Role != "owner"))
            {
                db.AccountRoles.Add(new AccountRole { AccountId = acc.Id, Role = "owner", GrantedAt = DateTimeOffset.UtcNow });
                db.AuditLogs.Add(new AuditLog { Action = "rbac.bootstrap_owner", TargetType = "account", TargetId = acc.Id.ToString(), At = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
            }
        }
    }

    private static string Normalize(string json) => JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);
}
