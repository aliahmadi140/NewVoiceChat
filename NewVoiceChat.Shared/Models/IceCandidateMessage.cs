namespace NewVoiceChat.Shared.Models;

public class IceCandidateMessage
{
    public string UserId { get; set; } = string.Empty;
    public string Candidate { get; set; } = string.Empty;
    public string SdpMid { get; set; } = string.Empty;
    public int SdpMLineIndex { get; set; }
} 