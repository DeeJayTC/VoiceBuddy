namespace VoiceBuddy.Models;

public enum AudioDeviceKind { Render, Capture }

public sealed record AudioDevice(
    string Id,
    string FriendlyName,
    AudioDeviceKind Kind,
    bool IsDefault);
