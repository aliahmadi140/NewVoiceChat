using Microsoft.JSInterop;
using System.Text.Json;
using NewVoiceChat.Shared.Models;

namespace NewVoiceChat.Client.Services;

public class JanusService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<JanusService> _logger;
    private IJSObjectReference? _janusModule;
    private string? _sessionId;
    private string? _handleId;
    private readonly string _serverUrl = "ws://localhost:8188";

    public event Action<string>? OnError;
    public event Action<User>? OnUserJoined;
    public event Action<User>? OnUserLeft;

    public JanusService(IJSRuntime jsRuntime, ILogger<JanusService> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _janusModule = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/janus.js");
            await _janusModule.InvokeVoidAsync("initialize", _serverUrl, DotNetObjectReference.Create(this));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Janus");
            OnError?.Invoke("Failed to initialize Janus connection");
            throw;
        }
    }

    [JSInvokable]
    public void OnJanusError(string error)
    {
        _logger.LogError("Janus error: {Error}", error);
        OnError?.Invoke($"Janus error: {error}");
    }

    [JSInvokable]
    public void OnSessionCreated(string sessionId)
    {
        _sessionId = sessionId;
        _logger.LogInformation("Janus session created: {SessionId}", sessionId);
    }

    [JSInvokable]
    public void OnHandleCreated(string handleId)
    {
        _handleId = handleId;
        _logger.LogInformation("Janus handle created: {HandleId}", handleId);
    }

    public async Task JoinRoom(string roomId)
    {
        try
        {
            if (_janusModule == null)
                throw new InvalidOperationException("Janus not initialized");

            await _janusModule.InvokeVoidAsync("joinRoom", roomId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join room");
            OnError?.Invoke("Failed to join room");
            throw;
        }
    }

    public async Task LeaveRoom()
    {
        try
        {
            if (_janusModule == null)
                return;

            await _janusModule.InvokeVoidAsync("leaveRoom");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to leave room");
            OnError?.Invoke("Failed to leave room");
            throw;
        }
    }

    [JSInvokable]
    public void OnParticipantJoined(string participantJson)
    {
        try
        {
            var participant = JsonSerializer.Deserialize<User>(participantJson);
            if (participant != null)
            {
                OnUserJoined?.Invoke(participant);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle participant joined event");
        }
    }

    [JSInvokable]
    public void OnParticipantLeft(string participantJson)
    {
        try
        {
            var participant = JsonSerializer.Deserialize<User>(participantJson);
            if (participant != null)
            {
                OnUserLeft?.Invoke(participant);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle participant left event");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_janusModule != null)
            {
                await LeaveRoom();
                await _janusModule.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing Janus service");
        }
    }
} 