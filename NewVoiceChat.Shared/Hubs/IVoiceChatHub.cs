using NewVoiceChat.Shared.Models;

namespace NewVoiceChat.Shared.Hubs;

public interface IVoiceChatHub
{
    Task RoomCreated(Room room);
    Task RoomDestroyed(string roomName);
    Task UserJoined(User user);
    Task UserLeft(User user);
    Task IceCandidateReceived(IceCandidateMessage candidate);
} 