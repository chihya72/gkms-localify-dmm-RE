using System.Runtime.InteropServices;
using Gkms.Sdk;

namespace Gkms.Bridge;

public static unsafe class NativeIcallCache
{
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInfo
    { public nint Base, AllocationBase; public uint AllocationProtect; public nuint Size; public uint State, Protect, Type; }
    [DllImport("kernel32.dll")] private static extern nuint VirtualQuery(nint address, out MemoryInfo info, nuint length);
    private static readonly Dictionary<nint, Site> sites = new();
    public static nint Locate(nint wrapper)
    {
        if (wrapper == 0 || VirtualQuery(wrapper, out var code, (nuint)sizeof(MemoryInfo)) == 0 || code.State != 0x1000 || (code.Protect & 0x101) != 0)
            throw new NotSupportedException("icall 包装函数不可读。");
        var available = Math.Min(0x80 + 18, checked((int)(code.Base + (nint)code.Size - wrapper)));
        var p = (byte*)wrapper;
        for (var i = 0; i < Math.Min(0x80, available - 18); i++)
        {
            if (p[i] != 0x48 || p[i + 1] != 0x8b || (p[i + 2] & 0xc7) != 5) continue;
            var reg = (p[i + 2] >> 3) & 7;
            var test = (byte)(0xc0 | reg << 3 | reg);
            var found = false;
            for (var j = i + 7; j <= i + 15; j++)
                if (p[j] == 0x48 && p[j + 1] == 0x85 && p[j + 2] == test) { found = true; break; }
            if (!found) continue;
            var cache = (nint)(p + i + 7 + *(int*)(p + i + 3));
            if ((cache & 7) != 0 || VirtualQuery(cache, out var data, (nuint)sizeof(MemoryInfo)) == 0 ||
                data.State != 0x1000 || (data.Protect & 0x0c) == 0 || (data.Protect & 0x100) != 0 || cache + 8 > data.Base + (nint)data.Size)
                throw new NotSupportedException("icall 缓存不是对齐的可写指针。");
            return cache;
        }
        throw new NotSupportedException("未找到原版 icall 缓存模式，未改写引擎函数。");
    }
    public static IDisposable Replace(nint wrapper, nint original, Delegate replacement)
    {
        if (original == 0) throw new MissingMethodException("icall 原始入口不存在。");
        var cache = Locate(wrapper);
        lock (sites)
        {
            if (!sites.TryGetValue(cache, out var site)) sites.Add(cache, site = new Site(original, replacement.GetType()));
            if (site.Callback != null) throw new InvalidOperationException("icall 缓存已被此宿主接管。");
            if (site.Thunk.GetType() != replacement.GetType()) throw new InvalidOperationException("icall 缓存签名冲突。");
            var before = Volatile.Read(ref *(nint*)cache);
            if (before != IntPtr.Zero && before != original && before != site.Pointer) throw new InvalidOperationException("icall 缓存已被其他模块更改。");
            site.Callback = replacement;
            if (Interlocked.CompareExchange(ref *(nint*)cache, site.Pointer, before) != before)
            { site.Callback = null; throw new InvalidOperationException("icall 缓存写入竞争。"); }
            return new Lease(cache, before, site);
        }
    }
    // Only bridge-owned thunks stay rooted. Clearing Callback allows collectible plugins
    // to unload; an in-flight call retains its own local delegate until it returns.
    private sealed class Site
    {
        public volatile Delegate? Callback;
        public readonly Delegate Thunk;
        public readonly nint Pointer;
        public Site(nint original, Type signature)
        {
            if (signature == typeof(IcallBoolean))
            { var next = Marshal.GetDelegateForFunctionPointer<IcallBoolean>(original); Thunk = new IcallBoolean(() => (Callback as IcallBoolean ?? next)()); }
            else if (signature == typeof(IcallIndexedBoolean))
            { var next = Marshal.GetDelegateForFunctionPointer<IcallIndexedBoolean>(original); Thunk = new IcallIndexedBoolean(i => (Callback as IcallIndexedBoolean ?? next)(i)); }
            else if (signature == typeof(IcallSetBoolean))
            { var next = Marshal.GetDelegateForFunctionPointer<IcallSetBoolean>(original); Thunk = new IcallSetBoolean((i, v) => (Callback as IcallSetBoolean ?? next)(i, v)); }
            else if (signature == typeof(IcallWriteValue))
            { var next = Marshal.GetDelegateForFunctionPointer<IcallWriteValue>(original); Thunk = new IcallWriteValue((i, p) => (Callback as IcallWriteValue ?? next)(i, p)); }
            else throw new NotSupportedException("icall 回调签名不受支持。");
            Pointer = Marshal.GetFunctionPointerForDelegate(Thunk);
        }
    }
    private sealed class Lease(nint cache, nint before, Site site) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            lock (sites)
            {
                if (disposed) return;
                Interlocked.CompareExchange(ref *(nint*)cache, before, site.Pointer);
                site.Callback = null; disposed = true;
            }
        }
    }
}
