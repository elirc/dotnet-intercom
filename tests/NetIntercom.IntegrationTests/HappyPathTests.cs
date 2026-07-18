using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace NetIntercom.IntegrationTests;

/// <summary>
/// End-to-end happy path over the real HTTP surface and a real PostgreSQL container:
/// agent logs in → visitor starts a session and writes → conversation is routed with an SLA
/// timer → agent sees it in the inbox and replies → SLA is met → visitor receives the reply.
/// </summary>
public class HappyPathTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ApiFactory _factory;

    public HappyPathTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task VisitorToAgentConversationFlow()
    {
        var client = _factory.CreateClient();

        // --- agent login ---
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "admin@netintercom.local",
            password = "Passw0rd!"
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await Read<JsonElement>(loginResponse);
        var agentToken = login.GetProperty("accessToken").GetString()!;
        login.GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty();

        // --- visitor session (anonymous cookie identity) ---
        var sessionResponse = await client.PostAsJsonAsync("/api/v1/widget/session", new
        {
            anonymousId = $"anon-{Guid.NewGuid():N}",
            name = "Pat Visitor",
            email = "pat@example.com"
        });
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await Read<JsonElement>(sessionResponse);
        var visitorToken = session.GetProperty("visitorToken").GetString()!;

        var visitorClient = _factory.CreateClient();
        visitorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", visitorToken);

        // --- visitor writes; conversation is created, routed and gets an SLA timer ---
        var sendResponse = await visitorClient.PostAsJsonAsync("/api/v1/widget/messages", new
        {
            body = "Hello, I need help with my billing invoice",
            sourceUrl = "https://example.com/pricing"
        });
        sendResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var sent = await Read<JsonElement>(sendResponse);
        var conversationId = sent.GetProperty("conversation").GetProperty("id").GetGuid();
        sent.GetProperty("conversation").GetProperty("teamName").GetString().Should().NotBeNull();
        sent.GetProperty("conversation").GetProperty("activeSlaTimer").ValueKind.Should().Be(JsonValueKind.Object);

        // --- agent sees it in the inbox ---
        var agentClient = _factory.CreateClient();
        agentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentToken);

        var inbox = await Read<JsonElement>(await agentClient.GetAsync("/api/v1/conversations?state=Open"));
        inbox.GetProperty("items").EnumerateArray()
            .Any(c => c.GetProperty("id").GetGuid() == conversationId)
            .Should().BeTrue("the new conversation must appear in the open inbox");

        // --- agent replies; first response SLA is met ---
        var replyResponse = await agentClient.PostAsJsonAsync($"/api/v1/conversations/{conversationId}/messages", new
        {
            body = "Hi Pat! Happy to help with that invoice.",
            isInternalNote = false
        });
        replyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var conversation = await Read<JsonElement>(await agentClient.GetAsync($"/api/v1/conversations/{conversationId}"));
        conversation.GetProperty("firstAgentReplyAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        conversation.GetProperty("activeSlaTimer").ValueKind.Should().Be(JsonValueKind.Null);
        conversation.GetProperty("hasBreachedSla").GetBoolean().Should().BeFalse();

        // --- internal note stays hidden from the visitor ---
        await agentClient.PostAsJsonAsync($"/api/v1/conversations/{conversationId}/messages", new
        {
            body = "note: VIP customer",
            isInternalNote = true
        });

        var visitorMessages = await Read<JsonElement>(
            await visitorClient.GetAsync($"/api/v1/widget/conversations/{conversationId}/messages"));
        var bodies = visitorMessages.EnumerateArray().Select(m => m.GetProperty("body").GetString()).ToList();
        bodies.Should().Contain("Hi Pat! Happy to help with that invoice.");
        bodies.Should().NotContain("note: VIP customer");

        // --- visitor history persists across widget reloads (same anonymousId ⇒ same conversation) ---
        var anonymousId = session.GetProperty("visitor").GetProperty("anonymousId").GetString();
        var resumed = await Read<JsonElement>(await client.PostAsJsonAsync("/api/v1/widget/session", new { anonymousId }));
        resumed.GetProperty("conversation").GetProperty("id").GetGuid().Should().Be(conversationId);
        resumed.GetProperty("messages").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task AgentEndpoints_RejectAnonymousAndVisitorCallers()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/api/v1/conversations")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var session = await Read<JsonElement>(await client.PostAsJsonAsync("/api/v1/widget/session",
            new { anonymousId = $"anon-{Guid.NewGuid():N}" }));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.GetProperty("visitorToken").GetString());
        (await client.GetAsync("/api/v1/conversations")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Healthz_IsHealthy()
    {
        var response = await _factory.CreateClient().GetAsync("/healthz");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(content, Json)!;
    }
}
