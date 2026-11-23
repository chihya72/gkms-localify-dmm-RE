global using Google.Protobuf;
using System.Runtime.CompilerServices;

namespace Google.Protobuf;

public interface IMessage { void InternalMergeFrom(); }
public sealed class ExtensionRegistry { }
public static class MessageExtensions
{
    // Signature and call order from the original DMM MessageExtensions_MergeFrom hook.
    // InternalMergeFrom may also be called on partial/nested data without this boundary.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void MergeFrom(IMessage message, ReadOnlySpan<byte> data, bool discardUnknownFields, ExtensionRegistry? registry)
        => message.InternalMergeFrom();
    public static void Read(this IMessage message) => MergeFrom(message, ReadOnlySpan<byte>.Empty, false, null);
}
