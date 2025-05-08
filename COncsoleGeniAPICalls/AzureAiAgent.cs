#nullable disable

using System.ClientModel;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

public class azureAIAgent
{
    // Define the delegate for the event
    public delegate void StreamUpdateHandler(string update);
    // Define the event
    public event StreamUpdateHandler OnStreamUpdate;
    public delegate void newImageHandler(string imagePath);
    public event newImageHandler OnNewImage;
    private string _connectionString;
    private AgentsClient _client;
    private Agent _agent;
    private AgentThread _thread;
    private IConfiguration _configuration;
    private string _genieInstructions;
    private FunctionToolDefinition _askGenieTool;

    #region Constructor and Initialization
    private void printRed(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }
    private void printYellow(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }
    private string GetConfigurationValue(string key)
    {
        return _configuration[key] ?? throw new ArgumentNullException(key, $"{key} cannot be null in the configuration.");
    }
    private async Task<string> LoadInstructionsAsync(string fileName)
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, fileName);
        return await File.ReadAllTextAsync(filePath);
    }
    public azureAIAgent(string connectionString)
    {
        _configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        _connectionString = connectionString;
        _client = new AgentsClient(_connectionString, new DefaultAzureCredential());
        InitializeAgentAsync().GetAwaiter().GetResult();
    }

    private async Task InitializeAgentAsync()
    {
        // Load instructions from files
        string agentInstructions = await LoadInstructionsAsync("agentInstructions.txt");
        _genieInstructions = await LoadInstructionsAsync("askGenieInstructions.txt");

        // Initialize tools
        InitializeAskGenieTool();

        // Create agent
        _agent = await CreateAgentAsync(agentInstructions);

        // Create thread
        _thread = await _client.CreateThreadAsync();
    }


    private async Task<Agent> CreateAgentAsync(string instructions)
    {
        Response<Agent> response = await _client.CreateAgentAsync(
            model: _configuration["AgenModelName"] ?? throw new ArgumentNullException("AgenModelName", "Model cannot be null in the configuration."),
            name: _configuration["AgentName"] ?? "myAgent",
            instructions: instructions,
            tools: [ _askGenieTool, new CodeInterpreterToolDefinition()]
        );

        return response.Value;
    }

    private void InitializeAskGenieTool()
    {
        _askGenieTool = new FunctionToolDefinition(
            name: "AskGenie",
            description: _genieInstructions,
            parameters: BinaryData.FromObjectAsJson(
                new
                {
                    Type = "object",
                    Properties = new
                    {
                        geiniePrompt = new
                        {
                            Type = "string",
                            Description = "Question to be asked to Genie.",
                        },
                    },
                    Required = new[] { "geiniePrompt" },
                },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    #endregion

    #region Main Functionality

    public async Task SendMessageAsync(string prompt)
    {
        //Create a message in the thread
        ThreadMessage message = await _client.CreateMessageAsync(_thread.Id, MessageRole.User, prompt);

        List<ToolOutput> toolOutputs = new();
        ThreadRun streamRun = null;

        // Create a run the thread
        AsyncCollectionResult<StreamingUpdate> stream = _client.CreateRunStreamingAsync(_thread.Id, _agent.Id);

        //event
        StringBuilder streamContent = new();
        
        do
        {
            toolOutputs.Clear();
            await foreach (StreamingUpdate update in stream)
            {
                 
                switch (update)
                {
                    case RequiredActionUpdate actionUpdate:
                        printYellow($"Tool call ID: {actionUpdate.ToolCallId}");
                        printYellow($"Function name: {actionUpdate.FunctionName}");
                        printYellow($"Function arguments: {actionUpdate.FunctionArguments}");
                        toolOutputs.Add(GetResolvedToolOutput(actionUpdate.FunctionName, actionUpdate.ToolCallId, actionUpdate.FunctionArguments));
                        streamRun = actionUpdate.Value;
                        break;

                    case MessageContentUpdate contentUpdate:
                        //Check Image first
                        if (contentUpdate.ImageFileId != null)
                        {
                            var imageInfo = await _client.GetFileAsync(contentUpdate.ImageFileId);
                            BinaryData imageBytes = await _client.GetFileContentAsync(contentUpdate.ImageFileId);
                            using FileStream myImagestream = File.OpenWrite($"{imageInfo.Value.Filename}");
                            imageBytes.ToStream().CopyTo(myImagestream);
                            //printYellow($"Image file ID: {contentUpdate.ImageFileId}");
                            //printYellow($"<image: {imageInfo.Value.Filename}");
                            OnNewImage?.Invoke($"![{imageInfo.Value.Filename}!]({imageInfo.Value.Filename})");
                        }
                        
                        // Append content to the StringBuilder
                        streamContent.Append(contentUpdate.Text);
                        // Trigger the OnStreamUpdate event
                        OnStreamUpdate?.Invoke(contentUpdate.Text);
                        //Agent Response stream
                        //printYellow( contentUpdate.Text);
        
                        break;

                    case { UpdateKind: StreamingUpdateReason.RunCompleted }:
                        printYellow("\n--- Run completed! ---");
                        break;

                    case { UpdateKind: StreamingUpdateReason.RunCreated }:
                        printYellow("--- Run started! ---");
                        break;
                }
            }

            if (toolOutputs.Count > 0)
            {
                printYellow($"Tool output: {toolOutputs[0].Output}");
                printYellow($"Tool call ID: {toolOutputs[0].ToolCallId}");
                stream = _client.SubmitToolOutputsToStreamAsync(streamRun, toolOutputs);
            }
        } while (toolOutputs.Count > 0);

       // printMessagesInfo();
    
    }

    private ToolOutput GetResolvedToolOutput(string functionName, string toolCallId, string functionArguments)
    {
        using JsonDocument argumentsJson = JsonDocument.Parse(functionArguments);

        if (functionName == _askGenieTool.Name)
        {
            string geniePrompt = argumentsJson.RootElement.GetProperty("geiniePrompt").GetString();
            return new ToolOutput(toolCallId, AskGenie(geniePrompt));
        }
        return null;
    }

    private string AskGenie(string geniePrompt)
    {
        // Retrieve configuration values
        string spaceId = GetConfigurationValue("SpaceId");
        string token = GetConfigurationValue("AuthToken");
        string baseAddress = GetConfigurationValue("BaseAddress");
        int pollingDelay = int.TryParse(_configuration["PollingDelayMilliseconds"], out var delay) ? delay : 5000;

        // Initialize GenieClient
        var genieClient = new GenieClient(baseAddress, spaceId, token);

        printRed("Genie Starting conversation...");
        var (messageId, conversationId) = genieClient.StartConversationAsync(geniePrompt).GetAwaiter().GetResult();

        // Poll for message status
        string status, attachmentsId, query, description;
        do
        {
            (status, attachmentsId, query, description) = genieClient.GetMessageStatusAsync(conversationId, messageId).GetAwaiter().GetResult();

            if (status != "COMPLETED")
            {
                Task.Delay(pollingDelay).Wait();
            }
        } while (status != "COMPLETED");

        printRed("Genei Message processing is completed.");

        // Retrieve query results
        return genieClient.RetrieveQueryResultsAsync(conversationId, messageId, attachmentsId).GetAwaiter().GetResult();
    }

    public void CleanResources()
    {
        _client.DeleteThreadAsync(_thread.Id).GetAwaiter().GetResult();
        printYellow("Thread deleted successfully.");
        _client.DeleteAgentAsync(_agent.Id).GetAwaiter().GetResult();
        printYellow("Agent deleted successfully.");
        // Delete all .png images in the local disk
        string[] pngFiles = Directory.GetFiles(AppContext.BaseDirectory, "*.png");
        foreach (string file in pngFiles)
        {
            try
            {
                File.Delete(file);
                printYellow($"Deleted image file: {file}");
            }
            catch (Exception ex)
            {
                printRed($"Failed to delete file {file}: {ex.Message}");
            }
        }
        
    }
    #endregion

}
