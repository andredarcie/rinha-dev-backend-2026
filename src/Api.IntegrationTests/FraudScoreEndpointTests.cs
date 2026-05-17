using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RinhaBackend;

namespace Api.IntegrationTests;

[Collection("Integration")]
public class FraudScoreEndpointTests(ApiFactory factory)
{
    // Low-risk request: all feature dimensions close to zero.
    // With 5 legit neighbors at [0,...,0], all 5 nearest are legit → score 0/5 = 0.0 → approved.
    [Fact]
    public async Task Approved_for_normal_transaction()
    {
        var request = NormalRequest();

        var response = await PostAsync(request);
        var result = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(result.Approved);
        Assert.Equal(0f, result.FraudScore);
    }

    // High-risk request: all feature dimensions close to one.
    // With 5 fraud neighbors at [1,...,1], all 5 nearest are fraud → score 5/5 = 1.0 → denied.
    [Fact]
    public async Task Denied_for_suspicious_transaction()
    {
        var request = SuspiciousRequest();

        var response = await PostAsync(request);
        var result = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(result.Approved);
        Assert.Equal(1f, result.FraudScore);
    }

    [Fact]
    public async Task Handles_missing_last_transaction()
    {
        var request = NormalRequest() with { LastTransaction = null };

        var response = await PostAsync(request);
        var result = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.InRange(result.FraudScore, 0f, 1f);
    }

    [Fact]
    public async Task Returns_400_for_invalid_json()
    {
        var content = new StringContent("{ not valid json", Encoding.UTF8, "application/json");
        var response = await factory.CreateClient().PostAsync("/fraud-score", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Returns_400_for_empty_body()
    {
        var content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
        var response = await factory.CreateClient().PostAsync("/fraud-score", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_request_returns_405()
    {
        var response = await factory.CreateClient().GetAsync("/fraud-score");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Response_contains_approved_and_fraud_score()
    {
        var response = await PostAsync(NormalRequest());

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("approved", out _));
        Assert.True(doc.RootElement.TryGetProperty("fraud_score", out _));
    }

    [Fact]
    public async Task Fraud_score_is_always_between_0_and_1()
    {
        var response = await PostAsync(NormalRequest());
        var result = await ReadAsync(response);

        Assert.InRange(result.FraudScore, 0f, 1f);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> PostAsync(FraudScoreRequest request) =>
        factory.CreateClient().PostAsJsonAsync("/fraud-score", request);

    private static async Task<FraudScoreResponse> ReadAsync(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<FraudScoreResponse>();
        return result ?? throw new InvalidOperationException("Null response body");
    }

    // Values that normalize to near [0,...,0]: small amount, known merchant, card present, close to home.
    private static FraudScoreRequest NormalRequest() => new(
        Id: "tx-normal",
        Transaction: new TransactionInfo(50m, 1, "2024-06-17T10:00:00Z"),
        Customer: new CustomerInfo(500m, 2, ["MERC-001"]),
        Merchant: new MerchantInfo("MERC-001", "5411", 50m),
        Terminal: new TerminalInfo(false, true, 2m),
        LastTransaction: null
    );

    // Values that normalize to near [1,...,1]: max amount, unknown merchant, online, far from home.
    private static FraudScoreRequest SuspiciousRequest() => new(
        Id: "tx-fraud",
        Transaction: new TransactionInfo(9999m, 12, "2024-06-17T10:00:00Z"),
        Customer: new CustomerInfo(50m, 20, []),
        Merchant: new MerchantInfo("MERC-UNKNOWN", "5912", 9999m),
        Terminal: new TerminalInfo(true, false, 999m),
        LastTransaction: new LastTransactionInfo("2024-06-17T09:58:00Z", 999m)
    );
}
