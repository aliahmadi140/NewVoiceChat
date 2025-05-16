using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NewVoiceChat.Server.Services;

public class JanusService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<JanusService> _logger;
    private readonly string _janusUrl;
    private string? _sessionId;
    private string? _handleId;

    public JanusService(IConfiguration configuration, ILogger<JanusService> logger)
    {
        _logger = logger;
        _janusUrl = configuration["Janus:Url"] ?? "http://localhost:8088/janus";
        _httpClient = new HttpClient();
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Create Janus session
            var response = await _httpClient.PostAsJsonAsync($"{_janusUrl}", new
            {
                janus = "create",
                transaction = Guid.NewGuid().ToString()
            });

            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            _sessionId = result.GetProperty("data").GetProperty("id").GetInt64().ToString();
            _logger.LogInformation("Created Janus session: {SessionId}", _sessionId);

            // Attach to AudioBridge plugin
            response = await _httpClient.PostAsJsonAsync($"{_janusUrl}/{_sessionId}", new
            {
                janus = "attach",
                plugin = "janus.plugin.audiobridge",
                transaction = Guid.NewGuid().ToString()
            });

            result = await response.Content.ReadFromJsonAsync<JsonElement>();
            _handleId = result.GetProperty("data").GetProperty("id").GetInt64().ToString();
            _logger.LogInformation("Attached to AudioBridge plugin: {HandleId}", _handleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing Janus service");
            throw;
        }
    }

    public async Task<string> CreateRoomAsync(string roomName)
    {
        try
        {
            var roomId = new Random().Next(100000, 999999); // Generate numeric room ID
            var response = await _httpClient.PostAsJsonAsync($"{_janusUrl}/{_sessionId}/{_handleId}", new
            {
                janus = "message",
                body = new
                {
                    request = "create",
                    room = roomId,
                    description = $"Voice chat room: {roomName}",
                    is_private = false,
                    audiolevel_ext = true,
                    audio_active_packets = 100,
                    audio_level_average = 25
                },
                transaction = Guid.NewGuid().ToString()
            });

            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            var createdRoomId = result.GetProperty("plugindata").GetProperty("data").GetProperty("room").GetInt64();
            _logger.LogInformation("Created Janus room: {RoomId}", createdRoomId);
            return createdRoomId.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Janus room");
            // Try to log the response content if available
            try
            {
                var errorContent = await _httpClient.GetStringAsync($"{_janusUrl}/{_sessionId}/{_handleId}");
                _logger.LogError("Janus error response: {ErrorContent}", errorContent);
            }
            catch {}
            throw;
        }
    }

    public async Task JoinRoomAsync(string roomId, string userId)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"{_janusUrl}/{_sessionId}/{_handleId}", new
            {
                janus = "message",
                body = new
                {
                    request = "join",
                    room = roomId,
                    id = userId,
                    display = userId,
                    muted = false
                },
                transaction = Guid.NewGuid().ToString()
            });

            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            _logger.LogInformation("User {UserId} joined room {RoomId}", userId, roomId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining Janus room");
            throw;
        }
    }

    public async Task LeaveRoomAsync(string roomId, string userId)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"{_janusUrl}/{_sessionId}/{_handleId}", new
            {
                janus = "message",
                body = new
                {
                    request = "leave",
                    room = roomId
                },
                transaction = Guid.NewGuid().ToString()
            });

            _logger.LogInformation("User {UserId} left room {RoomId}", userId, roomId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving Janus room");
            throw;
        }
    }

    public async Task DestroyRoomAsync(string roomId)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"{_janusUrl}/{_sessionId}/{_handleId}", new
            {
                janus = "message",
                body = new
                {
                    request = "destroy",
                    room = roomId
                },
                transaction = Guid.NewGuid().ToString()
            });

            _logger.LogInformation("Destroyed room {RoomId}", roomId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error destroying Janus room");
            throw;
        }
    }
} 