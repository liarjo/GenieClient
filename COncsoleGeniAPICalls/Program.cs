#nullable disable

using Microsoft.Extensions.Configuration;

class Program
{
    static async Task Main(string[] args)
    {
        while (true)
        {
            Console.WriteLine("=== Main Menu ===");
            Console.WriteLine("1. Call Genie API");
            Console.WriteLine("2. Call Azure AI Agent");
            Console.WriteLine("3. Exit");
            Console.Write("Please select an option (1-3): ");

            string? choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    await CallGenieAPI();
                    break;
                case "2":
                    await CallAzureAIAgent();
                    break;
                case "3":
                    Console.WriteLine("Exiting the application. Goodbye!");
                    return;
                default:
                    Console.WriteLine("Invalid choice. Please try again.");
                    break;
            }

            Console.WriteLine(); // Add a blank line for better readability
        }
    }
    static async Task CallAzureAIAgent()
    {
         // Load configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
        string PROJECT_CONNECTION_STRING = configuration["PROJECT_CONNECTION_STRING"] 
            ?? throw new ArgumentNullException("PROJECT_CONNECTION_STRING", "ConnectionString cannot be null in the configuration.");
        
        

        azureAIAgent myAgent = new(PROJECT_CONNECTION_STRING);

        // Subscribe to the OnStreamUpdate event
        myAgent.OnStreamUpdate += (update) =>
        {
            Console.Write($"{update}");
        };

        myAgent.OnNewImage += (image) =>
        {
            Console.WriteLine($"New image received: {image}");
        };
        while (true)
        {
            // Prompt the user for input
            Console.WriteLine("Please enter your prompt for the Azure AI Agent (type 'exit' to quit):");
            string myPrompt = Console.ReadLine();

            // Check if the user wants to exit
            if (string.Equals(myPrompt, "exit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Exiting Azure AI Agent interaction.");
                break;
            }

            // Validate the input
            if (string.IsNullOrWhiteSpace(myPrompt))
            {
                Console.WriteLine("Prompt cannot be null or empty. Please try again.");
                continue;
            }

            // Send the user's prompt to the Azure AI Agent
            await myAgent.SendMessageAsync(myPrompt);
            Console.WriteLine(); // Add a blank line for better readability
        }
        myAgent.CleanResources();
        
    }
    static async Task CallGenieAPI()
    {
        // Load configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // Retrieve required configuration values
        string spaceId = configuration["SpaceId"] 
            ?? throw new ArgumentNullException("SpaceId", "SpaceId cannot be null in the configuration.");
        string token = configuration["AuthToken"] 
            ?? throw new ArgumentNullException("AuthToken", "AuthToken cannot be null in the configuration.");
        string baseAddress = configuration["BaseAddress"] 
            ?? throw new ArgumentNullException("BaseAddress", "BaseAddress cannot be null in the configuration.");
        int pollingDelayMilliseconds = int.TryParse(configuration["PollingDelayMilliseconds"], out var delay) 
            ? delay 
            : 5000; // Default to 5000ms if not specified

        // Prompt the user for input
        Console.WriteLine("Please enter your prompt:");
        string myPrompt = Console.ReadLine() 
            ?? throw new ArgumentNullException("Prompt", "Prompt cannot be null or empty.");

        // Initialize the GenieClient with the provided configuration
        var genieClient = new GenieClient(baseAddress, spaceId, token);

        Console.WriteLine("Starting conversation...");
        var (messageId, conversationId) = await genieClient.StartConversationAsync(myPrompt);

        // Poll for message status until processing is complete
        string status;
        string attachmentsId;
        string query;
        string description;

        do
        {
            // Fetch the current status of the message
            (status, attachmentsId, query, description) = await genieClient.GetMessageStatusAsync(conversationId, messageId);

            // Print the current status information
            PrintGenieInformation(messageId, conversationId, status, attachmentsId, query, description, string.Empty);

            // Wait before polling again if the status is not "COMPLETED"
            if (status != "COMPLETED")
            {
                await Task.Delay(pollingDelayMilliseconds);
            }
        } while (status != "COMPLETED");

        Console.WriteLine("Message processing is completed.");

        // Retrieve and display the query results
        string queryResult = await genieClient.RetrieveQueryResultsAsync(conversationId, messageId, attachmentsId);
        PrintGenieInformation(messageId, conversationId, status, attachmentsId, query, description, queryResult);
    }

    /// <summary>
    /// Prints detailed information about the Genie conversation and query.
    /// </summary>
    /// <param name="messageId">The ID of the message.</param>
    /// <param name="conversationId">The ID of the conversation.</param>
    /// <param name="status">The current status of the message.</param>
    /// <param name="attachmentsId">The ID of the attachments (if any).</param>
    /// <param name="query">The generated query.</param>
    /// <param name="description">The description of the query.</param>
    /// <param name="queryResult">The result of the query.</param>
    public static void PrintGenieInformation(
        string messageId,
        string conversationId,
        string status,
        string attachmentsId,
        string query,
        string description,
        string queryResult)
    {
        Console.WriteLine("=== Genie Information ===");
        Console.WriteLine($"Message ID: {messageId}");
        Console.WriteLine($"Conversation ID: {conversationId}");
        Console.WriteLine($"Status: {status}");
        Console.WriteLine($"Attachments ID: {attachmentsId}");
        Console.WriteLine($"Query Description: {description}");
        Console.WriteLine($"Generated Query: {query}");
        Console.WriteLine($"Query Result: {queryResult}");
        Console.WriteLine("=========================");
        Console.WriteLine();
    }
}
