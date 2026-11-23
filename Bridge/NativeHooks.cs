using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Gkms.Sdk;

namespace Gkms.Bridge;

// The trampoline is installed by the original DMM MinHook backend. CLR delegates only marshal the Windows x64 ABI.
public sealed class NativeHooks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int CreateHook(nint target,nint detour,out nint original);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int ToggleHook(nint target);
    private readonly CreateHook create;
    private readonly ToggleHook enable,disable;
    private readonly Dictionary<nint,HookSite> sites=new();
    private readonly Dictionary<nint,string> incompatibleSites=new();
    private readonly HashSet<(nint Class,nint Code)> checkedAliases=new();
    private readonly HashSet<nint> sharedSites=new();
    private readonly HashSet<nint> foldedTextSites=new();
    private readonly object sync=new();
    private readonly Action<string> log;
    private long sequence;
    public NativeHooks(CreateHook create,ToggleHook enable,ToggleHook disable,Action<string> log)
    { this.create=create; this.enable=enable; this.disable=disable; this.log=log; }
    public IDisposable Patch(string owner,MethodBase target,MethodInfo? prefix,MethodInfo? postfix,MethodInfo? finalizer,int priority)
    {
        if(target is not INativeMethod native) throw new ArgumentException("Target must be a live IL2CPP method.",nameof(target));
        var method=native.Method;
        if(method.Code==0 || method.ContainsGenericParameters) throw new NotSupportedException("Method has no compiled native implementation: "+target);
        foreach(var callback in new[]{prefix,postfix,finalizer}.OfType<MethodInfo>()) Validate(callback,method);
        lock(sync)
        {
            if(incompatibleSites.TryGetValue(method.Code,out var reason)) throw new NotSupportedException(reason);
            sites.TryGetValue(method.Code,out var site);
            var foldedText = foldedTextSites.Contains(method.Code);
            try
            {
                // IL2CPP/linker folding can give different overloads the same address.
                // Check aliases across all image classes before installing the first detour;
                // an unregistered overload can still call that address at any time.
                if(!checkedAliases.Contains((method.Owner.Klass,method.Code)))
                {
                    var aliases = method.Runtime.Aliases(method);
                    if (aliases.Length != 0) sharedSites.Add(method.Code);
                    foldedText = HookSite.IsFoldedTextPair(method, aliases);
                    if (foldedText) foldedTextSites.Add(method.Code);
                    if (!foldedText)
                        foreach(var alias in aliases)
                            HookSite.CheckSignature(method,alias);
                    checkedAliases.Add((method.Owner.Klass,method.Code));
                }
                site?.CheckSignature(method);
            }
            catch(NotSupportedException error)
            {
                // Also retire a site first encountered through a different declaring class.
                if(site?.Count>0)
                {
                    var result=disable(method.Code);
                    if(result!=0) throw new InvalidOperationException($"MinHook disable incompatible {method.Name}: {result}",error);
                }
                incompatibleSites[method.Code]=error.Message;
                throw;
            }
            if(site==null)
            {
                site=new HookSite(method,log,foldedText);
                var result=create(method.Code,Marshal.GetFunctionPointerForDelegate(site.Detour),out var original);
                if(result!=0) throw new InvalidOperationException($"MinHook create {method.Name}: {result}");
                site.SetOriginal(original); sites.Add(method.Code,site);
                if (foldedText) log("TMP SetText 共享入口已接入：Windows x64 公共字符串参数适配，R8/R9 原样保留；每次原生调用只执行一次同组回调。");
            }
            site.ValidateCallbacks(prefix, postfix, finalizer);
            site.AllowUnspecifiedMethod = !sharedSites.Contains(method.Code);
            var registration=new Registration(this,site,method,target,owner,prefix,postfix,finalizer,priority,++sequence);
            site.Add(registration);
            if(site.Count==1)
            {
                var result=enable(method.Code);
                if(result!=0) { site.Remove(registration); throw new InvalidOperationException($"MinHook enable {method.Name}: {result}"); }
            }
            return registration;
        }
    }
    private static void Validate(MethodInfo callback,NativeMethod method)
    {
        if(!callback.IsStatic || callback.ContainsGenericParameters) throw new ArgumentException("Callbacks must be closed static methods.");
        var args=method.GetParameters();
        foreach(var parameter in callback.GetParameters())
        {
            var name=parameter.Name??"";
            if(name is "__instance" or "__result" or "__state" or "__originalMethod" or "__exception" or "__args" or "__runOriginal") continue;
            var index=name.StartsWith("__") && int.TryParse(name[2..],out var number)?number:Array.FindIndex(args,p=>p.Name==name);
            if(index<0 || index>=args.Length) throw new ArgumentException($"Unknown callback parameter {name} on {method.Name}.");
        }
    }
    private void Remove(Registration registration)
    {
        lock(sync)
        {
            registration.Site.Remove(registration);
            if(registration.Site.Count==0 && !incompatibleSites.ContainsKey(registration.Method.Code))
            {
                var result=disable(registration.Method.Code);
                if(result!=0) log($"MinHook disable {registration.Method.Name}: {result}");
            }
            // Keep delegates and original trampolines alive for in-flight calls. Disabling restores game code;
            // retaining inert sites also allows re-enabling while another plugin callback is still returning.
        }
    }
    private sealed class Registration : IDisposable
    {
        private NativeHooks? manager;
        public readonly HookSite Site;
        public readonly NativeMethod Method;
        public readonly MethodBase Target;
        public readonly string Owner;
        public readonly MethodInfo? Prefix,Postfix,Finalizer;
        public readonly int Priority;
        public readonly long Sequence;
        public Registration(NativeHooks manager,HookSite site,NativeMethod method,MethodBase target,string owner,MethodInfo? prefix,MethodInfo? postfix,MethodInfo? finalizer,int priority,long sequence)
        { this.manager=manager; Site=site; Method=method; Target=target; Owner=owner; Prefix=prefix; Postfix=postfix; Finalizer=finalizer; Priority=priority; Sequence=sequence; }
        public void Dispose()=>Interlocked.Exchange(ref manager,null)?.Remove(this);
    }
    private sealed class HookSite
    {
        private readonly NativeMethod method;
        private readonly NativeRuntime runtime;
        private readonly Type[] parameters;
        private readonly Type result;
        private readonly Type[] abi;
        private readonly Type abiResult,delegateType;
        private readonly Action<string> log;
        private readonly bool foldedText;
        private Delegate original=null!;
        private Registration[] registrations=Array.Empty<Registration>();
        public Delegate Detour {get;}
        public bool AllowUnspecifiedMethod { get; set; }
        public int Count=>Volatile.Read(ref registrations).Length;
        public HookSite(NativeMethod method,Action<string> log,bool foldedText = false)
        {
            this.method=method; runtime=method.Runtime; this.log=log; this.foldedText=foldedText;
            parameters=foldedText ? new[]{typeof(string)} : method.GetParameters().Select(p=>p.ParameterType).ToArray(); result=method.ReturnType;
            // These two x64 overloads share (this, string), but R8 is either MethodInfo*
            // or bool and R9 is either unspecified or MethodInfo*. Keep BOTH raw words.
            // Never marshal R8 as bool or manufacture/move a MethodInfo argument.
            abi=foldedText ? new[]{typeof(nint),typeof(nint),typeof(nint),typeof(nint)} : Signature(method); abiResult=AbiType(result,runtime);
            delegateType=DelegateTypes.Create(abiResult,abi);
            var thunk=new DynamicMethod("Dmm_"+method.Name,abiResult,new[]{typeof(HookSite)}.Concat(abi).ToArray(),typeof(NativeHooks),true);
            var il=thunk.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4,abi.Length); il.Emit(OpCodes.Newarr,typeof(object));
            for(int i=0;i<abi.Length;i++) { il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4,i); il.Emit(OpCodes.Ldarg,i+1); il.Emit(OpCodes.Box,abi[i]); il.Emit(OpCodes.Stelem_Ref); }
            il.Emit(OpCodes.Call,typeof(HookSite).GetMethod(nameof(Invoke),BindingFlags.Instance|BindingFlags.Public)!);
            if(abiResult==typeof(void)) il.Emit(OpCodes.Pop); else il.Emit(OpCodes.Unbox_Any,abiResult);
            il.Emit(OpCodes.Ret); Detour=thunk.CreateDelegate(delegateType,this);
        }
        private static Type[] Signature(NativeMethod method)=>(method.IsStatic?Array.Empty<Type>():new[]{typeof(nint)})
            .Concat(method.GetParameters().Select(p=>AbiType(p.ParameterType,method.Runtime))).Append(typeof(nint)).ToArray();
        public void CheckSignature(NativeMethod other)
        {
            if (!foldedText || !IsTextOverload(other)) CheckSignature(method,other);
        }
        private static bool IsTextOverload(NativeMethod value) =>
            !value.IsStatic && !value.Owner.IsValueType && !value.ContainsGenericParameters &&
            value.Owner.FullName == "TMPro.TMP_Text" && value.Name == "SetText" && value.ReturnType == typeof(void) &&
            (value.GetParameters().Select(p=>p.ParameterType).SequenceEqual(new[]{typeof(string)}) ||
             value.GetParameters().Select(p=>p.ParameterType).SequenceEqual(new[]{typeof(string),typeof(bool)}));
        public static bool IsFoldedTextPair(NativeMethod method, NativeMethod[] aliases) =>
            OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64 &&
            IsTextOverload(method) && aliases.Length == 1 && IsTextOverload(aliases[0]) &&
            method.Owner.Klass == aliases[0].Owner.Klass &&
            method.GetParameters().Length != aliases[0].GetParameters().Length;
        public void ValidateCallbacks(params MethodInfo?[] callbacks)
        {
            if (!foldedText) return;
            foreach (var callback in callbacks.OfType<MethodInfo>())
                foreach (var parameter in callback.GetParameters())
                    if (parameter.Name is not ("__instance" or "__0" or "sourceText" or "__state" or "__originalMethod" or "__exception" or "__runOriginal"))
                        throw new NotSupportedException("Folded TMP SetText callbacks may only access the common instance/string prefix: " + parameter.Name);
        }
        public static void CheckSignature(NativeMethod first,NativeMethod other)
        {
            if(first.IsStatic != other.IsStatic || !first.IsStatic && (first.Owner.IsValueType != other.Owner.IsValueType ||
                first.Owner.IsValueType && first.Owner.Klass != other.Owner.Klass) ||
                !Signature(first).SequenceEqual(Signature(other)) || !Compatible(first.ReturnType,other.ReturnType) ||
                !first.GetParameters().Select(p=>p.ParameterType).Zip(other.GetParameters().Select(p=>p.ParameterType),Compatible).All(x=>x))
                throw new NotSupportedException($"Shared native code has incompatible IL2CPP signatures at 0x{first.Code:X}: {Describe(first)} / {Describe(other)}");
            static string Describe(NativeMethod value)=>$"{value.Owner.FullName}.{value.Name}({string.Join(", ",value.GetParameters().Select(p=>p.ParameterType.FullName))})";
            static bool Compatible(Type a,Type b) => a==b || a.IsByRef && b.IsByRef && Compatible(a.GetElementType()!,b.GetElementType()!) ||
                !a.IsByRef && !b.IsByRef && !a.IsValueType && !b.IsValueType;
        }
        public void SetOriginal(nint pointer)=>original=Marshal.GetDelegateForFunctionPointer(pointer,delegateType);
        public void Add(Registration registration)=>Volatile.Write(ref registrations,registrations.Append(registration).ToArray());
        public void Remove(Registration registration)=>Volatile.Write(ref registrations,registrations.Where(r=>r!=registration).ToArray());
        public object? Invoke(object?[] raw)
        {
            try { return InvokeCore(raw); }
            catch(Exception error)
            {
                // CLR exceptions must never escape a reverse P/Invoke callback into the game.
                try { log($"Native callback boundary {method.Name}: {error}"); } catch { }
                return abiResult==typeof(void)?null:Activator.CreateInstance(abiResult);
            }
        }
        private object? InvokeCore(object?[] raw)
        {
            var actual=(nint)raw[^1]!;
            // Null is not a wildcard on folded/shared code. In particular, do not
            // inspect raw[0] as an object before establishing the called method.
            var registered = Volatile.Read(ref registrations);
            var active=registered.Where(r=>foldedText || actual==r.Method.Handle || actual==0 && AllowUnspecifiedMethod).OrderByDescending(r=>r.Priority).ThenBy(r=>r.Sequence).ToArray();
            if (foldedText)
            {
                // With a null MethodInfo the overload is unknowable, but both calls have
                // the same text semantics. Run each common callback once, not twice.
                var identified = registered.FirstOrDefault(r =>
                    r.Method.GetParameters().Length == 1 ? (nint)raw[2]! == r.Method.Handle : actual == r.Method.Handle);
                active = active.GroupBy(r => (r.Owner, r.Prefix, r.Postfix, r.Finalizer, r.Priority))
                    .Select(group => group.FirstOrDefault(r => r.Method.Handle == identified?.Method.Handle) ?? group.First()).ToArray();
            }
            if(active.Length==0) return original.DynamicInvoke(raw);
            using var execution = Gkms.Sdk.HookExecution.Begin();
            using var memory=new NativeMemoryScope();
            var offset=method.IsStatic?0:1;
            object? instance=null;
            if(offset!=0)
            {
                var pointer=(nint)raw[0]!;
                // Value-type method pointers take unboxed this; constructors must not allocate a second instance.
                instance=method.Owner.IsValueType?runtime.Wrap(runtime.P2("value_box",method.Owner.Klass,pointer)):runtime.Wrap(pointer);
            }
            var args=parameters.Select((p,i)=>FromRaw(raw[i+offset],p,memory)).ToArray();
            object? returned=result==typeof(void)?null:Default(result,memory);
            Exception? error=null; bool run=true,called=false;
            var states=new Dictionary<Registration,object?>();
            try
            {
                foreach(var item in active) if(item.Prefix!=null)
                {
                    var decision=Callback(item,item.Prefix,instance,args,ref returned,ref error,states,run);
                    if(decision is false) run=false;
                }
                for(int i=0;i<parameters.Length;i++)
                {
                    if(parameters[i].IsByRef) runtime.Write((nint)raw[i+offset]!,parameters[i].GetElementType()!,args[i],memory);
                    else raw[i+offset]=ToRaw(args[i],parameters[i],memory);
                }
                if(offset!=0 && method.Owner.IsValueType && instance is NativeObject before)
                    runtime.Write((nint)raw[0]!,method.Owner,before,memory);
                if(run)
                {
                    called=true; var native=original.DynamicInvoke(raw);
                    if(result!=typeof(void)) returned=FromRaw(native,result,memory);
                    if(offset!=0 && method.Owner.IsValueType && instance is NativeObject after)
                        runtime.CopyValueInto(after,(nint)raw[0]!);
                    for(int i=0;i<parameters.Length;i++) if(parameters[i].IsByRef) args[i]=FromRaw(raw[i+offset],parameters[i],memory);
                }
                foreach(var item in active.Reverse()) if(item.Postfix!=null) Callback(item,item.Postfix,instance,args,ref returned,ref error,states,run);
            }
            catch(Exception exception) { error=exception.GetBaseException(); }
            // Finish any permitted fallback before finalizers inspect the actual write result.
            if(error!=null && !called && run && !execution.ReplacementStarted)
            {
                try
                {
                    called=true;
                    var native=original.DynamicInvoke(raw);
                    if(result!=typeof(void)) returned=FromRaw(native,result,memory);
                    for(int i=0;i<parameters.Length;i++) if(parameters[i].IsByRef) args[i]=FromRaw(raw[i+offset],parameters[i],memory);
                }
                catch(Exception exception) { error=exception.GetBaseException(); }
            }
            foreach(var item in active.Reverse()) if(item.Finalizer!=null)
            {
                try { var decision=Callback(item,item.Finalizer,instance,args,ref returned,ref error,states,run); if(typeof(Exception).IsAssignableFrom(item.Finalizer.ReturnType)) error=(Exception?)decision; }
                catch(Exception exception) { error=exception.GetBaseException(); }
            }
            if(error!=null)
            {
                log($"Native hook {method.Owner.FullName}.{method.Name}: {error}");
            }
            for(int i=0;i<parameters.Length;i++) if(parameters[i].IsByRef) runtime.Write((nint)raw[i+offset]!,parameters[i].GetElementType()!,args[i],memory);
            if(offset!=0 && method.Owner.IsValueType && instance is NativeObject boxed)
                runtime.Write((nint)raw[0]!,method.Owner,boxed,memory);
            return result==typeof(void)?null:ToRaw(returned,result,memory);
        }
        private object? Callback(Registration registration,MethodInfo callback,object? instance,object?[] args,ref object? result,ref Exception? error,Dictionary<Registration,object?> states,bool run)
        {
            var formal=callback.GetParameters(); var values=new object?[formal.Length]; var names=registration.Method.GetParameters();
            for(int i=0;i<formal.Length;i++)
            {
                var name=formal[i].Name!;
                values[i]=name switch { "__instance"=>instance,"__result"=>result,"__state"=>states.GetValueOrDefault(registration),"__exception"=>error,
                    "__originalMethod"=>registration.Target,"__args"=>args,"__runOriginal"=>run,_=>args[Index(name,names)] };
                var type=formal[i].ParameterType; if(type.IsByRef) type=type.GetElementType()!;
                if(values[i]==null && type.IsValueType) values[i]=Activator.CreateInstance(type);
            }
            var returned=callback.Invoke(null,values);
            for(int i=0;i<formal.Length;i++) if(formal[i].ParameterType.IsByRef)
            {
                var name=formal[i].Name!;
                switch(name) { case "__result":result=values[i];break; case "__state":states[registration]=values[i];break; case "__exception":error=(Exception?)values[i];break;
                    default: if(!name.StartsWith("__") || int.TryParse(name[2..],out _)) args[Index(name,names)]=values[i]; break; }
            }
            return returned;
        }
        private static int Index(string name,ParameterInfo[] parameters)=>name.StartsWith("__") && int.TryParse(name[2..],out var index)?index:Array.FindIndex(parameters,p=>p.Name==name);
        private object? Default(Type type,NativeMemoryScope memory)=>type.IsValueType?runtime.Read(memory.Alloc(runtime.Size(type)),type):null;
        private object? FromRaw(object? value,Type type,NativeMemoryScope memory)
        {
            if(type.IsByRef) return runtime.Read((nint)value!,type.GetElementType()!);
            if(!type.IsValueType) return runtime.Wrap((nint)value!);
            if(type is NativeType) { var data=memory.Alloc(runtime.Size(type)); Marshal.StructureToPtr(value!,data,false); return runtime.Read(data,type); }
            if(type.IsEnum) return Enum.ToObject(type,value!);
            if(type==typeof(bool)) return (byte)value! != 0;
            if(type==typeof(char)) return (char)(ushort)value!;
            return value;
        }
        private object? ToRaw(object? value,Type type,NativeMemoryScope memory)
        {
            if(!type.IsValueType) return runtime.Reference(value,type,memory);
            if(type is NativeType) { var data=memory.Alloc(runtime.Size(type)); runtime.Write(data,type,value,memory); return Marshal.PtrToStructure(data,AbiType(type,runtime)); }
            if(type.IsEnum) return Convert.ChangeType(value,Enum.GetUnderlyingType(type));
            if(type==typeof(bool)) return Convert.ToBoolean(value)?(byte)1:(byte)0;
            if(type==typeof(char)) return (ushort)Convert.ToChar(value);
            return value;
        }
    }
    private static Type AbiType(Type type,NativeRuntime runtime)=>type==typeof(void)?type:type.IsByRef || !type.IsValueType?typeof(nint):
        type is NativeType?DelegateTypes.Blob(runtime.Size(type)):type.IsEnum?Enum.GetUnderlyingType(type):type==typeof(bool)?typeof(byte):type==typeof(char)?typeof(ushort):type;
    private static class DelegateTypes
    {
        private static readonly ModuleBuilder module=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Gkms.NativeAbi"),AssemblyBuilderAccess.Run).DefineDynamicModule("ABI");
        private static readonly Dictionary<int,Type> blobs=new();
        private static int serial;
        public static Type Blob(int size)
        {
            lock(module)
            {
                if(blobs.TryGetValue(size,out var type)) return type;
                var builder=module.DefineType("Blob"+size,TypeAttributes.Public|TypeAttributes.Sealed|TypeAttributes.ExplicitLayout,typeof(ValueType),PackingSize.Size1,size);
                // Native Windows x64 passes structs of 1/2/4/8 bytes in a register, all other sizes indirectly.
                for(int offset=0;offset<size;offset++) builder.DefineField("b"+offset,typeof(byte),FieldAttributes.Public).SetOffset(offset);
                return blobs[size]=builder.CreateTypeInfo()!.AsType();
            }
        }
        public static Type Create(Type result,Type[] args)
        {
            lock(module)
            {
                var type=module.DefineType("Call"+ ++serial,TypeAttributes.Public|TypeAttributes.Sealed,typeof(MulticastDelegate));
                type.SetCustomAttribute(new CustomAttributeBuilder(typeof(UnmanagedFunctionPointerAttribute).GetConstructor(new[]{typeof(CallingConvention)})!,new object[]{CallingConvention.Cdecl}));
                type.DefineConstructor(MethodAttributes.Public|MethodAttributes.RTSpecialName,CallingConventions.Standard,new[]{typeof(object),typeof(nint)}).SetImplementationFlags(MethodImplAttributes.Runtime|MethodImplAttributes.Managed);
                type.DefineMethod("Invoke",MethodAttributes.Public|MethodAttributes.Virtual|MethodAttributes.NewSlot, result,args).SetImplementationFlags(MethodImplAttributes.Runtime|MethodImplAttributes.Managed);
                return type.CreateTypeInfo()!.AsType();
            }
        }
    }
}
