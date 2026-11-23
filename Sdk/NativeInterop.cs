namespace Gkms.Sdk;

// Game objects live in the game's IL2CPP heap, not in the manager's CLR heap.
// Keep these interfaces in the shared SDK so packages never need the bridge assembly.
public interface INativeObject
{
    Type NativeType { get; }
    IntPtr Pointer { get; }
}

public interface INativeByteBuffer { byte[] CopyBytes(); }

// Optional engine bindings can survive IL2CPP managed-code stripping.
public interface INativeInternalCalls { IntPtr ResolveInternalCall(string signature); }

// Replace the generated wrapper's shared icall cache, not a folded engine stub.
public interface INativeCachedInternalCalls
{
    IDisposable ReplaceInternalCallCache(System.Reflection.MethodInfo wrapper, string signature, Delegate replacement);
}
[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
public delegate byte IcallBoolean();
[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
public delegate byte IcallIndexedBoolean(int index);
[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
public delegate void IcallSetBoolean(int index, byte value);
[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
public delegate void IcallWriteValue(int index, IntPtr destination);

public interface INativeType
{
    object Create(object?[] arguments);
    object TypeObject { get; }
    object Cast(object value);
}

public static partial class Interop
{
    public static Type TypeOf(object value) => value is INativeObject native ? native.NativeType : value.GetType();
    public static object? New(Type type, params object?[] arguments) =>
        type is INativeType native ? native.Create(arguments) : Activator.CreateInstance(type, arguments);
    public static object NativeTypeObject(Type type) => type is INativeType native ? native.TypeObject : type;
    public static object Cast(object value, Type type) => type is INativeType native ? native.Cast(value) :
        type.IsInstanceOfType(value) ? value : throw new InvalidCastException(type.FullName);
    // Callback generic arguments must be CLR types; native reference/value objects are boxed bridge objects.
    public static Type CallbackType(Type type) => type is INativeType ? typeof(object) : type;
    public static IntPtr PointerOf(object value) => value is INativeObject native ? native.Pointer :
        value.GetType().GetProperty("Pointer")?.GetValue(value) is IntPtr pointer ? pointer : IntPtr.Zero;
}
