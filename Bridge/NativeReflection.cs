using System.Globalization;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using Gkms.Sdk;

namespace Gkms.Bridge;

public sealed unsafe class NativeType : TypeDelegator, INativeType, INativeInternalCalls, INativeCachedInternalCalls
{
    public IDisposable ReplaceInternalCallCache(MethodInfo wrapper, string signature, Delegate replacement) =>
        NativeIcallCache.Replace(((NativeMethod)wrapper).Code, ResolveInternalCall(signature), replacement);
    internal readonly NativeRuntime Runtime;
    internal readonly nint Klass;
    private readonly string name;
    private readonly Lazy<Type?> parentType;
    private readonly Lazy<NativeMethod[]> declaredMethods;
    private readonly ConcurrentDictionary<BindingFlags,MethodInfo[]> methods = new();
    private readonly ConcurrentDictionary<BindingFlags,ConstructorInfo[]> constructors = new();
    private readonly ConcurrentDictionary<BindingFlags,PropertyInfo[]> properties = new();
    private readonly ConcurrentDictionary<BindingFlags,FieldInfo[]> fields = new();
    internal NativeType(NativeRuntime runtime,nint klass,string name):base(typeof(NativeObject))
    {
        Runtime=runtime; Klass=klass; this.name=name;
        parentType=new(()=>{ var parent=Runtime.P1("class_get_parent",Klass); return parent==0?null:Runtime.ClassType(parent); });
        declaredMethods=new(()=>Runtime.Enumerate("class_get_methods",Klass).Select(p=>new NativeMethod(this,p)).ToArray());
    }
    public override string Name => name[(Math.Max(name.LastIndexOf('.'),name.LastIndexOf('+'))+1)..];
    public override string FullName=>name;
    public override string? Namespace=>name.Contains('.')?name[..name.LastIndexOf('.')]:"";
    public override string AssemblyQualifiedName=>name+", "+Assembly.GetName().Name;
    public override Type UnderlyingSystemType=>this;
    public override Type? BaseType=>parentType.Value;
    public override bool IsInstanceOfType(object? value) => value is NativeObject obj && IsAssignableFrom(obj.NativeType) || IsArray && value is Array;
    public override bool IsAssignableFrom(Type? other) => other is NativeType native &&
        ((delegate* unmanaged[Cdecl]<nint,nint,byte>)Runtime.Export("class_is_assignable_from"))(Klass,native.Klass)!=0;
    public override bool Equals(object? other)=>ReferenceEquals(this,other);
    public override bool Equals(Type? other)=>ReferenceEquals(this,other);
    public override int GetHashCode()=>Klass.GetHashCode();
    public override string ToString()=>FullName;
    protected override bool IsValueTypeImpl()=>Runtime.B1("class_is_valuetype",Klass);
    protected override bool IsArrayImpl()=>Runtime.U1("class_get_rank",Klass)>0;
    protected override bool IsPrimitiveImpl()=>false;
    protected override bool IsPointerImpl()=>false;
    protected override bool IsByRefImpl()=>false;
    protected override bool HasElementTypeImpl()=>IsArray;
    public override Type? GetElementType()=>IsArray?Runtime.ClassType(Runtime.P1("class_get_element_class",Klass)):null;
    public override int GetArrayRank()=>checked((int)Runtime.U1("class_get_rank",Klass));
    protected override TypeAttributes GetAttributeFlagsImpl()=>(TypeAttributes)Runtime.U1("class_get_flags",Klass);
    public override Type[] GetInterfaces()=>Runtime.Enumerate("class_get_interfaces",Klass).Select(Runtime.ClassType).ToArray();
    public object TypeObject=>Runtime.Wrap(Runtime.P1("type_get_object",Runtime.P1("class_get_type",Klass)))!;
    public IntPtr ResolveInternalCall(string signature)=>Runtime.ResolveInternalCall(signature);
    public object Cast(object value)=>IsInstanceOfType(value)?value:throw new InvalidCastException(FullName);
    public object Create(object?[] arguments)
    {
        if(IsArray) throw new NotSupportedException("Pass a managed array to an IL2CPP method instead.");
        var constructor=GetConstructors(Interop.Members).SingleOrDefault(c=>NativeMember.Matches(c.GetParameters(),arguments));
        if(constructor!=null) return constructor.Invoke(arguments);
        if(arguments.Length==0 && IsValueType) return Runtime.Wrap(Runtime.NewObject(Klass))!;
        throw new MissingMethodException(FullName,".ctor");
    }
    public override bool IsGenericType=>Runtime.B1("class_is_generic",Klass) || Runtime.B1("class_is_inflated",Klass);
    public override bool IsGenericTypeDefinition=>Runtime.B1("class_is_generic",Klass) && !Runtime.B1("class_is_inflated",Klass);
    public override bool ContainsGenericParameters=>IsGenericTypeDefinition;
    public override Type[] GenericTypeArguments=>IsGenericTypeDefinition?Type.EmptyTypes:GetGenericArguments();
    public override Type[] GetGenericArguments()
    {
        if(!IsGenericType) return Type.EmptyTypes;
        var reflected=TypeObject;
        var arguments=Interop.Call(reflected,"GetGenericArguments");
        return Interop.Items(arguments).Cast<NativeObject>().Select(t=>Runtime.ClassType(Runtime.P1("class_from_system_type",t.Pointer))).ToArray();
    }
    public override Type GetGenericTypeDefinition()
    {
        if(!IsGenericType) throw new InvalidOperationException();
        var reflected=(NativeObject)Interop.Call(TypeObject,"GetGenericTypeDefinition")!;
        return Runtime.ClassType(Runtime.P1("class_from_system_type",reflected.Pointer));
    }
    // IL2CPP member metadata is stable for this runtime. Cache descriptions only,
    // never object values or code pointers (MinHook can change the latter).
    private MethodInfo[] Methods(BindingFlags flags)=>methods.GetOrAdd(flags,f=>
        EnumerateMethods().Where(m=>m.Name is not (".ctor" or ".cctor") && NativeMember.Visible(m.Attributes,f)).Cast<MethodInfo>()
            .Concat((f&BindingFlags.DeclaredOnly)==0 && BaseType!=null?BaseType.GetMethods(f):Array.Empty<MethodInfo>()).ToArray());
    public override MethodInfo[] GetMethods(BindingFlags flags)=>Methods(flags).ToArray();
    internal IEnumerable<NativeMethod> EnumerateMethods()=>declaredMethods.Value;
    private ConstructorInfo[] Constructors(BindingFlags flags)=>constructors.GetOrAdd(flags,f=>EnumerateMethods().Where(m=>m.Name==".ctor" && NativeMember.Visible(m.Attributes,f)).Select(m=>(ConstructorInfo)new NativeConstructor(m)).ToArray());
    public override ConstructorInfo[] GetConstructors(BindingFlags flags)=>Constructors(flags).ToArray();
    private PropertyInfo[] Properties(BindingFlags flags)=>properties.GetOrAdd(flags,BuildProperties);
    public override PropertyInfo[] GetProperties(BindingFlags flags)=>Properties(flags).ToArray();
    private PropertyInfo[] BuildProperties(BindingFlags flags)
    {
        var properties=Runtime.Enumerate("class_get_properties",Klass).Select(p=>new NativeProperty(this,p)).Where(p=>
            p.GetAccessors(true).Any(m=>NativeMember.Visible(m.Attributes,flags))).Cast<PropertyInfo>();
        var declared=properties.ToArray();
        var names=declared.Select(p=>p.Name).ToHashSet(StringComparer.Ordinal);
        properties=declared.Concat(Fields(flags|BindingFlags.DeclaredOnly).Where(f=>!names.Contains(f.Name)).Select(f=>(PropertyInfo)new NativeFieldProperty(f)));
        if(IsArray) properties=properties.Concat(typeof(NativeArray).GetProperties(flags));
        return properties.Concat((flags&BindingFlags.DeclaredOnly)==0 && BaseType!=null?BaseType.GetProperties(flags):Array.Empty<PropertyInfo>()).ToArray();
    }
    private FieldInfo[] Fields(BindingFlags flags)=>fields.GetOrAdd(flags,f=>Runtime.Enumerate("class_get_fields",Klass).Select(p=>new NativeField(this,p))
        .Where(field=>NativeMember.Visible((MethodAttributes)(uint)field.Attributes,f)).Cast<FieldInfo>()
        .Concat((f&BindingFlags.DeclaredOnly)==0 && BaseType!=null?BaseType.GetFields(f):Array.Empty<FieldInfo>()).ToArray());
    public override FieldInfo[] GetFields(BindingFlags flags)=>Fields(flags).ToArray();
    protected override MethodInfo? GetMethodImpl(string name,BindingFlags flags,Binder? binder,CallingConventions convention,Type[]? types,ParameterModifier[]? modifiers)=>
        Methods(flags).FirstOrDefault(m=>m.Name==name && (types==null || NativeMember.Types(m.GetParameters(),types)));
    protected override ConstructorInfo? GetConstructorImpl(BindingFlags flags,Binder? binder,CallingConventions convention,Type[] types,ParameterModifier[]? modifiers)=>
        Constructors(flags).SingleOrDefault(c=>NativeMember.Types(c.GetParameters(),types));
    protected override PropertyInfo? GetPropertyImpl(string name,BindingFlags flags,Binder? binder,Type? returnType,Type[]? types,ParameterModifier[]? modifiers)=>
        Properties(flags).FirstOrDefault(p=>NativeMember.NameMatches(p.Name,name,flags) && (returnType==null || p.PropertyType==returnType) &&
            (types==null || NativeMember.Types(p.GetIndexParameters(),types)));
    public override FieldInfo? GetField(string name,BindingFlags flags)=>Fields(flags).FirstOrDefault(f=>NativeMember.NameMatches(f.Name,name,flags));
    public override MemberInfo[] GetMembers(BindingFlags flags)=>GetMethods(flags).Cast<MemberInfo>().Concat(GetConstructors(flags)).Concat(GetProperties(flags)).Concat(GetFields(flags)).ToArray();
    public override object[] GetCustomAttributes(bool inherit)=>NativeRuntime.Attributes;
    public override object[] GetCustomAttributes(Type type,bool inherit)=>NativeRuntime.Attributes;
    public override bool IsDefined(Type type,bool inherit)=>false;
}

// Expose fields through the same property access surface used by existing modular plugins.
internal sealed class NativeFieldProperty : PropertyInfo
{
    private readonly FieldInfo field;
    internal NativeFieldProperty(FieldInfo field)=>this.field=field;
    public override string Name=>field.Name;
    public override Type? DeclaringType=>field.DeclaringType;
    public override Type? ReflectedType=>field.ReflectedType;
    public override Type PropertyType=>field.FieldType;
    public override PropertyAttributes Attributes=>PropertyAttributes.None;
    public override bool CanRead=>true;
    public override bool CanWrite=>!field.IsLiteral && !field.IsInitOnly;
    public override ParameterInfo[] GetIndexParameters()=>Array.Empty<ParameterInfo>();
    public override MethodInfo[] GetAccessors(bool nonPublic)=>Array.Empty<MethodInfo>();
    public override MethodInfo? GetGetMethod(bool nonPublic)=>null;
    public override MethodInfo? GetSetMethod(bool nonPublic)=>null;
    public override object? GetValue(object? obj,BindingFlags flags,Binder? binder,object?[]? index,CultureInfo? culture)=>field.GetValue(obj);
    public override void SetValue(object? obj,object? value,BindingFlags flags,Binder? binder,object?[]? index,CultureInfo? culture)=>field.SetValue(obj,value);
    public override object[] GetCustomAttributes(bool inherit)=>NativeRuntime.Attributes;
    public override object[] GetCustomAttributes(Type type,bool inherit)=>NativeRuntime.Attributes;
    public override bool IsDefined(Type type,bool inherit)=>false;
}

internal static class NativeMember
{
    public static bool NameMatches(string actual,string expected,BindingFlags flags)=>string.Equals(actual,expected,(flags&BindingFlags.IgnoreCase)!=0?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);
    public static bool Visible(MethodAttributes attributes,BindingFlags flags)=>
        ((attributes&MethodAttributes.Static)!=0?(flags&BindingFlags.Static)!=0:(flags&BindingFlags.Instance)!=0) &&
        ((attributes&MethodAttributes.MemberAccessMask)==MethodAttributes.Public?(flags&BindingFlags.Public)!=0:(flags&BindingFlags.NonPublic)!=0);
    public static bool Types(ParameterInfo[] parameters,Type[] types)=>parameters.Length==types.Length && parameters.Select((p,i)=>p.ParameterType==types[i] || p.ParameterType.FullName==types[i].FullName).All(v=>v);
    public static bool Matches(ParameterInfo[] parameters,object?[] args)=>parameters.Length==args.Length && parameters.Select((p,i)=>args[i]==null || p.ParameterType.IsInstanceOfType(args[i]) || p.ParameterType.IsEnum && args[i] is int).All(v=>v);
}

internal sealed class NativeParameter : ParameterInfo
{
    internal NativeParameter(NativeRuntime runtime,nint method,uint index)
    { ClassImpl=runtime.TypeFrom(runtime.At("method_get_param",method,index)); NameImpl=NativeRuntime.Text(runtime.At("method_get_param_name",method,index)); PositionImpl=(int)index; }
}

internal interface INativeMethod { NativeMethod Method {get;} }

internal sealed unsafe class NativeMethod : MethodInfo, INativeMethod
{
    internal readonly NativeType Owner;
    internal readonly nint Handle;
    internal NativeRuntime Runtime=>Owner.Runtime;
    internal nint Code=>Marshal.ReadIntPtr(Handle);
    private readonly Lazy<NativeParameter[]> parameters;
    private string? name;
    private Type? returnType;
    private MethodAttributes? attributes;
    internal NativeMethod(NativeType owner,nint handle)
    {
        Owner=owner; Handle=handle;
        parameters=new(()=>Enumerable.Range(0,checked((int)Runtime.U1("method_get_param_count",Handle))).Select(i=>new NativeParameter(Runtime,Handle,(uint)i)).ToArray());
    }
    public NativeMethod Method=>this;
    public override string Name=>name??=Runtime.Name("method_get_name",Handle);
    public override Type DeclaringType=>Owner;
    public override Type ReflectedType=>Owner;
    public override RuntimeMethodHandle MethodHandle=>throw new NotSupportedException("IL2CPP methods do not have CLR handles.");
    public override MethodAttributes Attributes { get { if(attributes is { } value)return value; uint implementation=0; return (attributes=(MethodAttributes)((delegate* unmanaged[Cdecl]<nint,uint*,uint>)Runtime.Export("method_get_flags"))(Handle,&implementation)).Value; } }
    public override Type ReturnType=>returnType??=Runtime.TypeFrom(Runtime.P1("method_get_return_type",Handle));
    public override ICustomAttributeProvider ReturnTypeCustomAttributes=>this;
    public override bool IsGenericMethod=>Runtime.B1("method_is_generic",Handle) || Runtime.B1("method_is_inflated",Handle);
    public override bool IsGenericMethodDefinition=>Runtime.B1("method_is_generic",Handle) && !Runtime.B1("method_is_inflated",Handle);
    public override bool ContainsGenericParameters=>IsGenericMethodDefinition;
    public override Type[] GetGenericArguments()=>Type.EmptyTypes;
    public override ParameterInfo[] GetParameters()=>parameters.Value.Cast<ParameterInfo>().ToArray();
    public override MethodImplAttributes GetMethodImplementationFlags()=>MethodImplAttributes.Native;
    public override MethodInfo GetBaseDefinition()=>this;
    public override object? Invoke(object? obj,BindingFlags flags,Binder? binder,object?[]? args,CultureInfo? culture)
    { args??=Array.Empty<object?>(); return Runtime.Invoke(Handle,obj,args,parameters.Value); }
    public override object[] GetCustomAttributes(bool inherit)=>NativeRuntime.Attributes;
    public override object[] GetCustomAttributes(Type type,bool inherit)=>NativeRuntime.Attributes;
    public override bool IsDefined(Type type,bool inherit)=>false;
    public override bool Equals(object? obj)=>obj is NativeMethod other && Handle==other.Handle;
    public override int GetHashCode()=>Handle.GetHashCode();
}

internal sealed class NativeConstructor : ConstructorInfo, INativeMethod
{
    public NativeMethod Method {get;}
    internal NativeConstructor(NativeMethod method)=>Method=method;
    public override string Name=>".ctor";
    public override Type DeclaringType=>Method.DeclaringType;
    public override Type ReflectedType=>DeclaringType;
    public override RuntimeMethodHandle MethodHandle=>Method.MethodHandle;
    public override MethodAttributes Attributes=>Method.Attributes;
    public override ParameterInfo[] GetParameters()=>Method.GetParameters();
    public override MethodImplAttributes GetMethodImplementationFlags()=>MethodImplAttributes.Native;
    public override object Invoke(BindingFlags flags,Binder? binder,object?[]? args,CultureInfo? culture)
    { var obj=Method.Runtime.Wrap(Method.Runtime.NewObject(Method.Owner.Klass))!; Method.Invoke(obj,args); return obj; }
    public override object? Invoke(object? obj,BindingFlags flags,Binder? binder,object?[]? args,CultureInfo? culture)=>Method.Invoke(obj,args);
    public override object[] GetCustomAttributes(bool inherit)=>NativeRuntime.Attributes;
    public override object[] GetCustomAttributes(Type type,bool inherit)=>NativeRuntime.Attributes;
    public override bool IsDefined(Type type,bool inherit)=>false;
}

internal sealed class NativeProperty : PropertyInfo
{
    private readonly NativeType owner;
    private readonly nint handle;
    private readonly Lazy<MethodInfo?> getter, setter;
    private string? name;
    internal NativeProperty(NativeType owner,nint handle)
    {
        this.owner=owner; this.handle=handle;
        getter=new(()=>Accessor("property_get_get_method")); setter=new(()=>Accessor("property_get_set_method"));
    }
    public override string Name=>name??=owner.Runtime.Name("property_get_name",handle);
    public override Type DeclaringType=>owner;
    public override Type ReflectedType=>owner;
    public override PropertyAttributes Attributes=>PropertyAttributes.None;
    public override bool CanRead=>GetGetMethod(true)!=null;
    public override bool CanWrite=>GetSetMethod(true)!=null;
    public override Type PropertyType=>GetGetMethod(true)?.ReturnType ?? GetSetMethod(true)!.GetParameters().Last().ParameterType;
    private MethodInfo? Accessor(string which) { var ptr=owner.Runtime.P1(which,handle); return ptr==0?null:new NativeMethod(owner,ptr); }
    public override MethodInfo? GetGetMethod(bool nonPublic)=>getter.Value is { } method && (nonPublic || method.IsPublic)?method:null;
    public override MethodInfo? GetSetMethod(bool nonPublic)=>setter.Value is { } method && (nonPublic || method.IsPublic)?method:null;
    public override MethodInfo[] GetAccessors(bool nonPublic)=>new[]{GetGetMethod(nonPublic),GetSetMethod(nonPublic)}.OfType<MethodInfo>().ToArray();
    public override ParameterInfo[] GetIndexParameters()=>GetGetMethod(true)?.GetParameters() ?? GetSetMethod(true)!.GetParameters()[..^1];
    public override object? GetValue(object? obj,BindingFlags flags,Binder? binder,object?[]? index,CultureInfo? culture)=>GetGetMethod(true)!.Invoke(obj,index);
    public override void SetValue(object? obj,object? value,BindingFlags flags,Binder? binder,object?[]? index,CultureInfo? culture)=>GetSetMethod(true)!.Invoke(obj,(index??Array.Empty<object?>()).Append(value).ToArray());
    public override object[] GetCustomAttributes(bool inherit)=>NativeRuntime.Attributes;
    public override object[] GetCustomAttributes(Type type,bool inherit)=>NativeRuntime.Attributes;
    public override bool IsDefined(Type type,bool inherit)=>false;
}

internal sealed unsafe class NativeField : FieldInfo
{
    private readonly NativeType owner;
    private readonly nint handle;
    private string? name;
    private Type? fieldType;
    private FieldAttributes? attributes;
    internal NativeField(NativeType owner,nint handle) { this.owner=owner; this.handle=handle; }
    public override string Name=>name??=owner.Runtime.Name("field_get_name",handle);
    public override Type DeclaringType=>owner;
    public override Type ReflectedType=>owner;
    public override FieldAttributes Attributes=>attributes??=(FieldAttributes)owner.Runtime.U1("field_get_flags",handle);
    public override Type FieldType=>fieldType??=owner.Runtime.TypeFrom(owner.Runtime.P1("field_get_type",handle));
    public override RuntimeFieldHandle FieldHandle=>throw new NotSupportedException();
    public override object? GetValue(object? obj)
    {
        using var scope=new NativeMemoryScope(); var buffer=scope.Alloc(owner.Runtime.Size(FieldType));
        if(IsStatic) ((delegate* unmanaged[Cdecl]<nint,nint,void>)owner.Runtime.Export("field_static_get_value"))(handle,buffer);
        else ((delegate* unmanaged[Cdecl]<nint,nint,nint,void>)owner.Runtime.Export("field_get_value"))(((NativeObject)obj!).Pointer,handle,buffer);
        return owner.Runtime.Read(buffer,FieldType);
    }
    public override void SetValue(object? obj,object? value,BindingFlags flags,Binder? binder,CultureInfo? culture)
    {
        using var scope=new NativeMemoryScope();
        // IL2CPP Field::SetValueRaw uses deref_pointer=false for setters: reference
        // fields take the object pointer itself, not the address of that pointer.
        nint buffer;
        if(FieldType.IsValueType) { buffer=scope.Alloc(owner.Runtime.Size(FieldType)); owner.Runtime.Write(buffer,FieldType,value,scope); }
        else buffer=owner.Runtime.Reference(value,FieldType,scope);
        if(IsStatic) ((delegate* unmanaged[Cdecl]<nint,nint,void>)owner.Runtime.Export("field_static_set_value"))(handle,buffer);
        else ((delegate* unmanaged[Cdecl]<nint,nint,nint,void>)owner.Runtime.Export("field_set_value"))(((NativeObject)obj!).Pointer,handle,buffer);
    }
    public override object[] GetCustomAttributes(bool inherit)=>NativeRuntime.Attributes;
    public override object[] GetCustomAttributes(Type type,bool inherit)=>NativeRuntime.Attributes;
    public override bool IsDefined(Type type,bool inherit)=>false;
}
