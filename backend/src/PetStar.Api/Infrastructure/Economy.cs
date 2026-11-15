using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Rules;

namespace PetStar.Api.Infrastructure;

// Table/column names concatenated below are private compile-time constants chosen by this class (never user input);
// every value is passed as a SQL parameter.
#pragma warning disable EF1002, EF1003

/// <summary>
/// The only code path that changes balances. Every change:
///  1. inserts a ledger row first (unique on membership+asset+reason+refId → a business reference can never
///     apply twice, even under retries or concurrent requests);
///  2. applies the delta with a single atomic UPDATE guarded by "balance + delta >= 0" (row lock, no lost update,
///     no negative balances);
///  3. records balance_after.
/// Must be called inside a database transaction owned by the caller.
/// </summary>
public sealed class EconomyService(PetStarDb db)
{
    public sealed record Change(bool Applied, long BalanceAfter);

    public Task<Change> Currency(long membershipId, int serverId, string currency, long delta, string reason, string refId,
        long? actor = null, bool tolerateDuplicate = false)
    {
        if (!Rules.Currency.All.Contains(currency)) throw ApiException.BadRequest("unknown_currency");
        return Apply("economy_wallets", "currency", "balance", "economy_ledger", currency, membershipId, serverId, delta,
            reason, refId, actor, tolerateDuplicate, "insufficient_" + currency.ToLowerInvariant());
    }

    public Task<Change> Item(long membershipId, int serverId, string itemId, long delta, string reason, string refId,
        bool tolerateDuplicate = false) =>
        Apply("inventory_items", "item_id", "quantity", "item_ledger", itemId, membershipId, serverId, delta, reason, refId,
            null, tolerateDuplicate, "insufficient_items");

    public Task<Change> Packs(long membershipId, int serverId, string packTypeId, int delta, string reason, string refId,
        bool tolerateDuplicate = false) =>
        Apply("pack_inventory", "pack_type_id", "quantity", "item_ledger", packTypeId, membershipId, serverId, delta, reason,
            refId, null, tolerateDuplicate, "no_pack_available", ledgerAssetPrefix: "pack:");

    private async Task<Change> Apply(string table, string keyCol, string balCol, string ledger, string asset,
        long membershipId, int serverId, long delta, string reason, string refId, long? actor, bool tolerateDuplicate,
        string insufficientCode, string ledgerAssetPrefix = "")
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Economy changes require a transaction");
        if (delta == 0) throw new ArgumentException("delta must be non-zero");
        string ledgerAsset = ledgerAssetPrefix + asset;
        string ledgerKey = ledger == "economy_ledger" ? "currency" : "item_id";

        // 1. ledger row first: duplicates are detected before any balance moves
        bool money = ledger == "economy_ledger";
        string sql = "INSERT INTO " + ledger + " (membership_id, server_id, " + ledgerKey + ", delta, balance_after, reason, ref_id" +
                     (money ? ", actor_account_id" : "") + ", at) VALUES ({0}, {1}, {2}, {3}, 0, {4}, {5}" +
                     (money ? ", {6}" : "") + ", now()) ON CONFLICT DO NOTHING RETURNING id AS \"Value\"";
        var args = money
            ? new object[] { membershipId, serverId, ledgerAsset, delta, reason, refId, new Npgsql.NpgsqlParameter { Value = (object?)actor ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint } }
            : new object[] { membershipId, serverId, ledgerAsset, delta, reason, refId };
        var inserted = await db.Database.SqlQueryRaw<long>(sql, args).ToListAsync();
        if (inserted.Count == 0)
        {
            if (tolerateDuplicate) return new Change(false, await Balance(table, keyCol, balCol, membershipId, asset));
            throw ApiException.Conflict("duplicate_transaction");
        }

        // 2. atomic, non-negative balance change
        await db.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {table} (membership_id, {keyCol}, {balCol}" + (table == "economy_wallets" ? ", updated_at" : "") +
            ") VALUES ({0}, {1}, 0" + (table == "economy_wallets" ? ", now()" : "") + ") ON CONFLICT DO NOTHING",
            membershipId, asset);
        var bal = await db.Database.SqlQueryRaw<long>(
            $"UPDATE {table} SET {balCol} = {balCol} + {{0}}" + (table == "economy_wallets" ? ", updated_at = now()" : "") +
            $" WHERE membership_id = {{1}} AND {keyCol} = {{2}} AND {balCol} + {{0}} >= 0 RETURNING {balCol}::bigint AS \"Value\"",
            delta, membershipId, asset).ToListAsync();
        if (bal.Count == 0) throw ApiException.Conflict(insufficientCode);

        // 3. balance snapshot on the ledger row
        await db.Database.ExecuteSqlRawAsync($"UPDATE {ledger} SET balance_after = {{0}} WHERE id = {{1}}", bal[0], inserted[0]);
        return new Change(true, bal[0]);
    }

    private async Task<long> Balance(string table, string keyCol, string balCol, long membershipId, string asset)
    {
        var r = await db.Database.SqlQueryRaw<long>(
            $"SELECT {balCol}::bigint AS \"Value\" FROM {table} WHERE membership_id = {{0}} AND {keyCol} = {{1}}",
            membershipId, asset).ToListAsync();
        return r.Count > 0 ? r[0] : 0;
    }

    public async Task<Dictionary<string, long>> Balances(long membershipId)
    {
        var rows = await db.Wallets.AsNoTracking().Where(w => w.MembershipId == membershipId).ToListAsync();
        return Rules.Currency.All.ToDictionary(c => c, c => rows.FirstOrDefault(r => r.Currency == c)?.Balance ?? 0);
    }
}
