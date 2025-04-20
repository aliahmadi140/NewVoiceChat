using Microsoft.AspNetCore.SignalR;
using NewVoiceChat.Server.Services;
using NewVoiceChat.Shared.Models;
using NewVoiceChat.Shared.Hubs;
using Microsoft.Extensions.Logging;

namespace NewVoiceChat.Server.Hubs;

public class VoiceChatHub : Hub<IVoiceChatHub>
{
    private readonly ILogger<VoiceChatHub> _logger;
    private readonly JanusService _janusService;
    private static readonly Dictionary<string, Room> _rooms = new();
    private static readonly Dictionary<string, string> _userRooms = new();

    public VoiceChatHub(ILogger<VoiceChatHub> logger, JanusService janusService)
    {
        _logger = logger;
        _janusService = janusService;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        
        if (_userRooms.TryGetValue(Context.ConnectionId, out var roomId))
        {
            await LeaveRoomAsync(roomId);
        }
        
        await base.OnDisconnectedAsync(exception);
    }

    public async Task CreateRoomAsync(string roomName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(roomName))
            {
                throw new HubException("Room name cannot be empty");
            }

            if (_rooms.ContainsKey(roomName))
            {
                throw new HubException("Room already exists");
            }

            // Create room in Janus
            var janusRoomId = await _janusService.CreateRoomAsync(roomName);

            var room = new Room
            {
                Id = janusRoomId,
                Name = roomName,
                CreatedAt = DateTime.UtcNow,
                Users = new List<User>()
            };

            _rooms[roomName] = room;
            _logger.LogInformation("Room created: {RoomName}", roomName);

            await Clients.All.RoomCreated(room);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating room: {RoomName}", roomName);
            throw new HubException("Failed to create room");
        }
    }

    public async Task JoinRoomAsync(string roomName)
    {
        try
        {
            if (!_rooms.TryGetValue(roomName, out var room))
            {
                throw new HubException("Room not found");
            }

            var user = new User
            {
                Id = Context.ConnectionId,
                Name = $"User-{Context.ConnectionId[..8]}"
            };

            // Join room in Janus
            await _janusService.JoinRoomAsync(room.Id, user.Id);

            room.Users.Add(user);
            _userRooms[user.Id] = roomName;

            _logger.LogInformation("User {UserId} joined room {RoomName}", user.Id, roomName);
            await Clients.Group(roomName).UserJoined(user);
            await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining room: {RoomName}", roomName);
            throw new HubException("Failed to join room");
        }
    }

    public async Task LeaveRoomAsync(string roomName)
    {
        try
        {
            if (!_rooms.TryGetValue(roomName, out var room))
            {
                throw new HubException("Room not found");
            }

            var user = room.Users.FirstOrDefault(u => u.Id == Context.ConnectionId);
            if (user == null)
            {
                throw new HubException("User not in room");
            }

            // Leave room in Janus
            await _janusService.LeaveRoomAsync(room.Id, user.Id);

            room.Users.Remove(user);
            _userRooms.Remove(user.Id);

            _logger.LogInformation("User {UserId} left room {RoomName}", user.Id, roomName);
            await Clients.Group(roomName).UserLeft(user);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomName);

            if (!room.Users.Any())
            {
                await _janusService.DestroyRoomAsync(room.Id);
                _rooms.Remove(roomName);
                await Clients.All.RoomDestroyed(roomName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving room: {RoomName}", roomName);
            throw new HubException("Failed to leave room");
        }
    }

    public async Task SendIceCandidateAsync(string roomName, string candidate, string sdpMid, int sdpMLineIndex)
    {
        try
        {
            if (!_rooms.TryGetValue(roomName, out var room))
            {
                throw new HubException("Room not found");
            }

            var user = room.Users.FirstOrDefault(u => u.Id == Context.ConnectionId);
            if (user == null)
            {
                throw new HubException("User not in room");
            }

            await Clients.OthersInGroup(roomName).IceCandidateReceived(new IceCandidateMessage
            {
                UserId = user.Id,
                Candidate = candidate,
                SdpMid = sdpMid,
                SdpMLineIndex = sdpMLineIndex
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending ICE candidate");
            throw new HubException("Failed to send ICE candidate");
        }
    }
} 