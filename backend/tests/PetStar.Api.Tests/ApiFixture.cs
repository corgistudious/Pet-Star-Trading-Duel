using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PetStar.Api.Data;
using PetStar.Contracts;

namespace PetStar.Api.Tests;

/// <summary>Boots the real API against a throw-away PostgreSQL database (created by migrations, dropped after).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly string BaseConn = Environment.GetEnvironmentVariable("PETSTAR_TEST_PG")
        ?? "Host=localhost;Port=5432;Username=petstar;Password=petstar_dev_only";
    public readonly string DbName = "petstar_test_" + Guid.NewGuid().ToString("N")[..12];
    public string Conn => BaseConn + ";Database=" + DbName;

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder b)
    {
        b.UseEnvironment("Testing");
        b.UseSetting("ConnectionStrings:Postgres", Conn);
        b.UseSetting("ConnectionStrings:Redis", "");
        b.UseSetting("Iap:AllowFakeStore", "true");
        b.UseSetting("Seed:SyncPackTypes", "true");
        b.UseSetting("RateLimits:AuthPerMinute", "100000");
        b.UseSetting("RateLimits:UserPerMinute", "100000");
    }

    public Task InitializeAsync()
    {
        PetStar.Api.Features.MarketMaintenanceService.Interval = TimeSpan.FromHours(1); // tests drive maintenance explicitly
        _ = Server;
        return Task.CompletedTask;
    }

    private WebApplicationFactory<Program>? _iap;
    /// <summary>Same database, with the (disabled-by-default) store verification enabled, for IAP code-path tests.</summary>
    public HttpClient IapClient() => (_iap ??= WithWebHostBuilder(b => b.UseSetting("Iap:Enabled", "true"))).CreateClient();

    public new async Task DisposeAsync()
    {
        Dispose();
        NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(BaseConn + ";Database=postgres");
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {DbName} WITH (FORCE)", c);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task Sql(string sql, params object[] args)
    {
        await using var c = new NpgsqlConnection(Conn);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        for (int i = 0; i < args.Length; i++) cmd.Parameters.AddWithValue("p" + i, args[i]);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<T?> Scalar<T>(string sql, params object[] args)
    {
        await using var c = new NpgsqlConnection(Conn);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        for (int i = 0; i < args.Length; i++) cmd.Parameters.AddWithValue("p" + i, args[i]);
        var r = await cmd.ExecuteScalarAsync();
        return r == null || r is DBNull ? default : (T)Convert.ChangeType(r, typeof(T));
    }

    public PetStarDb NewDb() => Services.CreateScope().ServiceProvider.GetRequiredService<PetStarDb>();

    public async Task<Player> NewPlayer(string? prefix = null, int server = 1, HttpClient? client = null)
    {
        var p = new Player(client ?? CreateClient());
        var email = $"{prefix ?? "p"}{Guid.NewGuid():N}@test.local";
        await p.Register(email, "Passw0rd1", "Tester" + Guid.NewGuid().ToString("N")[..6]);
        await p.Enter(server);
        return p;
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;

public sealed class Player(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public HttpClient Http { get; } = http;
    public string Email { get; private set; } = "";
    public string Password { get; private set; } = "";
    public long Uid { get; private set; }
    public string Refresh { get; private set; } = "";

    public void UseToken(string t) => Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", t);

    public async Task<AuthResponse> Register(string email, string pw, string name)
    {
        Email = email; Password = pw;
        var r = await Http.PostAsJsonAsync("/api/auth/register", new RegisterRequest { Email = email, Password = pw, DisplayName = name }, Json);
        await Ok(r);
        var a = (await r.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        Uid = a.Uid; Refresh = a.RefreshToken; UseToken(a.AccessToken);
        return a;
    }

    public async Task<AuthResponse> Enter(int server)
    {
        var r = await Http.PostAsJsonAsync($"/api/servers/{server}/enter", new EnterServerRequest(), Json);
        await Ok(r);
        var a = (await r.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        UseToken(a.AccessToken);
        return a;
    }

    public async Task<T> Get<T>(string url)
    {
        var r = await Http.GetAsync(url);
        await Ok(r);
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public Task<HttpResponseMessage> Post(string url, object? body, string? idemKey = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { }, options: Json) };
        if (idemKey != null) req.Headers.Add("Idempotency-Key", idemKey);
        return Http.SendAsync(req);
    }

    public async Task<T> PostOk<T>(string url, object? body, string? idemKey = null)
    {
        var r = await Post(url, body, idemKey);
        await Ok(r);
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public static async Task Ok(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode) throw new Xunit.Sdk.XunitException($"{(int)r.StatusCode} {r.RequestMessage?.RequestUri}: {await r.Content.ReadAsStringAsync()}");
    }

    public static async Task<string> Code(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;

    public Task<PlayerDto> Me() => Get<PlayerDto>("/api/player");
    public static string Key() => "k" + Guid.NewGuid().ToString("N");
}
