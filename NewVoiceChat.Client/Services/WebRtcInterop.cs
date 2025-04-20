using Microsoft.JSInterop;
using NewVoiceChat.Shared.Models;

namespace NewVoiceChat.Client.Services;

public class WebRtcInterop
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<WebRtcInterop> _logger;

    public WebRtcInterop(IJSRuntime jsRuntime, ILogger<WebRtcInterop> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task<bool> InitializeWebRTC()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>("initializeWebRTC");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing WebRTC");
            return false;
        }
    }

    public async Task CreatePeerConnection()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("createPeerConnection");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating peer connection");
            throw;
        }
    }

    public async Task<SdpMessage> CreateOffer(string roomId, string userId)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<SdpMessage>("createOffer", roomId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating offer");
            throw;
        }
    }

    public async Task HandleAnswer(SdpMessage answer)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("handleAnswer", answer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling answer");
            throw;
        }
    }

    public async Task<SdpMessage> HandleOffer(SdpMessage offer, string roomId, string userId)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<SdpMessage>("handleOffer", offer, roomId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling offer");
            throw;
        }
    }

    public async Task AddIceCandidate(IceCandidate candidate)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("addIceCandidate", candidate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding ICE candidate");
            throw;
        }
    }

    public async Task StopWebRTC()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("stopWebRTC");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping WebRTC");
            throw;
        }
    }
} 