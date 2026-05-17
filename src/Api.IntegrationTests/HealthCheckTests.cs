using System.Net;

namespace Api.IntegrationTests;

[Collection("Integration")]
public class HealthCheckTests(ApiFactory factory)
{
    [Fact]
    public async Task Ready_returns_200()
    {
        var response = await factory.CreateClient().GetAsync("/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
