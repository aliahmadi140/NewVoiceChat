namespace NewVoiceChat.Shared.Models;

public class SdpMessage
{
    public string Type { get; set; } = string.Empty; // "offer" or "answer"
    public string Sdp { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
}

public class IceCandidate
{
    public string Candidate { get; set; } = string.Empty;
    public string SdpMid { get; set; } = string.Empty;
    public int SdpMLineIndex { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
} 