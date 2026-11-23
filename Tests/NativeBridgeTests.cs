using System.Reflection;
using System.Runtime.InteropServices;
using Gkms.Bridge;
using Gkms.Sdk;

internal static class NativeBridgeTests
{
    [UnmanagedCallersOnly]
    public static int RunFromNative(nint fixture,int unused)
    {
        try { Run(Marshal.PtrToStringUni(fixture)!); return 0; }
        catch(Exception error) { Console.Error.WriteLine(error); return 30; }
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Number();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ObjectCheck(nint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IntCheck(int value);
    private static int textPrefixes, textPostfixes;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte ReadBool();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void WriteBool(byte value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ActionCall();
    private static nint constructorInstance;
    private static bool finalized;
    private static object? completedTask;
    private static void Check(bool condition,string message) { if(!condition) throw new Exception("Native bridge: "+message); }
    private static MethodInfo Handler(string name)=>typeof(NativeBridgeTests).GetMethod(name,BindingFlags.Public|BindingFlags.Static)!;
    public static void Run(string fixture)
    {
        var library=NativeLibrary.Load(Path.GetFullPath(fixture));
        T Function<T>(string name) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
        Check(Function<Number>("fixture_init")()==0,"MinHook initialize");
        var logs=new List<string>();
        var hooks=new NativeHooks(Function<NativeHooks.CreateHook>("fixture_create"),Function<NativeHooks.ToggleHook>("fixture_enable"),Function<NativeHooks.ToggleHook>("fixture_disable"),logs.Add);
        var runtime=new NativeRuntime(library);
        var newString = typeof(NativeRuntime).GetMethod("NewString", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var wrap = typeof(NativeRuntime).GetMethod("Wrap", BindingFlags.Instance | BindingFlags.NonPublic)!;
        nint NativeString(string text) => (nint)newString.Invoke(runtime, new object[] { text })!;
        string ReadString(nint pointer) => (string)wrap.Invoke(runtime, new object[] { pointer })!;
        var rootsBeforeStrings = Function<Number>("fixture_live_handles")();
        for (var i = 0; i < 10000; i++) Check(ReadString(NativeString("原文/译文")) == "原文/译文", "plain string roundtrip");
        Check(Function<Number>("fixture_live_handles")() == rootsBeforeStrings, "string marshalling retains no provenance handles");
        var type=runtime.FindType("NativeFixture.Runtime","Fixture.Counter") ?? throw new Exception("runtime metadata lookup");
        var tmp = runtime.FindType("NativeFixture.Runtime", "TMPro.TMP_Text")!;
        var shortText = tmp.GetMethod("SetText", new[]{typeof(string)})!;
        var longText = tmp.GetMethod("SetText", new[]{typeof(string),typeof(bool)})!;
        using (hooks.Patch("text", longText, Handler(nameof(TextPrefix)), Handler(nameof(TextPostfix)), null, 400))
        using (hooks.Patch("text", shortText, Handler(nameof(TextPrefix)), Handler(nameof(TextPostfix)), null, 400))
        {
            for (var mode=0; mode<5; mode++)
            {
                var before = textPrefixes;
                Check(Function<IntCheck>("fixture_tmp_call")(mode)==7, "folded TMP translates before original and preserves both raw trailing registers, mode " + mode);
                Check(textPrefixes==before+1 && textPostfixes==textPrefixes, "folded overload registrations run once with shared prefix/postfix state");
            }
            try { using var unsupported = hooks.Patch("bool", longText, Handler(nameof(TextBool)), null, null, 400); throw new Exception("accepted non-common bool callback"); }
            catch (NotSupportedException) { }
        }
        Check(Function<IntCheck>("fixture_tmp_call")(0)==6, "folded TMP unhook preserves original Japanese write");
        using (hooks.Patch("text", shortText, Handler(nameof(TextPrefix)), Handler(nameof(TextPostfix)), null, 400))
            Check(Function<IntCheck>("fixture_tmp_call")(4)==7, "folded TMP re-enable handles null MethodInfo even with one registration");
        var many=runtime.FindType("NativeFixture.Runtime","Fixture.ManyMembers")!;
        Check(many.GetProperty("Field199")!.PropertyType==typeof(int),"field property on a large native type");
        var reads=Function<Number>("fixture_metadata_reads")();
        var allocated=GC.GetAllocatedBytesForCurrentThread();
        var lookup=System.Diagnostics.Stopwatch.StartNew();
        for(var i=0;i<1000;i++) Check(many.GetProperty("Field199")!=null,"repeated native property lookup");
        lookup.Stop();
        Console.WriteLine($"Native 200-property/200-field lookup x1000: {lookup.ElapsedMilliseconds} ms, {GC.GetAllocatedBytesForCurrentThread()-allocated} allocated bytes, {Function<Number>("fixture_metadata_reads")()-reads} metadata reads.");
        Check(Function<Number>("fixture_metadata_reads")()==reads,"warm lookups do not rescan IL2CPP metadata");
        var members=many.GetProperties(); members[0]=null!;
        Check(many.GetProperties().All(p=>p!=null),"public reflection arrays cannot corrupt cached metadata");
        Check(many.GetProperty("property199",BindingFlags.IgnoreCase|BindingFlags.Public|BindingFlags.Instance)!.CanRead &&
            many.GetProperty("Property199",BindingFlags.Public|BindingFlags.Static)==null,"cached properties preserve visibility and ignore-case lookup");
        var methodList=type.GetMethods(); methodList[0]=null!;
        var fieldList=type.GetFields(); fieldList[0]=null!;
        var constructorList=type.GetConstructors(); constructorList[0]=null!;
        var addParameters=type.GetMethod("Add")!.GetParameters(); addParameters[0]=null!;
        Check(type.GetMethods().All(m=>m!=null) && type.GetFields().All(f=>f!=null) && type.GetConstructors().All(c=>c!=null) &&
            type.GetMethod("Add")!.GetParameters()[0].ParameterType==typeof(int),"caller-owned member/parameter arrays preserve the metadata cache");
        var derived=runtime.FindType("NativeFixture.Runtime","Fixture.DerivedCounter")!;
        Check(derived.BaseType==type && derived.GetProperty("value")!=null &&
            derived.GetProperty("value",Interop.Members|BindingFlags.DeclaredOnly)==null &&
            derived.GetMethod("Add")==type.GetMethod("Add"),"metadata caches preserve inheritance and DeclaredOnly isolation");
        var internalCalls=(INativeInternalCalls)type;
        var readSrgb=Marshal.GetDelegateForFunctionPointer<ReadBool>(internalCalls.ResolveInternalCall("UnityEngine.GL::get_sRGBWrite()"));
        var writeSrgb=Marshal.GetDelegateForFunctionPointer<WriteBool>(internalCalls.ResolveInternalCall("UnityEngine.GL::set_sRGBWrite(System.Boolean)"));
        Check(readSrgb()==0,"stripped engine property resolved through native registration");
        writeSrgb(1); Check(readSrgb()==1,"native bool setter ABI"); writeSrgb(0);
        Check(internalCalls.ResolveInternalCall("Fixture::Missing()") == IntPtr.Zero,"unavailable internal call is explicit");
        var add=type.GetMethod("Add")!; var shared=type.GetMethod("Shared")!;
        var created=Function<Number>("fixture_creates")();
        foreach(var name in new[]{"FoldedShort","FoldedLong"})
        {
            try {using var invalid=hooks.Patch("folded",type.GetMethod(name)!,Handler(nameof(Noop)),null,null,400); throw new Exception("accepted incompatible folded native target");}
            catch(NotSupportedException error) {Check(error.Message.Contains("FoldedShort") && error.Message.Contains("FoldedLong"),"conflict reports both native signatures");}
        }
        Check(Function<Number>("fixture_creates")()==created,"incompatible aliases rejected before creating any native detour");
        var calls=Function<Number>("fixture_calls")();
        type.GetMethod("FoldedShort")!.Invoke(null,null); type.GetMethod("FoldedLong")!.Invoke(null,new object[]{2,3f});
        Check(Function<Number>("fixture_calls")()==calls+2,"folded native methods still execute their original code");
        Check((int)add.Invoke(null,new object[]{2,3f})! ==5,"native invocation");
        using(var patch=hooks.Patch("add",add,Handler(nameof(Add)),Handler(nameof(AfterAdd)),null,400))
        {
            Check((int)add.Invoke(null,new object[]{2,3f})! ==16,"mixed integer/float arguments and result");
            Check((int)shared.Invoke(null,new object[]{2,3f})! ==5,"shared native address keeps MethodInfo identity");
            Check(Function<Number>("fixture_add_null_method")()==9,"null MethodInfo is not a wildcard on a shared body");
        }
        Check((int)add.Invoke(null,new object[]{2,3f})! ==5,"unhook restores original native code");
        using(var patch=hooks.Patch("again",add,Handler(nameof(Skip)),null,null,400))
        { var before=Function<Number>("fixture_calls")(); Check((int)add.Invoke(null,new object[]{2,3f})! ==42 && Function<Number>("fixture_calls")()==before,"skip original"); }
        var rate=type.GetMethod("Rate")!;
        using(var patch=hooks.Patch("float",rate,Handler(nameof(Rate)),null,null,400)) Check((float)rate.Invoke(null,new object[]{1f})! ==9,"floating point ABI");
        var echo=type.GetMethod("Echo")!;
        var rootsBeforeEcho=Function<Number>("fixture_live_handles")();
        using(var patch=hooks.Patch("string",echo,Handler(nameof(Echo)),null,null,400)) Check((string)echo.Invoke(null,new object[]{"原文"})! =="译文","UTF-16 strings");
        Check(Function<Number>("fixture_invalid_handles")()==0,"Unity 6 GC handles retain all 64 bits when released");
        Check(Function<Number>("fixture_live_handles")()==rootsBeforeEcho,"temporary string handles are freed after callbacks and invocation");
        var reference=type.GetMethod("Ref")!;
        using(var patch=hooks.Patch("ref",reference,Handler(nameof(Ref)),null,null,400))
        { object?[] values={1}; reference.Invoke(null,values); Check((int)values[0]! ==12,"native by-reference writeback"); }
        var constructor=type.GetConstructor(new[]{typeof(int)})!;
        var beforeAlloc=Function<Number>("fixture_allocations")();
        object instance;
        using(var patch=hooks.Patch("ctor",constructor,Handler(nameof(Constructor)),Handler(nameof(AfterConstructor)),null,400)) instance=constructor.Invoke(new object[]{7});
        Check(((INativeObject)instance).Pointer==constructorInstance && Function<Number>("fixture_allocations")()==beforeAlloc+1,"constructor initializes the original object once");
        Check((int)type.GetField("value")!.GetValue(instance)! ==8,"instance field read");
        type.GetField("value")!.SetValue(instance,12);
        Check((int)type.GetMethod("Instance")!.Invoke(instance,new object[]{3})! ==15,"instance calls and field write");
        var platform=type.GetField("platform")!;
        platform.SetValue(instance,"iOS");
        Check(Function<ObjectCheck>("fixture_platform_valid")(Interop.PointerOf(instance))==1,"reference field stores the string object, not a temporary pointer buffer");
        Check((string)platform.GetValue(instance)! =="iOS","platform header string round trip");
        platform.SetValue(instance,null);
        Check(platform.GetValue(instance)==null,"null reference field");
        var staticPlatform=type.GetField("staticPlatform")!;
        staticPlatform.SetValue(null,"Dmm");
        Check(Function<ObjectCheck>("fixture_platform_valid")(0)==1 && (string)staticPlatform.GetValue(null)! =="Dmm","static reference field ABI");
        var vectorType=runtime.FindType("NativeFixture.Runtime","Fixture.Vector")!;
        var vector=Interop.New(vectorType)!;
        vectorType.GetProperty("x")!.SetValue(vector,1f); vectorType.GetField("y")!.SetValue(vector,2f); vectorType.GetField("z")!.SetValue(vector,3f);
        var method=type.GetMethod("Vector")!;
        using(var patch=hooks.Patch("struct",method,null,Handler(nameof(Vector)),null,400))
        {
            var result=method.Invoke(null,new[]{vector})!;
            Check((float)vectorType.GetField("x")!.GetValue(result)! ==20 && (float)vectorType.GetField("z")!.GetValue(result)! ==6,"12-byte struct argument and return ABI");
        }
        using(var patch=hooks.Patch("failure",add,Handler(nameof(Throw)),null,Handler(nameof(FinalizeCall)),400))
            Check((int)add.Invoke(null,new object[]{2,3f})! ==5 && finalized,"callback failure runs finalizer and falls back to original");
        calls=Function<Number>("fixture_calls")();
        using(var patch=hooks.Patch("skip-post-error",add,Handler(nameof(Skip)),Handler(nameof(Throw)),null,400))
            Check((int)add.Invoke(null,new object[]{2,3f})! ==42,"postfix failure preserves skipped original and replacement result");
        using(var patch=hooks.Patch("skip-final-error",add,Handler(nameof(Skip)),null,Handler(nameof(Throw)),400))
            Check((int)add.Invoke(null,new object[]{2,3f})! ==42,"finalizer failure preserves skipped original");
        using(var patch=hooks.Patch("replacement-error",add,Handler(nameof(ReplacementThrows)),null,null,400))
            add.Invoke(null,new object[]{2,3f});
        Check(Function<Number>("fixture_calls")()==calls,"replacement side effects followed by exceptions never execute original again");
        Check(logs.Count(line => line.Contains("Native hook"))==4 && logs.Count(line => line.Contains("fixture failure"))==3 &&
            logs.Any(line => line.Contains("replacement callback failure")),"each injected callback failure is logged once");
        var orientation=type.GetMethod("Orientation")!;
        var enumType=orientation.ReturnType;
        Check(enumType.IsEnum && Enum.GetNames(enumType).SequenceEqual(new[]{"Portrait","Landscape"}),"native enum values");
        using(var patch=hooks.Patch("enum",orientation,Handler(nameof(EnumValue)).MakeGenericMethod(Interop.CallbackType(enumType)),null,null,400))
            Check(Convert.ToInt32(orientation.Invoke(null,new[]{Enum.Parse(enumType,"Portrait")}))==3,"generic enum callback");
        var taskMethod=type.GetMethod("Task")!;
        var taskType=taskMethod.ReturnType;
        Check(taskType.IsGenericType && taskType.GetGenericTypeDefinition().FullName=="Fixture.Task`1" && taskType.GetGenericArguments().Single()==type,"runtime generic type metadata");
        completedTask=taskType.GetConstructor(new[]{type})!.Invoke(new[]{instance});
        var secondWrapper = taskType.GetProperty("Result")!.GetValue(completedTask)!;
        Check(!ReferenceEquals(instance, secondWrapper) && instance.Equals(secondWrapper), "native identity survives wrapper recreation");
        var increment = type.GetMethod("Instance")!;
        using (var observe = hooks.Patch("observe", increment, Handler(nameof(Observe)), null, null, 1000))
        using (var feature = hooks.Patch("feature", increment, Handler(nameof(ChangeIncrement)), null, null, 400))
        {
            increment.Invoke(instance, new object[] { 2 });
            Check((int)LiveGameState.Calls("Instance").Single().Arguments[0] == 2, "observer records native arguments before the feature changes them");
            increment.Invoke(secondWrapper, new object[] { 3 });
            using (LiveGameState.Replay()) increment.Invoke(instance, new object[] { 7 });
            Check(LiveGameState.Calls("Instance").Length == 1 && (int)LiveGameState.Calls("Instance")[0].Arguments[0] == 3,
                "native wrapper identity and replay suppression preserve the game's original request");
        }
        LiveGameState.Clear();
        var lyricsType = typeof(Gkms.Plugins.LiveUnlock.Plugin).Assembly.GetType("Gkms.Plugins.LiveUnlock.LiveLyrics")!;
        var firstCall = new object[] { instance, false }; var nextCall = new object[] { secondWrapper, false };
        lyricsType.GetMethod("ShowInitially")!.Invoke(null, firstCall);
        lyricsType.GetMethod("ShowInitially")!.Invoke(null, nextCall);
        Check((bool)firstCall[1] && !(bool)nextCall[1], "lyrics initial state uses native identity instead of wrapper identity");
        ((IDisposable)Activator.CreateInstance(lyricsType, nonPublic: true)!).Dispose();
        var bytes = type.GetMethod("Bytes")!.Invoke(null, null)!;
        Check(bytes is not IEnumerable<byte> && Interop.Items(bytes).Select(Convert.ToByte).SequenceEqual(new byte[] { 137,80,78,71 }),
            "PNG bytes can be read from the actual native array bridge");
        var pixels = new byte[16 * 1024 * 1024]; new Random(417).NextBytes(pixels);
        var transfer = System.Diagnostics.Stopwatch.StartNew();
        var copied = Interop.ReadBytes(type.GetMethod("EchoBytes")!.Invoke(null, new object[] { pixels }));
        transfer.Stop();
        Check(copied.SequenceEqual(pixels) && Interop.ReadBytes(type.GetMethod("EchoBytes")!.Invoke(null, new object[] { Array.Empty<byte>() })).Length == 0,
            "bulk native texture bytes preserve all data and empty arrays");
        Console.WriteLine($"Native 16 MiB byte-array roundtrip: {transfer.ElapsedMilliseconds} ms.");
        using(var patch=hooks.Patch("task",taskMethod,Handler(nameof(TaskResult)).MakeGenericMethod(Interop.CallbackType(taskType)),null,null,400))
        {
            var result=taskMethod.Invoke(null,new[]{instance})!;
            Check(Interop.PointerOf(taskType.GetProperty("Result")!.GetValue(result)!)==Interop.PointerOf(instance),"constructed generic 24-byte return value");
        }
        GC.Collect(); GC.WaitForPendingFinalizers();
        Check(Function<Number>("fixture_invalid_handles")()==0,"Unity 6 GC handle lookup, scoped release and finalizer release");
        var cross=type.GetMethod("CrossBase")!;
        created=Function<Number>("fixture_creates")();
        try {using var invalid=hooks.Patch("cross-class",cross,Handler(nameof(Skip)),null,null,400); throw new Exception("accepted cross-class folded body");}
        catch(NotSupportedException error)
        {
            Check(error.Message.Contains("CrossAlias"),"cross-class conflict discovered without registering that alias");
        }
        Check(Function<Number>("fixture_creates")()==created && (int)cross.Invoke(null,new object[]{2,3f})! ==12,"cross-class conflict never installs a detour and preserves original call");
        try {using var invalid=hooks.Patch("retry",cross,Handler(nameof(Skip)),null,null,400); throw new Exception("reactivated incompatible shared target");}
        catch(NotSupportedException) { }
        try {using var invalid=hooks.Patch("initialize",type.GetMethod("Initialize")!,Handler(nameof(Noop)),null,null,400); throw new Exception("accepted foreign non-object call on Initialize");}
        catch(NotSupportedException error) {Check(error.Message.Contains("ForeignInitialize"),"Initialize reports the cross-class alias");}
        Check(Function<Number>("fixture_creates")()==created && Function<Number>("fixture_foreign_initialize")()==1,
            "startup crash regression: foreign call with address 1 and null MethodInfo never enters object wrapping");
        DmmParityTests.IcallCache();
        Console.WriteLine("Native bridge PASS: live metadata, MinHook, shared targets, 64-bit GC handles, float/struct/string/ref ABI, enums, generic returns, constructors and disable/re-enable.");
        // The library stays loaded while native object finalizers and inert MinHook trampolines can reference it.
    }
    public static void Add(ref int __0)=>__0=5;
    public static void Noop() { }
    public static void TextPrefix(ref string __0, out string __state) { textPrefixes++; __state=__0; if(__0=="マニー") __0="金币"; }
    public static void TextPostfix(string __state) { Check(__state=="マニー", "folded text state"); textPostfixes++; }
    public static void TextBool(ref bool __1) => __1 = !__1;
    public static void Observe(object __instance, object[] __args, MethodBase __originalMethod) => LiveGameState.Record(__instance, (MethodInfo)__originalMethod, __args);
    public static void ChangeIncrement(ref int __0) => __0 = 9;
    public static void AfterAdd(ref int __result)=>__result*=2;
    public static bool Skip(ref int __result) { __result=42; return false; }
    public static void Rate(ref float __0)=>__0=4.5f;
    public static void Echo(ref string __0)=>__0="译文";
    public static void Ref(ref int __0)=>__0=9;
    public static void Constructor(object __instance,ref int __0,out int __state) { constructorInstance=((INativeObject)__instance).Pointer; __state=__0; __0++; }
    public static void AfterConstructor(object __instance,int __state)=>Check(((INativeObject)__instance).Pointer==constructorInstance && __state==7,"constructor callback state");
    public static void Vector(object __result)=>Interop.TypeOf(__result).GetField("x")!.SetValue(__result,20f);
    public static void Throw()=>throw new InvalidOperationException("fixture failure");
    public static bool ReplacementThrows() { HookExecution.MarkReplacement(); throw new InvalidOperationException("replacement callback failure"); }
    public static void FinalizeCall(Exception __exception)=>finalized=__exception.Message=="fixture failure";
    public static bool EnumValue<T>(ref T __result) where T:struct,Enum { __result=(T)Enum.ToObject(typeof(T),3); return false; }
    public static bool TaskResult<T>(ref T __result) { __result=(T)completedTask!; return false; }
}
