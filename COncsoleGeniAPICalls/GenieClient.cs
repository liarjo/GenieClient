using System.Text;
using System.Text.Json;

/// <summary>
/// A client for interacting with the Genie API.
/// </summary>
public class GenieClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseAddress;
    private readonly string _spaceId;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenieClient"/> class.
    /// </summary>
    /// <param name="baseAddress">The base address of the Genie API.</param>
    /// <param name="spaceId">The space ID for the Genie API.</param>
    /// <param name="authToken">The authentication token for the Genie API.</param>
    public GenieClient(string baseAddress, string spaceId, string authToken)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseAddress) };
        _httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);

        _baseAddress = baseAddress;
        _spaceId = spaceId;
    }

    /// <summary>
    /// Starts a new conversation with the Genie API.
    /// </summary>
    /// <param name="content">The content of the initial message.</param>
    /// <returns>A tuple containing the message ID and conversation ID.</returns>
    public async Task<(string messageId, string conversationId)> StartConversationAsync(string content)
    {
        // Prepare the payload
        var payload = new { content };
        string jsonPayload = JsonSerializer.Serialize(payload);
        var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        // Send the POST request
        HttpResponseMessage response = await _httpClient.PostAsync(
            $"/api/2.0/genie/spaces/{_spaceId}/start-conversation", httpContent);
        response.EnsureSuccessStatusCode();

        // Parse the response
        string responseContent = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(responseContent);
        JsonElement root = document.RootElement;

        string messageId = root.GetProperty("message_id").GetString() 
            ?? throw new InvalidOperationException("message_id is null");
        string conversationId = root.GetProperty("conversation_id").GetString() 
            ?? throw new InvalidOperationException("conversation_id is null");

        return (messageId, conversationId);
    }

    /// <summary>
    /// Retrieves the status of a specific message in a conversation.
    /// </summary>
    /// <param name="conversationId">The ID of the conversation.</param>
    /// <param name="messageId">The ID of the message.</param>
    /// <returns>A tuple containing the status, attachments ID, query, and description.</returns>
    public async Task<(string status, string attachmentsId, string query, string description)> GetMessageStatusAsync(
        string conversationId, string messageId)
    {
        // Construct the endpoint URL
        string endpoint = $"/api/2.0/genie/spaces/{_spaceId}/conversations/{conversationId}/messages/{messageId}";

        // Send the GET request
        HttpResponseMessage response = await _httpClient.GetAsync(endpoint);
        response.EnsureSuccessStatusCode();

        // Parse the response
        string responseContent = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(responseContent);
        JsonElement root = document.RootElement;

        string status = root.GetProperty("status").GetString() ?? string.Empty;
        string attachmentsId = string.Empty;
        string query = string.Empty;
        string description = string.Empty;

        // Extract attachments and query details if available
        if (root.TryGetProperty("attachments", out JsonElement attachmentsElement) && 
            attachmentsElement.ValueKind == JsonValueKind.Array && 
            attachmentsElement.GetArrayLength() > 0)
        {
            JsonElement firstAttachment = attachmentsElement[0];
            attachmentsId = firstAttachment.GetProperty("attachment_id").GetString() ?? string.Empty;

            if (!string.IsNullOrEmpty(attachmentsId) && 
                firstAttachment.TryGetProperty("query", out JsonElement queryElement))
            {
                query = queryElement.GetProperty("query").GetString() ?? string.Empty;
                description = queryElement.GetProperty("description").GetString() ?? string.Empty;
            }
        }

        return (status, attachmentsId, query, description);
    }

    /// <summary>
    /// Retrieves the query results for a specific message and attachment.
    /// </summary>
    /// <param name="conversationId">The ID of the conversation.</param>
    /// <param name="messageId">The ID of the message.</param>
    /// <param name="attachmentId">The ID of the attachment.</param>
    /// <returns>The query result as a string.</returns>
    public async Task<string> RetrieveQueryResultsAsync(string conversationId, string messageId, string attachmentId)
    {
        // Construct the endpoint URL
        string endpoint = $"/api/2.0/genie/spaces/{_spaceId}/conversations/{conversationId}/messages/{messageId}/query-result/{attachmentId}";

        // Send the GET request
        HttpResponseMessage response = await _httpClient.GetAsync(endpoint);
        response.EnsureSuccessStatusCode();

        // Return the query result
        return await response.Content.ReadAsStringAsync();
    }
}