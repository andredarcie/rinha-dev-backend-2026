using RinhaBackend;
using Xunit;

namespace Api.Tests;

public class ModelsTests
{
    // Cobre FraudScoreRequest.get_Id que estava em 0%
    [Fact]
    public void FraudScoreRequest_Id_is_accessible()
    {
        var req = new FraudScoreRequest(
            Id: "tx-abc",
            Transaction: new TransactionInfo(100m, 1, "2024-06-17T10:00:00Z"),
            Customer: new CustomerInfo(100m, 1, []),
            Merchant: new MerchantInfo("M1", "5411", 100m),
            Terminal: new TerminalInfo(false, true, 0m),
            LastTransaction: null);

        Assert.Equal("tx-abc", req.Id);
    }

    [Fact]
    public void FraudScoreResponse_properties_are_accessible()
    {
        var res = new FraudScoreResponse(Approved: true, FraudScore: 0.4f);

        Assert.True(res.Approved);
        Assert.Equal(0.4f, res.FraudScore);
    }
}
