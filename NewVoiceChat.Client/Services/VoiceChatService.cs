using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using NewVoiceChat.Shared.Models;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;

namespace NewVoiceChat.Client.Services;

public class VoiceChatService : IAsyncDisposable
{
    private HubConnection? _hubConnection;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<VoiceChatService> _logger;
    private readonly IConfiguration _configuration;
    private string? _currentRoomId;
    private string? _userId;
    private bool _isDisposed;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private DotNetObjectReference<VoiceChatService>? _dotNetObjectReference;
    private IJSObjectReference? _webRtcModule;
    private string? errorMessage;

    public event Action<Room>? OnRoomCreated;
    public event Action<string>? OnRoomDestroyed;
    public event Action<User>? OnUserJoined;
    public event Action<User>? OnUserLeft;
    public event Action<string>? OnError;
    public event Action? OnConnectionStateChanged;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
    public string? CurrentRoomId => _currentRoomId;
    public string UserId => _userId ?? string.Empty;

    public VoiceChatService(IJSRuntime jsRuntime, ILogger<VoiceChatService> logger, IConfiguration configuration)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task InitializeAsync(string serverUrl)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(VoiceChatService));

        await _connectionLock.WaitAsync();
        try
        {
            if (_hubConnection?.State == HubConnectionState.Connected)
                return;

            if (_hubConnection != null)
            {
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }

            _logger.LogInformation($"Initializing connection to SignalR hub at {serverUrl}/voiceChatHub");

            _hubConnection = new HubConnectionBuilder()
                .WithUrl($"{serverUrl}/voiceChatHub", options =>
                {
                    options.SkipNegotiation = false;
                    options.Headers.Add("X-Requested-With", "XMLHttpRequest");
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets |
                                       Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                    options.CloseTimeout = TimeSpan.FromSeconds(5);
                    options.Headers.Add("Origin", "https://localhost:7000");
                })
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Debug);
                })
                .Build();

            RegisterHubEvents();

            try
            {
                _logger.LogInformation("Attempting to connect to SignalR hub...");
                await _hubConnection.StartAsync();
                _logger.LogInformation("Successfully connected to SignalR hub");
                _userId = _hubConnection.ConnectionId;
                OnConnectionStateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to SignalR hub. Server URL: {ServerUrl}", serverUrl);
                OnError?.Invoke($"Failed to connect to the server at {serverUrl}. Please check if the server is running and try again. Error: {ex.Message}");
                throw;
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private void RegisterHubEvents()
    {
        if (_hubConnection == null) return;

        _hubConnection.On<Room>("RoomCreated", room => OnRoomCreated?.Invoke(room));
        _hubConnection.On<string>("RoomDestroyed", roomName => OnRoomDestroyed?.Invoke(roomName));
        _hubConnection.On<User>("UserJoined", user => OnUserJoined?.Invoke(user));
        _hubConnection.On<User>("UserLeft", user => OnUserLeft?.Invoke(user));

        _hubConnection.On<SdpMessage>("ReceiveSdpOffer", async (offer) =>
        {
            try
            {
                _logger.LogInformation($"Received SDP offer from {offer.UserId}");
                await _jsRuntime.InvokeVoidAsync("handleOffer", offer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling SDP offer");
                OnError?.Invoke("Error handling incoming call. Please try again.");
            }
        });

        _hubConnection.On<SdpMessage>("ReceiveSdpAnswer", async (answer) =>
        {
            try
            {
                _logger.LogInformation($"Received SDP answer from {answer.UserId}");
                await _jsRuntime.InvokeVoidAsync("handleAnswer", answer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling SDP answer");
                OnError?.Invoke("Error establishing connection. Please try again.");
            }
        });

        _hubConnection.On<IceCandidate>("ReceiveIceCandidate", async (candidate) =>
        {
            try
            {
                _logger.LogInformation($"Received ICE candidate from {candidate.UserId}");
                await _jsRuntime.InvokeVoidAsync("addIceCandidate", candidate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling ICE candidate");
                OnError?.Invoke("Error establishing connection. Please try again.");
            }
        });

        _hubConnection.Closed += async (error) =>
        {
            _logger.LogError(error, "Connection closed");
            OnConnectionStateChanged?.Invoke();
            if (!_isDisposed)
            {
                await Task.Delay(5000);
                try
                {
                    await EnsureConnectionAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to reconnect");
                    OnError?.Invoke("Lost connection to server. Please refresh the page.");
                }
            }
        };

        _hubConnection.Reconnecting += (error) =>
        {
            _logger.LogWarning(error, "Reconnecting to hub...");
            OnConnectionStateChanged?.Invoke();
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += (connectionId) =>
        {
            _logger.LogInformation($"Reconnected to hub with connection ID: {connectionId}");
            OnConnectionStateChanged?.Invoke();
            return Task.CompletedTask;
        };
    }

    private async Task InitializeWebRTC()
    {
        if (_isDisposed) return;
        try
        {
            _dotNetObjectReference ??= DotNetObjectReference.Create(this);
            if (_webRtcModule == null)
            {
                _logger.LogInformation("Loading WebRTC JavasScript module...");
                _webRtcModule = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/webrtc.js");
                _logger.LogInformation("WebRTC JavaScript module loaded.");
            }
            await _jsRuntime.InvokeVoidAsync("initializeWebRTC", _dotNetObjectReference);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing WebRTC script or calling its initializeWebRTC function");
            OnError?.Invoke("Failed to initialize audio connection components.");
            throw;
        }
    }

    [JSInvokable]
    public async Task HandleWebRtcError(string message)
    {
        _logger.LogError("WebRTC error: {Message}", message);
        OnError?.Invoke(message);
    }

    [JSInvokable]
    public async Task OnIceCandidate(string candidate, string sdpMid, int sdpMLineIndex)
    {
        if (_currentRoomId == null || _hubConnection == null) return;

        try
        {
            await _hubConnection.InvokeAsync("SendIceCandidate", new IceCandidate
            {
                Candidate = candidate,
                SdpMid = sdpMid,
                SdpMLineIndex = sdpMLineIndex,
                RoomId = _currentRoomId,
                UserId = _userId!
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending ICE candidate");
            OnError?.Invoke("Error establishing connection");
        }
    }

    [JSInvokable]
    public async Task SendOffer(SdpMessage offer)
    {
        if (_currentRoomId == null || _hubConnection == null) return;

        try
        {
            offer.RoomId = _currentRoomId;
            offer.UserId = _userId!;
            await _hubConnection.InvokeAsync("SendSdpOffer", offer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending SDP offer");
            OnError?.Invoke("Error establishing connection");
        }
    }

    [JSInvokable]
    public async Task SendAnswer(SdpMessage answer)
    {
        if (_currentRoomId == null || _hubConnection == null) return;

        try
        {
            answer.RoomId = _currentRoomId;
            answer.UserId = _userId!;
            await _hubConnection.InvokeAsync("SendSdpAnswer", answer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending SDP answer");
            OnError?.Invoke("Error establishing connection");
        }
    }

    public async Task CreateRoomAsync(string roomName)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(VoiceChatService));

        try
        {
            await EnsureConnectionAsync();
            _logger.LogInformation($"Creating room: {roomName}");
            await _hubConnection!.InvokeAsync("CreateRoomAsync", roomName);
            errorMessage = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating room");
            OnError?.Invoke("Failed to create room. Please try again.");
            throw;
        }
    }

    public async Task JoinRoomAsync(string roomName)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(VoiceChatService));

        try
        {
            await EnsureConnectionAsync();
            _logger.LogInformation($"Joining room: {roomName}");
            _currentRoomId = roomName;
            await _hubConnection!.InvokeAsync("JoinRoomAsync", roomName);
            errorMessage = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining room");
            _currentRoomId = null;
            OnError?.Invoke("Failed to join room. Please try again.");
            throw;
        }
    }

    public async Task InitializeWebRTCAndStartCallAsync()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(VoiceChatService));
        if (string.IsNullOrEmpty(_currentRoomId))
        {
            _logger.LogWarning("CurrentRoomId is not set. Cannot create peer connection.");
            OnError?.Invoke("Not currently in a room. Please join a room first.");
            return;
        }

        try
        {
            await EnsureConnectionAsync();
            await InitializeWebRTC();

            _logger.LogInformation($"Attempting to create peer connection for room: {_currentRoomId} via user action.");
            await _jsRuntime.InvokeVoidAsync("createPeerConnection", _currentRoomId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during InitializeWebRTCAndStartCallAsync or subsequent JS interop");
            OnError?.Invoke("Failed to start voice call. Please try again.");
            throw;
        }
    }

    public async Task LeaveRoomAsync()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(VoiceChatService));

        if (_currentRoomId != null)
        {
            string roomToLeave = _currentRoomId;
            _currentRoomId = null;

            try
            {
                _logger.LogInformation($"Leaving room: {roomToLeave}");
                if (_hubConnection?.State == HubConnectionState.Connected)
                {
                    await _hubConnection.InvokeAsync("LeaveRoomAsync", roomToLeave);
                }
                
                if (_webRtcModule != null)
                {
                    await _jsRuntime.InvokeVoidAsync("closeWebRTCPeerConnection");
                }
                errorMessage = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leaving room or closing WebRTC connection");
                OnError?.Invoke("Error leaving room. Please try again.");
            }
        }
    }

    public async Task CreatePeerConnection(string roomId)
    {
        try
        {
            _logger.LogInformation("Creating peer connection for room: {RoomId}", roomId);
            await _jsRuntime.InvokeVoidAsync("createPeerConnection", roomId, _userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating peer connection");
            OnError?.Invoke("Failed to create peer connection");
        }
    }

    public async Task HandleOffer(SdpMessage offer)
    {
        try
        {
            _logger.LogInformation("Received SDP offer from {UserId}", offer.UserId);
            await _jsRuntime.InvokeVoidAsync("handleOffer", offer, offer.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling offer");
            OnError?.Invoke("Failed to handle offer");
        }
    }

    public async Task HandleAnswer(SdpMessage answer)
    {
        try
        {
            _logger.LogInformation("Received SDP answer from {UserId}", answer.UserId);
            await _jsRuntime.InvokeVoidAsync("handleAnswer", answer, answer.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling answer");
            OnError?.Invoke("Failed to handle answer");
        }
    }

    public async Task AddIceCandidate(string candidate, string sdpMid, int sdpMLineIndex, string userId)
    {
        try
        {
            _logger.LogInformation("Received ICE candidate from {UserId}", userId);
            await _jsRuntime.InvokeVoidAsync("addIceCandidate", candidate, sdpMid, sdpMLineIndex, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding ICE candidate");
            OnError?.Invoke("Failed to add ICE candidate");
        }
    }

    public async Task ClosePeerConnection(string userId)
    {
        try
        {
            _logger.LogInformation("Closing peer connection for user: {UserId}", userId);
            await _jsRuntime.InvokeVoidAsync("closePeerConnection", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing peer connection");
            OnError?.Invoke("Failed to close peer connection");
        }
    }

    private async Task EnsureConnectionAsync()
    {
        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            await InitializeAsync(_configuration["ServerUrl"] ?? "https://localhost:7001");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        await _connectionLock.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(_currentRoomId) || _webRtcModule != null)
            {
                await LeaveRoomAsync();
            }
            if (_hubConnection != null)
            {
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }
            if (_webRtcModule != null)
            {
                try
                {
                    await _webRtcModule.DisposeAsync();
                }
                catch (JSException ex)
                {
                    _logger.LogWarning(ex, "Error disposing WebRTC JavaScript module. It might not support DisposeAsync.");
                }
                _webRtcModule = null;
            }
            _dotNetObjectReference?.Dispose();
        }
        finally
        {
            _connectionLock.Release();
            _connectionLock.Dispose();
        }
    }
} 