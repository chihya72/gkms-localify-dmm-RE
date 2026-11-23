using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Gkms.Sdk;

namespace Gkms.Bridge;

// Resolve the same exported IL2CPP API used by the original DMM plugin. No disk metadata or generated assemblies.
public sealed unsafe class NativeRuntime
{
    private readonly nint module;
    private readonly ConcurrentDictionary<string, nint> exports = new();
    private readonly Dictionary<nint, Type> types = new();
    private readonly Dictionary<Type, nint> classes = new();
    private readonly ModuleBuilder enums = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Gkms.NativeEnums"), AssemblyBuilderAccess.Run).DefineDynamicModule("Enums");
    private readonly object sync = new();
    private Dictionary<nint, List<(nint Class, nint Method)>>? codeAliases;
    public NativeRuntime(nint module) => this.module = module;
    private string ReadString(nint pointer) => Marshal.PtrToStringUni(P1("string_chars", pointer), checked((int)U1("string_length", pointer)))!;
    internal nint ResolveInternalCall(string signature)
    {
        using var name = new Utf8(signature);
        return P1("resolve_icall", name.Pointer);
    }
    internal nint Export(string name) => exports.GetOrAdd(name, key => NativeLibrary.GetExport(module, "il2cpp_" + key));
    internal nint P0(string name) => ((delegate* unmanaged[Cdecl]<nint>)Export(name))();
    internal nint P1(string name, nint a) => ((delegate* unmanaged[Cdecl]<nint, nint>)Export(name))(a);
    internal nint P2(string name, nint a, nint b) => ((delegate* unmanaged[Cdecl]<nint, nint, nint>)Export(name))(a,b);
    internal uint U1(string name, nint a) => ((delegate* unmanaged[Cdecl]<nint, uint>)Export(name))(a);
    internal bool B1(string name, nint a) => ((delegate* unmanaged[Cdecl]<nint, byte>)Export(name))(a) != 0;
    internal nint At(string name, nint a, uint index) => ((delegate* unmanaged[Cdecl]<nint,uint,nint>)Export(name))(a,index);
    internal static string Text(nint pointer) => Marshal.PtrToStringUTF8(pointer) ?? "";
    internal string Name(string name, nint a) => Text(P1(name,a));
    internal IEnumerable<nint> Enumerate(string name, nint owner)
    {
        // Iterator state is opaque and must remain a native pointer, not an index.
        var result = new List<nint>(); nint iter = 0, item;
        while ((item = ((delegate* unmanaged[Cdecl]<nint,nint*,nint>)Export(name))(owner,&iter)) != 0) result.Add(item);
        return result;
    }
    internal NativeMethod[] Aliases(NativeMethod target)
    {
        lock (sync)
        {
            if (codeAliases == null)
            {
                var index = new Dictionary<nint, List<(nint Class, nint Method)>>();
                nuint count = 0;
                var assemblies = ((delegate* unmanaged[Cdecl]<nint,nuint*,nint>)Export("domain_get_assemblies"))(P0("domain_get"), &count);
                for (nuint i = 0; i < count; i++)
                {
                    var image = P1("assembly_get_image", Marshal.ReadIntPtr(assemblies, checked((int)i * IntPtr.Size)));
                    var total = U1("image_get_class_count", image);
                    for (uint j = 0; j < total; j++)
                    {
                        var klass = At("image_get_class", image, j);
                        if (klass == 0) continue;
                        foreach (var method in Enumerate("class_get_methods", klass))
                        {
                            var code = Marshal.ReadIntPtr(method);
                            if (code == IntPtr.Zero) continue;
                            if (!index.TryGetValue(code, out var aliases)) index[code] = aliases = new();
                            aliases.Add((klass, method));
                        }
                    }
                }
                codeAliases = index;
            }
            // Inflated generic classes may not appear in image_get_class. Include
            // the requested owner as well as all image classes, before any detour.
            var entries = codeAliases.GetValueOrDefault(target.Code) ?? new();
            return entries.Select(e => (e.Class, e.Method))
                .Concat(Enumerate("class_get_methods", target.Owner.Klass)
                    .Where(p => Marshal.ReadIntPtr(p) == target.Code).Select(p => (target.Owner.Klass, p)))
                .DistinctBy(e => e.Item2).Where(e => e.Item2 != target.Handle)
                .Select(e => new NativeMethod(new NativeType(this, e.Item1, TypeName(e.Item1)), e.Item2)).ToArray();
        }
    }
    public Type? FindType(string assembly, string fullName)
    {
        nuint count = 0;
        var all = ((delegate* unmanaged[Cdecl]<nint,nuint*,nint>)Export("domain_get_assemblies"))(P0("domain_get"), &count);
        for (nuint i=0; i<count; i++)
        {
            var image = P1("assembly_get_image", Marshal.ReadIntPtr(all, checked((int)i*IntPtr.Size)));
            var assemblyName=assembly.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)?assembly[..^4]:assembly;
            if (!string.Equals(Path.GetFileNameWithoutExtension(Name("image_get_name",image)), assemblyName, StringComparison.Ordinal)) continue;
            var split = fullName.LastIndexOf('.');
            using var ns = new Utf8(split < 0 ? "" : fullName[..split]);
            using var name = new Utf8(split < 0 ? fullName : fullName[(split+1)..]);
            var klass = ((delegate* unmanaged[Cdecl]<nint,nint,nint,nint>)Export("class_from_name"))(image,ns.Pointer,name.Pointer);
            if (klass != 0) return ClassType(klass);
            // Nested and instantiated classes are exposed through the image's runtime metadata.
            var total = U1("image_get_class_count", image);
            for (uint j=0; j<total; j++)
            {
                klass = At("image_get_class",image,j);
                if (TypeName(klass) == fullName) return ClassType(klass);
            }
        }
        return null;
    }
    internal string TypeName(nint klass)
    {
        var name = Name("class_get_name",klass);
        var parent = P1("class_get_declaring_type",klass);
        if (parent != 0) return TypeName(parent) + "+" + name;
        var ns = Name("class_get_namespace",klass);
        return ns.Length == 0 ? name : ns + "." + name;
    }
    internal Type TypeFrom(nint nativeType)
    {
        var type = ClassType(P1("class_from_type",nativeType));
        return B1("type_is_byref",nativeType) ? new NativeByRef(type) : type;
    }
    internal Type ClassType(nint klass)
    {
        if (klass == 0) return typeof(void);
        lock (sync)
        {
            if (types.TryGetValue(klass,out var known)) return known;
            var name = TypeName(klass);
            var primitive = name switch {
                "System.Void"=>typeof(void), "System.Boolean"=>typeof(bool), "System.Char"=>typeof(char),
                "System.SByte"=>typeof(sbyte), "System.Byte"=>typeof(byte), "System.Int16"=>typeof(short), "System.UInt16"=>typeof(ushort),
                "System.Int32"=>typeof(int), "System.UInt32"=>typeof(uint), "System.Int64"=>typeof(long), "System.UInt64"=>typeof(ulong),
                "System.Single"=>typeof(float), "System.Double"=>typeof(double), "System.IntPtr"=>typeof(nint), "System.UIntPtr"=>typeof(nuint),
                "System.String"=>typeof(string), _=>null };
            if (primitive != null) { types[klass]=primitive; classes[primitive]=klass; return primitive; }
            if (B1("class_is_enum",klass))
            {
                var underlying = TypeFrom(P1("class_enum_basetype",klass));
                var builder = enums.DefineEnum(name,TypeAttributes.Public,underlying);
                var buffer = stackalloc byte[8];
                foreach (var field in Enumerate("class_get_fields",klass))
                {
                    if ((U1("field_get_flags",field) & 0x40) == 0) continue;
                    ((delegate* unmanaged[Cdecl]<nint,void*,void>)Export("field_static_get_value"))(field,buffer);
                    builder.DefineLiteral(Name("field_get_name",field), Read((nint)buffer,underlying));
                }
                var value = builder.CreateTypeInfo()!.AsType(); types[klass]=value; classes[value]=klass; return value;
            }
            var native = new NativeType(this,klass,name); types[klass]=native; classes[native]=klass; return native;
        }
    }
    internal nint ClassOf(Type type) => type is NativeType native ? native.Klass : classes.TryGetValue(type,out var klass) ? klass :
        FindType("mscorlib", type.FullName!) is { } found ? classes[found] : throw new TypeLoadException(type.FullName);
    internal int Size(Type type)
    {
        if (type is NativeType native && native.IsValueType)
        {
            uint alignment=0;
            return ((delegate* unmanaged[Cdecl]<nint,uint*,int>)Export("class_value_size"))(native.Klass,&alignment);
        }
        if (!type.IsValueType) return IntPtr.Size;
        if (type.IsEnum) return Size(Enum.GetUnderlyingType(type));
        if (type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte)) return 1;
        if (type == typeof(char) || type == typeof(short) || type == typeof(ushort)) return 2;
        return Marshal.SizeOf(type);
    }
    internal nint NewString(string text)
    {
        nint pointer;
        fixed(char* chars=text) pointer = ((delegate* unmanaged[Cdecl]<char*,int,nint>)Export("string_new_utf16"))(chars,text.Length);
        return pointer;
    }
    internal nint Reference(object? value, Type expected, NativeMemoryScope scope)
    {
        if (value == null) return 0;
        if (value is NativeObject native) return native.Pointer;
        if (value is string text) { var str=NewString(text); scope.Root(this,str); return str; }
        if (value is Array array && expected is NativeType { IsArray:true } type)
        {
            var element=type.GetElementType()!;
            var pointer=((delegate* unmanaged[Cdecl]<nint,nuint,nint>)Export("array_new"))(ClassOf(element),(nuint)array.Length);
            scope.Root(this,pointer);
            if(element==typeof(byte) && array is byte[] bytes)
            {
                Marshal.Copy(bytes,0,pointer+4*IntPtr.Size,bytes.Length);
                return pointer;
            }
            for(int i=0;i<array.Length;i++) Write(pointer+4*IntPtr.Size+i*Size(element),element,array.GetValue(i),scope,pointer);
            return pointer;
        }
        throw new InvalidCastException($"Cannot marshal {value.GetType()} to {expected}.");
    }
    internal object? Wrap(nint pointer)
    {
        if(pointer==0) return null;
        var klass=P1("object_get_class",pointer); var type=ClassType(klass);
        if(type==typeof(string)) return ReadString(pointer);
        if(type is not NativeType) return Read(P1("object_unbox",pointer),type);
        return ((NativeType)type).IsArray ? new NativeArray(this,(NativeType)type,pointer) : new NativeObject(this,(NativeType)type,pointer);
    }
    internal object? Read(nint address, Type type)
    {
        if(!type.IsValueType) return Wrap(Marshal.ReadIntPtr(address));
        if(type is NativeType native) return Wrap(P2("value_box",native.Klass,address));
        if(type.IsEnum) return Enum.ToObject(type,Read(address,Enum.GetUnderlyingType(type))!);
        if(type==typeof(bool)) return Marshal.ReadByte(address)!=0;
        if(type==typeof(char)) return (char)Marshal.ReadInt16(address);
        return Marshal.PtrToStructure(address,type);
    }
    internal void Write(nint address, Type type, object? value, NativeMemoryScope scope, nint owner=0)
    {
        if(!type.IsValueType)
        {
            var pointer=Reference(value,type,scope);
            if(owner!=0) ((delegate* unmanaged[Cdecl]<nint,nint,nint,void>)Export("gc_wbarrier_set_field"))(owner,address,pointer);
            else Marshal.WriteIntPtr(address,pointer);
            return;
        }
        if(type is NativeType)
        {
            if(value==null) { for(int i=0;i<Size(type);i++) Marshal.WriteByte(address,i,0); return; }
            if(value is not NativeObject native) throw new InvalidCastException(type.FullName);
            Buffer.MemoryCopy((void*)P1("object_unbox",native.Pointer),(void*)address,Size(type),Size(type)); return;
        }
        if(type.IsEnum) { Write(address,Enum.GetUnderlyingType(type),Convert.ChangeType(value,Enum.GetUnderlyingType(type)),scope); return; }
        if(type==typeof(bool)) { Marshal.WriteByte(address,Convert.ToBoolean(value)?(byte)1:(byte)0); return; }
        if(type==typeof(char)) { Marshal.WriteInt16(address,(short)Convert.ToChar(value)); return; }
        Marshal.StructureToPtr(Interop.ConvertValue(type,value) ?? Activator.CreateInstance(type)!,address,false);
    }
    internal object? Invoke(nint method, object? instance, object?[] arguments, NativeParameter[] parameters)
    {
        if(arguments.Length!=parameters.Length) throw new TargetParameterCountException();
        using var scope=new NativeMemoryScope();
        var argv=(nint*)scope.Alloc(Math.Max(1,arguments.Length)*IntPtr.Size);
        for(int i=0;i<arguments.Length;i++)
        {
            var type=parameters[i].ParameterType; var element=type.IsByRef?type.GetElementType()!:type;
            if(element.IsValueType || type.IsByRef) { argv[i]=scope.Alloc(Size(element)); Write(argv[i],element,arguments[i],scope); }
            else argv[i]=Reference(arguments[i],element,scope);
        }
        nint exception=0;
        var obj=instance is NativeObject native ? native.Pointer : 0;
        if(obj!=0)
        {
            uint implementation=0;
            var flags=((delegate* unmanaged[Cdecl]<nint,uint*,uint>)Export("method_get_flags"))(method,&implementation);
            if((flags & 0x40)!=0) method=P2("object_get_virtual_method",obj,method);
        }
        // The invoker for a value-type instance expects the unboxed data pointer.
        if(instance is NativeObject { NativeType.IsValueType:true }) obj=P1("object_unbox",obj);
        var result=((delegate* unmanaged[Cdecl]<nint,nint,nint*,nint*,nint>)Export("runtime_invoke"))(method,obj,argv,&exception);
        if(exception!=0) throw new NativeGameException(Wrap(exception));
        for(int i=0;i<arguments.Length;i++) if(parameters[i].ParameterType.IsByRef) arguments[i]=Read(argv[i],parameters[i].ParameterType.GetElementType()!);
        return Wrap(result);
    }
    // Unity 6 Il2CppGCHandle is a pointer, including for temporary strings in
    // hook callbacks. A uint truncates the address before get_target/free use it.
    internal nint Root(nint obj) => ((delegate* unmanaged[Cdecl]<nint,byte,nint>)Export("gchandle_new"))(obj,0);
    internal nint Target(nint handle) => ((delegate* unmanaged[Cdecl]<nint,nint>)Export("gchandle_get_target"))(handle);
    internal void Release(nint handle) { if(handle!=0) ((delegate* unmanaged[Cdecl]<nint,void>)Export("gchandle_free"))(handle); }
    internal nint NewObject(nint klass) => P1("object_new",klass);
    internal void CopyValueInto(NativeObject value,nint data) => Buffer.MemoryCopy((void*)data,(void*)P1("object_unbox",value.Pointer),Size(value.NativeType),Size(value.NativeType));
    internal static object[] Attributes => Array.Empty<object>();
    private sealed class Utf8 : IDisposable
    { public nint Pointer {get;} public Utf8(string value)=>Pointer=Marshal.StringToCoTaskMemUTF8(value); public void Dispose()=>Marshal.FreeCoTaskMem(Pointer); }
}

public class NativeObject : INativeObject
{
    internal readonly NativeRuntime Runtime;
    private readonly nint root;
    public Type NativeType {get;}
    public nint Pointer => Runtime.Target(root);
    internal NativeObject(NativeRuntime runtime, NativeType type,nint pointer) { Runtime=runtime; NativeType=type; root=runtime.Root(pointer); }
    ~NativeObject() { Runtime.Release(root); }
    public override bool Equals(object? other)=>other is NativeObject obj && Pointer==obj.Pointer;
    public override int GetHashCode()=>Pointer.GetHashCode();
    public override string ToString()=>NativeType.FullName??NativeType.Name;
}

public sealed class NativeArray : NativeObject, IEnumerable, INativeByteBuffer
{
    internal NativeArray(NativeRuntime runtime,NativeType type,nint pointer):base(runtime,type,pointer){}
    public int Length=>checked((int)Runtime.U1("array_length",Pointer));
    public byte[] CopyBytes()
    {
        if(NativeType.GetElementType()!=typeof(byte) || NativeType.GetArrayRank()!=1) throw new InvalidCastException("Expected a native byte array.");
        var bytes=new byte[Length];
        Marshal.Copy(Pointer+4*IntPtr.Size,bytes,0,bytes.Length);
        GC.KeepAlive(this);
        return bytes;
    }
    public object? this[int index]
    {
        get { if((uint)index>=(uint)Length) throw new IndexOutOfRangeException(); var type=NativeType.GetElementType()!; return Runtime.Read(Pointer+4*IntPtr.Size+index*Runtime.Size(type),type); }
        set { if((uint)index>=(uint)Length) throw new IndexOutOfRangeException(); using var scope=new NativeMemoryScope(); var type=NativeType.GetElementType()!; Runtime.Write(Pointer+4*IntPtr.Size+index*Runtime.Size(type),type,value,scope,Pointer); }
    }
    public IEnumerator GetEnumerator() { for(int i=0;i<Length;i++) yield return this[i]; }
}

internal sealed class NativeMemoryScope : IDisposable
{
    private readonly List<nint> allocations=new();
    private readonly List<(NativeRuntime Runtime,nint Handle)> roots=new();
    public nint Alloc(int size) { var ptr=Marshal.AllocHGlobal(size); allocations.Add(ptr); for(int i=0;i<size;i++) Marshal.WriteByte(ptr,i,0); return ptr; }
    public void Root(NativeRuntime runtime,nint obj) { if(obj!=0) roots.Add((runtime,runtime.Root(obj))); }
    public void Dispose() { foreach(var root in roots) root.Runtime.Release(root.Handle); foreach(var ptr in allocations) Marshal.FreeHGlobal(ptr); }
}

public sealed class NativeGameException : Exception
{
    public object? NativeException {get;}
    internal NativeGameException(object? exception):base("IL2CPP invocation failed: "+exception)=>NativeException=exception;
}

internal sealed class NativeByRef : TypeDelegator
{
    private readonly Type element;
    internal NativeByRef(Type element):base(typeof(nint))=>this.element=element;
    public override string Name=>element.Name+"&";
    public override string? FullName=>element.FullName+"&";
    public override Type GetElementType()=>element;
    protected override bool IsByRefImpl()=>true;
    protected override bool HasElementTypeImpl()=>true;
}
