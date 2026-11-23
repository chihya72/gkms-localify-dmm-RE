// A small, executable IL2CPP-API fixture. This DLL never loads a game or launcher.
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <cwchar>
#include <vector>
#include <map>
#include <mutex>
#include <string>
#include "MinHook.h"
#define API extern "C" __declspec(dllexport)
static bool srgbWrite = false;
static bool GetSrgbWrite() { return srgbWrite; }
static void SetSrgbWrite(bool value) { srgbWrite = value; }
API void* il2cpp_resolve_icall(const char* name)
{
    if (!strcmp(name, "UnityEngine.GL::get_sRGBWrite()")) return reinterpret_cast<void*>(&GetSrgbWrite);
    if (!strcmp(name, "UnityEngine.GL::set_sRGBWrite(System.Boolean)")) return reinterpret_cast<void*>(&SetSrgbWrite);
    return nullptr;
}
struct Class;
struct Type { Class* klass; bool byref=false; };
struct Method { void* code; const char* name; Class* owner; Type* result; std::vector<Type*> args; unsigned flags=0x16; };
struct Field { const char* name; Type* type; int offset; unsigned flags=6; };
struct Property { const char* name; Method* getter; Method* setter; };
struct Class { const char* ns; const char* name; int size; bool value; std::vector<Method*> methods; std::vector<Field*> fields; std::vector<Property*> properties; Class* parent=nullptr; };
struct Object { Class* klass; void* monitor; };
struct Vec { float x,y,z; };
struct Counter { Object object; int value; void* platform; };
struct String { Object object; int length; wchar_t chars[1]; };
Class cVoid{"System","Void",0,true},cInt{"System","Int32",4,true},cFloat{"System","Single",4,true},cString{"System","String",8,false};
Class cVec{"Fixture","Vector",12,true},cCounter{"Fixture","Counter",16,false};
Class cEnum{"Fixture","Orientation",4,true},cRuntimeType{"System","RuntimeType",8,false},cTypeArray{"System","RuntimeType[]",8,false};
Class cTask{"Fixture","Task`1",24,true},cTaskDefinition{"Fixture","Task`1",24,true};
Class cAlias{"Fixture","OtherClass",8,false};
Class cMany{"Fixture","ManyMembers",8,false};
Class cDerived{"Fixture","DerivedCounter",16,false,{},{},{},&cCounter};
Class cTmp{"TMPro","TMP_Text",8,false},cBool{"System","Boolean",1,true};
Class cByte{"System","Byte",1,true},cByteArray{"System","Byte[]",8,false};
Type tByteArray{&cByteArray};
struct ByteArray { Object object; void* bounds; size_t length; uint8_t values[4]; };
__declspec(noinline) ByteArray* Bytes(Method*) { static ByteArray bytes{{&cByteArray,nullptr},nullptr,4,{137,80,78,71}}; return &bytes; }
Method mBytes{reinterpret_cast<void*>(Bytes),"Bytes",&cCounter,&tByteArray,{}};
__declspec(noinline) ByteArray* EchoBytes(ByteArray* bytes, Method*) { return bytes; }
Method mEchoBytes{reinterpret_cast<void*>(EchoBytes),"EchoBytes",&cCounter,&tByteArray,{&tByteArray}};
Type tVoid{&cVoid},tInt{&cInt},tFloat{&cFloat},tString{&cString},tVec{&cVec},tCounter{&cCounter},tRefInt{&cInt,true};
Type tEnum{&cEnum},tRuntimeType{&cRuntimeType},tTypeArray{&cTypeArray},tTask{&cTask};
Type tBool{&cBool};
static uintptr_t textR8, textR9;
static int textCalls;
static std::wstring writtenText;
__declspec(noinline) void FoldedText(Object*, String* value, uintptr_t r8, uintptr_t r9) {
    ++textCalls; textR8=r8; textR9=r9;
    writtenText=value ? std::wstring(value->chars,value->length) : L"";
}
Method mTextShort{reinterpret_cast<void*>(FoldedText),"SetText",&cTmp,&tVoid,{&tString},6};
Method mTextLong{reinterpret_cast<void*>(FoldedText),"SetText",&cTmp,&tVoid,{&tString,&tBool},6};
struct InitializeText { InitializeText() { cTmp.methods={&mTextShort,&mTextLong}; } } initializeText;
struct TypeObject { Object object; Class* klass; };
struct TypeArray { Object object; void* bounds; size_t length; Object* values[1]; };
struct TaskValue { void* source; short token; Counter* result; };
static int allocations=0,calls=0;
__declspec(noinline) int Add(int x,float y,Method*) { ++calls; return x+static_cast<int>(y); }
__declspec(noinline) float Rate(float x,Method*) { ++calls; return x*2; }
__declspec(noinline) Vec Vector(Vec value,Method*) { ++calls; return {value.x+1,value.y+2,value.z+3}; }
__declspec(noinline) String* Echo(String* value,Method*) { ++calls; return value; }
__declspec(noinline) void Ref(int* value,Method*) { ++calls; *value+=3; }
__declspec(noinline) void Construct(Counter* obj,int value,Method*) { ++calls; obj->value=value; }
__declspec(noinline) int Instance(Counter* obj,int value,Method*) { ++calls; return obj->value+value; }
__declspec(noinline) int Orientation(int value,Method*) { ++calls; return value+1; }
__declspec(noinline) TaskValue TaskResult(Counter* obj,Method*) { ++calls; return {nullptr,0,obj}; }
__declspec(noinline) void TaskConstruct(TaskValue* obj,Counter* result,Method*) { ++calls; obj->source=nullptr; obj->token=0; obj->result=result; }
// Model a folded body that ignores its differently-sized argument lists.
__declspec(noinline) void Folded(Method*) { ++calls; }
__declspec(noinline) int Cross(int value,float second,Method*) { ++calls; return value+static_cast<int>(second)+7; }
__declspec(noinline) void FoldedInitialize() { ++calls; }
Method mAdd{reinterpret_cast<void*>(Add),"Add",&cCounter,&tInt,{&tInt,&tFloat}};
Method mShared{reinterpret_cast<void*>(Add),"Shared",&cCounter,&tInt,{&tInt,&tFloat}};
Method mRate{reinterpret_cast<void*>(Rate),"Rate",&cCounter,&tFloat,{&tFloat}};
Method mVec{reinterpret_cast<void*>(Vector),"Vector",&cCounter,&tVec,{&tVec}};
Method mEcho{reinterpret_cast<void*>(Echo),"Echo",&cCounter,&tString,{&tString}};
Method mRef{reinterpret_cast<void*>(Ref),"Ref",&cCounter,&tVoid,{&tRefInt}};
Method mCtor{reinterpret_cast<void*>(Construct),".ctor",&cCounter,&tVoid,{&tInt},6};
Method mInstance{reinterpret_cast<void*>(Instance),"Instance",&cCounter,&tInt,{&tInt},6};
Method mEnum{reinterpret_cast<void*>(Orientation),"Orientation",&cCounter,&tEnum,{&tEnum}};
Method mTask{reinterpret_cast<void*>(TaskResult),"Task",&cCounter,&tTask,{&tCounter}};
Method mTaskCtor{reinterpret_cast<void*>(TaskConstruct),".ctor",&cTask,&tVoid,{&tCounter},6};
Method mTypeArgs{nullptr,"GetGenericArguments",&cRuntimeType,&tTypeArray,{},6};
Method mTypeDefinition{nullptr,"GetGenericTypeDefinition",&cRuntimeType,&tRuntimeType,{},6};
Method mFoldedShort{reinterpret_cast<void*>(Folded),"FoldedShort",&cCounter,&tVoid,{}};
Method mFoldedLong{reinterpret_cast<void*>(Folded),"FoldedLong",&cCounter,&tVoid,{&tInt,&tFloat}};
Method mCrossBase{reinterpret_cast<void*>(Cross),"CrossBase",&cCounter,&tInt,{&tInt,&tFloat}};
Method mCrossAlias{reinterpret_cast<void*>(Cross),"CrossAlias",&cAlias,&tInt,{&tInt,&tFloat,&tInt}};
Method mEmptyInitialize{reinterpret_cast<void*>(FoldedInitialize),"Initialize",&cCounter,&tVoid,{},6};
Method mForeignInitialize{reinterpret_cast<void*>(FoldedInitialize),"ForeignInitialize",&cAlias,&tVoid,{&tInt}};
Field fValue{"value",&tInt,16},fPlatform{"platform",&tString,24},fStaticPlatform{"staticPlatform",&tString,0,0x16},fX{"x",&tFloat,16},fY{"y",&tFloat,20},fZ{"z",&tFloat,24};
Field fPortrait{"Portrait",&tEnum,0,0x56},fLandscape{"Landscape",&tEnum,0,0x56},fTaskResult{"Result",&tCounter,32};
struct Initialize { Initialize() { cCounter.methods={&mAdd,&mShared,&mRate,&mVec,&mEcho,&mRef,&mCtor,&mInstance,&mEnum,&mTask,&mFoldedShort,&mFoldedLong,&mBytes,&mEchoBytes,&mCrossBase,&mEmptyInitialize}; cCounter.fields={&fValue,&fPlatform,&fStaticPlatform}; cVec.fields={&fX,&fY,&fZ}; cEnum.fields={&fPortrait,&fLandscape}; cTask.methods={&mTaskCtor}; cTask.fields={&fTaskResult}; cRuntimeType.methods={&mTypeArgs,&mTypeDefinition}; cAlias.methods={&mCrossAlias,&mForeignInitialize}; } } initialize;
// TMP classes expose hundreds of properties and backing fields. Small fixtures
// cannot reveal the cost of enumerating and comparing all names for each lookup.
struct InitializeMany { InitializeMany() {
    static std::string names[400]; static Field fields[200]; static Property properties[200];
    static Method getter{nullptr,"get_Value",&cMany,&tInt,{},6};
    for(int i=0;i<200;++i) {
        names[i]="Property"+std::to_string(i); names[200+i]="Field"+std::to_string(i);
        fields[i]={names[200+i].c_str(),&tInt,16}; properties[i]={names[i].c_str(),&getter,nullptr};
        cMany.fields.push_back(&fields[i]); cMany.properties.push_back(&properties[i]);
    }
} } initializeMany;
Class* allClasses[]={&cVoid,&cInt,&cFloat,&cString,&cVec,&cCounter,&cEnum,&cRuntimeType,&cTypeArray,&cTask,&cTaskDefinition,&cAlias,&cByte,&cByteArray,&cMany,&cDerived,&cTmp,&cBool};
static int metadataReads=0;
API int fixture_metadata_reads() { return metadataReads; }
API int fixture_init() { return MH_Initialize(); }
static int creates=0;
API int fixture_creates() { return creates; }
API int fixture_create(void* target,void* replacement,void** original) { ++creates; return MH_CreateHook(target,replacement,original); }
API int fixture_enable(void* target) { return MH_EnableHook(target); }
API int fixture_disable(void* target) { return MH_DisableHook(target); }
API int fixture_allocations() { return allocations; }
API int fixture_calls() { return calls; }
API int fixture_add_null_method() { return Add(7,2.0f,nullptr); }
API int fixture_foreign_initialize() { auto before=calls; reinterpret_cast<void(*)(uintptr_t,Method*)>(FoldedInitialize)(1,nullptr); return calls-before; }
API void* il2cpp_domain_get() { return &cCounter; }
API void** il2cpp_domain_get_assemblies(void*,size_t* count) { static void* values[]={&cCounter}; *count=1; return values; }
API void* il2cpp_assembly_get_image(void* value) { return value; }
API const char* il2cpp_image_get_name(void*) { return "NativeFixture.Runtime.dll"; }
API size_t il2cpp_image_get_class_count(void*) { return std::size(allClasses); }
API Class* il2cpp_image_get_class(void*,unsigned index) { return allClasses[index]; }
API Class* il2cpp_class_from_name(void*,const char* ns,const char* name) { for(auto c:allClasses) if(!strcmp(c->ns,ns)&&!strcmp(c->name,name))return c; return nullptr; }
API const char* il2cpp_class_get_name(Class* c) { return c->name; }
API const char* il2cpp_class_get_namespace(Class* c) { return c->ns; }
API Class* il2cpp_class_get_declaring_type(Class*) { return nullptr; }
API Class* il2cpp_class_get_parent(Class* c) { return c->parent; }
API Class* il2cpp_class_get_interfaces(Class*,void**) { return nullptr; }
API Class* il2cpp_class_from_type(Type* type) { return type->klass; }
API bool il2cpp_type_is_byref(Type* type) { return type->byref; }
API bool il2cpp_class_is_enum(Class* c) { return c==&cEnum; }
API Type* il2cpp_class_enum_basetype(Class*) { return &tInt; }
API bool il2cpp_class_is_valuetype(Class* c) { return c->value; }
API bool il2cpp_class_is_generic(Class* c) { return c==&cTaskDefinition; }
API bool il2cpp_class_is_inflated(Class* c) { return c==&cTask; }
API unsigned il2cpp_class_get_rank(Class* c) { return c==&cTypeArray || c==&cByteArray?1:0; }
API Class* il2cpp_class_get_element_class(Class* c) { return c==&cByteArray?&cByte:&cRuntimeType; }
API size_t il2cpp_array_length(TypeArray* value) { return value->length; }
API Type* il2cpp_class_get_type(Class* c) { static std::map<Class*,Type> types; types[c]={c}; return &types[c]; }
API Object* il2cpp_type_get_object(Type* type) { auto obj=static_cast<TypeObject*>(calloc(1,sizeof(TypeObject))); obj->object.klass=&cRuntimeType; obj->klass=type->klass; return &obj->object; }
API Class* il2cpp_class_from_system_type(TypeObject* obj) { return obj->klass; }
API unsigned il2cpp_class_get_flags(Class*) { return 1; }
API bool il2cpp_class_is_assignable_from(Class* a,Class* b) { return a==b; }
API int il2cpp_class_value_size(Class* c,unsigned* alignment) { *alignment=4; return c->size; }
API Method* il2cpp_class_get_methods(Class* c,void** state) { auto index=reinterpret_cast<size_t>(*state); if(index>=c->methods.size())return nullptr; *state=reinterpret_cast<void*>(index+1); return c->methods[index]; }
API Field* il2cpp_class_get_fields(Class* c,void** state) { auto index=reinterpret_cast<size_t>(*state); if(index>=c->fields.size())return nullptr; *state=reinterpret_cast<void*>(index+1); return c->fields[index]; }
API Property* il2cpp_class_get_properties(Class* c,void** state) { ++metadataReads; auto index=reinterpret_cast<size_t>(*state); if(index>=c->properties.size())return nullptr; *state=reinterpret_cast<void*>(index+1); return c->properties[index]; }
API const char* il2cpp_property_get_name(Property* p) { ++metadataReads; return p->name; }
API Method* il2cpp_property_get_get_method(Property* p) { ++metadataReads; return p->getter; }
API Method* il2cpp_property_get_set_method(Property* p) { ++metadataReads; return p->setter; }
API const char* il2cpp_method_get_name(Method* m) { return m->name; }
API Type* il2cpp_method_get_return_type(Method* m) { return m->result; }
API unsigned il2cpp_method_get_flags(Method* m,unsigned* implementation) { *implementation=0; return m->flags; }
API unsigned il2cpp_method_get_param_count(Method* m) { return static_cast<unsigned>(m->args.size()); }
API Type* il2cpp_method_get_param(Method* m,unsigned index) { return m->args[index]; }
API const char* il2cpp_method_get_param_name(Method*,unsigned index) { return index==0?"value":"second"; }
API bool il2cpp_method_is_generic(Method*) { return false; }
API bool il2cpp_method_is_inflated(Method*) { return false; }
API Object* il2cpp_object_new(Class* c) { ++allocations; auto obj=static_cast<Object*>(calloc(1,16+c->size)); obj->klass=c; return obj; }
API ByteArray* il2cpp_array_new(Class* c,size_t length) { if(c!=&cByte) return nullptr; auto array=static_cast<ByteArray*>(calloc(1,32+length)); array->object.klass=&cByteArray; array->length=length; return array; }
API Class* il2cpp_object_get_class(Object* o) { return o->klass; }
API void* il2cpp_object_unbox(Object* o) { return o+1; }
API Object* il2cpp_value_box(Class* c,void* data) { auto obj=il2cpp_object_new(c); memcpy(obj+1,data,c->size); return obj; }
static String* lastString{};
static void* staticPlatform{};
API String* il2cpp_string_new_utf16(const wchar_t* chars,int length) { auto obj=static_cast<String*>(calloc(1,20+(length+1)*2)); obj->object.klass=&cString; obj->length=length; memcpy(obj->chars,chars,length*2); lastString=obj; return obj; }
API int fixture_tmp_call(int mode) {
    static Object instance{&cTmp,nullptr};
    auto value=il2cpp_string_new_utf16(L"\u30de\u30cb\u30fc",3);
    uintptr_t r8=mode==0 ? reinterpret_cast<uintptr_t>(&mTextShort) : mode==1 ? 0 : mode==2 ? 1 : 0;
    uintptr_t r9=mode<2 ? 0x123456789abcdef0ULL : mode<4 ? reinterpret_cast<uintptr_t>(&mTextLong) : 0;
    auto before=textCalls;
    FoldedText(&instance,value,r8,r9);
    return (writtenText==L"\u91d1\u5e01" ? 1 : 0) | (textR8==r8 && textR9==r9 ? 2 : 0) | (textCalls==before+1 ? 4 : 0);
}
API int fixture_platform_valid(Counter* obj) { return (obj?obj->platform:staticPlatform)==lastString; }
API const wchar_t* il2cpp_string_chars(String* s) { return s->chars; }
API int il2cpp_string_length(String* s) { return s->length; }
API void il2cpp_gc_wbarrier_set_field(Object*,void** address,void* value) { *address=value; }
static std::mutex rootsMutex;
// Unity 6 uses pointer-sized handles. Start above 4 GiB so a uint32_t ABI
// declaration cannot accidentally pass this fixture as it did previously.
static std::map<uintptr_t,Object*> roots;
static uintptr_t nextRoot=0x100000000ULL;
static int invalidHandles=0;
static int targetReads=0;
API uintptr_t il2cpp_gchandle_new(Object* obj,bool) { std::lock_guard<std::mutex> lock(rootsMutex); auto id=++nextRoot; roots[id]=obj; return id; }
static std::vector<uintptr_t> weakRoots;
API uintptr_t il2cpp_gchandle_new_weakref(Object* obj,bool) { std::lock_guard<std::mutex> lock(rootsMutex); auto id=++nextRoot; roots[id]=obj; weakRoots.push_back(id); return id; }
API void fixture_collect_weak_strings() { std::lock_guard<std::mutex> lock(rootsMutex); for(auto id:weakRoots) { auto found=roots.find(id); if(found!=roots.end()) found->second=nullptr; } weakRoots.clear(); }
API Object* il2cpp_gchandle_get_target(uintptr_t handle) { std::lock_guard<std::mutex> lock(rootsMutex); ++targetReads; auto found=roots.find(handle); if(found==roots.end()) { ++invalidHandles; return nullptr; } return found->second; }
API int fixture_target_reads() { std::lock_guard<std::mutex> lock(rootsMutex); return targetReads; }
API void il2cpp_gchandle_free(uintptr_t handle) { std::lock_guard<std::mutex> lock(rootsMutex); if(roots.erase(handle)!=1) ++invalidHandles; }
API int fixture_invalid_handles() { std::lock_guard<std::mutex> lock(rootsMutex); return invalidHandles; }
API int fixture_live_handles() { std::lock_guard<std::mutex> lock(rootsMutex); return static_cast<int>(roots.size()); }
API const char* il2cpp_field_get_name(Field* f) { return f->name; }
API Type* il2cpp_field_get_type(Field* f) { return f->type; }
API unsigned il2cpp_field_get_flags(Field* f) { return f->flags; }
API void il2cpp_field_get_value(Object* obj,Field* f,void* value) { memcpy(value,reinterpret_cast<char*>(obj)+f->offset,f->type->klass->value?f->type->klass->size:sizeof(void*)); }
// Field::SetValueRaw(..., deref_pointer=false): reference fields take the object
// itself, whereas value fields take an address containing the value.
API void il2cpp_field_set_value(Object* obj,Field* f,void* value) { auto address=reinterpret_cast<char*>(obj)+f->offset; if(f->type->klass->value) memcpy(address,value,f->type->klass->size); else *reinterpret_cast<void**>(address)=value; }
API void il2cpp_field_static_set_value(Field*,void* value) { staticPlatform=value; }
API void il2cpp_field_static_get_value(Field* f,void* value) { if(f==&fStaticPlatform) *static_cast<void**>(value)=staticPlatform; else *static_cast<int*>(value)=f==&fPortrait?2:3; }
API Object* il2cpp_runtime_invoke(Method* m,void* obj,void** args,void** exception) {
    *exception=nullptr;
    if(m==&mFoldedShort) reinterpret_cast<decltype(&Folded)>(m->code)(m);
    if(m==&mFoldedLong) reinterpret_cast<void(*)(int,float,Method*)>(m->code)(*static_cast<int*>(args[0]),*static_cast<float*>(args[1]),m);
    if(m==&mAdd || m==&mShared) { auto result=reinterpret_cast<decltype(&Add)>(m->code)(*static_cast<int*>(args[0]),*static_cast<float*>(args[1]),m); return il2cpp_value_box(&cInt,&result); }
    if(m==&mCrossBase) { auto result=reinterpret_cast<decltype(&Cross)>(m->code)(*static_cast<int*>(args[0]),*static_cast<float*>(args[1]),m); return il2cpp_value_box(&cInt,&result); }
    if(m==&mRate) { auto result=reinterpret_cast<decltype(&Rate)>(m->code)(*static_cast<float*>(args[0]),m); return il2cpp_value_box(&cFloat,&result); }
    if(m==&mVec) { auto result=reinterpret_cast<decltype(&Vector)>(m->code)(*static_cast<Vec*>(args[0]),m); return il2cpp_value_box(&cVec,&result); }
    if(m==&mEcho) return reinterpret_cast<Object*>(reinterpret_cast<decltype(&Echo)>(m->code)(static_cast<String*>(args[0]),m));
    if(m==&mRef) reinterpret_cast<decltype(&Ref)>(m->code)(static_cast<int*>(args[0]),m);
    if(m==&mCtor) reinterpret_cast<decltype(&Construct)>(m->code)(static_cast<Counter*>(obj),*static_cast<int*>(args[0]),m);
    if(m==&mInstance) { auto result=reinterpret_cast<decltype(&Instance)>(m->code)(static_cast<Counter*>(obj),*static_cast<int*>(args[0]),m); return il2cpp_value_box(&cInt,&result); }
    if(m==&mEnum) { auto result=reinterpret_cast<decltype(&Orientation)>(m->code)(*static_cast<int*>(args[0]),m); return il2cpp_value_box(&cEnum,&result); }
    if(m==&mTask) { auto result=reinterpret_cast<decltype(&TaskResult)>(m->code)(static_cast<Counter*>(args[0]),m); return il2cpp_value_box(&cTask,&result); }
    if(m==&mTaskCtor) reinterpret_cast<decltype(&TaskConstruct)>(m->code)(static_cast<TaskValue*>(obj),static_cast<Counter*>(args[0]),m);
    if(m==&mTypeArgs) { auto array=static_cast<TypeArray*>(calloc(1,sizeof(TypeArray))); array->object.klass=&cTypeArray; array->length=1; array->values[0]=il2cpp_type_get_object(&tCounter); return &array->object; }
    if(m==&mTypeDefinition) { Type type{&cTaskDefinition}; return il2cpp_type_get_object(&type); }
    if(m==&mBytes) return &reinterpret_cast<decltype(&Bytes)>(m->code)(m)->object;
    if(m==&mEchoBytes) return reinterpret_cast<Object*>(reinterpret_cast<decltype(&EchoBytes)>(m->code)(static_cast<ByteArray*>(args[0]),m));
    return nullptr;
}
