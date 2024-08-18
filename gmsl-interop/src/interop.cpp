#include "extensions/Extension_Interface.h"
#include "extensions/YYRValue.h"

#include <nethost.h>
#include <coreclr_delegates.h>
#include <hostfxr.h>
#ifdef _WIN32
#include <Windows.h>

#define STR(s) L ## s
#define CH(c) L ## c
#define DIR_SEPARATOR L'\\'

#define string_compare wcscmp

#else
#include <dlfcn.h>
#include <limits.h>

#define STR(s) s
#define CH(c) c
#define DIR_SEPARATOR '/'
#define MAX_PATH PATH_MAX

#define string_compare strcmp

#endif

#include <iostream>
#include "intrin.h"
#include <filesystem>
#include <map>
#include <cstring>
#include <locale>
#include <codecvt>
#include <assert.h>

// Forward declarations
void *load_library(const char_t *);
void *get_export(void *, const char *);

#ifdef _WIN32
void *load_library(const char_t *path)
{
    HMODULE h = ::LoadLibraryW(path);
    assert(h != nullptr);
    return (void*)h;
}
void *get_export(void *h, const char *name)
{
    void *f = ::GetProcAddress((HMODULE)h, name);
    assert(f != nullptr);
    return f;
}
#else
void *load_library(const char_t *path)
{
    void *h = dlopen(path, RTLD_LAZY | RTLD_LOCAL);
    assert(h != nullptr);
    return h;
}
void *get_export(void *h, const char *name)
{
    void *f = dlsym(h, name);
    assert(f != nullptr);
    return f;
}
#endif

struct CurrentInterop
{
    std::string dll;
    std::string ns;
    std::string clazz;
    std::string function;
    int argc;
};

YYRunnerInterface gs_runnerInterface;
YYRunnerInterface* g_pYYRunnerInterface;
CurrentInterop currentInterop;

auto config_path = L"gmsl\\patcher\\gmsl-patcher.runtimeconfig.json";

// hostfxr_initialize_for_dotnet_command_line_fn init_for_cmd_line_fptr;
using get_hostfxr_path_fn = int(*)(char_t * buffer, size_t * buffer_size, const get_hostfxr_parameters *parameters);
hostfxr_initialize_for_runtime_config_fn init_for_config;
hostfxr_get_runtime_delegate_fn get_delegate;
hostfxr_set_error_writer_fn set_error_writer;
load_assembly_fn load_assembly;
get_function_pointer_fn get_function_pointer;
// hostfxr_run_app_fn run_app_fptr;
hostfxr_close_fn close_hostfxr;
hostfxr_handle ctx = nullptr;

// std::map<std::string, MonoImage*> mods;
// MonoDomain *domain;

void dotnet_error_handler(const char_t *message) {
    #if defined(_WIN32)
        printf(".NET error: %ls\n", message);
    #else
        printf(".NET error: %s\n", message);
    #endif
}

YYEXPORT void YYExtensionInitialise(const struct YYRunnerInterface* _pFunctions, size_t _functions_size)
{
	memcpy(&gs_runnerInterface, _pFunctions, sizeof(YYRunnerInterface));
	g_pYYRunnerInterface = &gs_runnerInterface;
	
	if (_functions_size < sizeof(YYRunnerInterface)) {
		std::cout << "[VSLoader] ERROR : runner interface mismatch in extension DLL" << std::endl;
	}
	
	std::cout << "[VSLoader] YYExtensionInitialise CONFIGURED" << std::endl;

    std::cout << "[VSLoader] Loading .NET runtime..." << std::endl;

    #if defined(_WIN32)
        auto netHostLibrary = load_library(L"gmsl/interop/lib/nethost.dll");
    #elif defined(__linux__)
        auto netHostLibrary = load_library("gmsl/interop/lib/libnethost.so");
    // #elif defined(OS_MACOS)
    //     void *netHostLibrary = nullptr;
    //     for (const auto &pluginPath : paths::Plugins.read()) {
    //         auto frameworksPath = pluginPath.parent_path().parent_path() / "Frameworks";

    //         netHostLibrary = loadLibrary((frameworksPath / "libnethost.dylib").c_str());
    //         if (netHostLibrary != nullptr)
    //             break;
    //     }
    //     if (netHostLibrary == nullptr) {
    //         for (const auto &librariesPath : paths::Libraries.read()) {
    //             netHostLibrary = loadLibrary((librariesPath / "libnethost.dylib").c_str());
    //             if (netHostLibrary != nullptr)
    //                 break;
    //         }
    //     }
    #endif

    if (netHostLibrary == nullptr) {
	    std::cout << "[VSLoader] Failed to find libnethost" << std::endl;
        return;
    }

    auto get_hostfxr_path_ptr = (get_hostfxr_path_fn)get_export(netHostLibrary, "get_hostfxr_path");

    // second arg is assembly path
    get_hostfxr_parameters params { sizeof(get_hostfxr_parameters), nullptr, nullptr };
    // Pre-allocate a large buffer for the path to hostfxr
    char_t buffer[MAX_PATH];
    size_t buffer_size = sizeof(buffer) / sizeof(char_t);
    int rc = get_hostfxr_path_ptr(buffer, &buffer_size, &params);
    if (rc != 0) {
        printf("[VSLoader] Failed to find hostfxr status=%d\n", rc);
        return;
    }

    // Load hostfxr and get desired exports
    // NOTE: The .NET Runtime does not support unloading any of its native libraries. Running
    // dlclose/FreeLibrary on any .NET libraries produces undefined behavior.
    void *lib = load_library(buffer);
    // init_for_cmd_line_fptr = (hostfxr_initialize_for_dotnet_command_line_fn)get_export(lib, "hostfxr_initialize_for_dotnet_command_line");
    init_for_config = (hostfxr_initialize_for_runtime_config_fn)get_export(lib, "hostfxr_initialize_for_runtime_config");
    get_delegate = (hostfxr_get_runtime_delegate_fn)get_export(lib, "hostfxr_get_runtime_delegate");
    set_error_writer = (hostfxr_set_error_writer_fn) get_export(lib, "hostfxr_set_error_writer");
    // run_app_fptr = (hostfxr_run_app_fn)get_export(lib, "hostfxr_run_app");
    close_hostfxr = (hostfxr_close_fn)get_export(lib, "hostfxr_close");
    // mono_set_assemblies_path("gmsl/interop/lib");
    // domain = mono_jit_init_version("gmsl", "v4.0.30319");

    printf("[VSLoader] .NET function pointers 0x%x 0x%x 0x%x\n", init_for_config, get_delegate, close_hostfxr);

    uint32 result = init_for_config(config_path, nullptr, &ctx);

    if (result > 2 || ctx == nullptr) {
        if (result == /* FrameworkMissingFailure */ 0x80008096) {
	        std::cout << "[VSLoader] Failed to find .NET runtime" << std::endl;
        }

        printf("[VSLoader] .NET init failed 0x%x\n", result);
        return;
    }

    set_error_writer(dotnet_error_handler);

    std::cout << "[VSLoader] .NET runtime initialized!" << std::endl;


    result = get_delegate(
        ctx,
        hostfxr_delegate_type::hdt_load_assembly,
        reinterpret_cast<void**>(&load_assembly)
    );

    if (result != 0 || load_assembly == nullptr) {
        printf("[VSLoader] Failed to get load_assembly delegate 0x%x\n", result);
        return;
    }

    result = get_delegate(
        ctx,
        hostfxr_delegate_type::hdt_get_function_pointer,
        reinterpret_cast<void**>(&get_function_pointer)
    );

    if (result != 0 || load_assembly == nullptr) {
        printf("[VSLoader] Failed to get get_function_pointer delegate 0x%x\n", result);
        return;
    }

    printf("[VSLoader] load_assembly 0x%x get_function_pointer 0x%x\n", load_assembly, get_function_pointer);

    std::cout << "[VSLoader] Loading mods for interop..." << std::endl;
    std::filesystem::path directoryPath(L"./gmsl/mods");

    for (const auto& entry : std::filesystem::directory_iterator(directoryPath)) {
        if (std::filesystem::is_directory(entry)) {
            std::filesystem::path fn = entry.path().filename();
            std::filesystem::path modpath = std::filesystem::current_path() / directoryPath / fn / (fn.wstring() + L".dll");
            std::cout << modpath << std::endl;
	    if (!std::filesystem::exists(modpath)) continue;
            printf("[VSLoader] Loading mod %s...\n", modpath.string().c_str());
            result = load_assembly(modpath.wstring().c_str(), nullptr, nullptr);
            if (result != 0) {
                printf("[VSLoader] Failed to load mod %s 0x%x\n", modpath.string().c_str(), result);
            } else {
                printf("[VSLoader] Loaded mod %s\n", modpath.string().c_str());
            }
        }
    }
}

YYEXPORT void interop_set_function(RValue& Result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    Result.kind = VALUE_REAL;
    Result.val = 1;
    currentInterop = CurrentInterop();
    currentInterop.dll = arg[0].GetString();
    currentInterop.ns = arg[1].GetString();
    currentInterop.clazz = arg[2].GetString();
    currentInterop.function = arg[3].GetString();
    currentInterop.argc = arg[4].val;
}

inline std::wstring convert( const std::string& as )
{
            // deal with trivial case of empty string
    if( as.empty() )    return std::wstring();

            // determine required length of new string
    size_t reqLength = ::MultiByteToWideChar( CP_UTF8, 0, as.c_str(), (int)as.length(), 0, 0 );

            // construct new string of required length
    std::wstring ret( reqLength, L'\0' );

            // convert old string to new string
    ::MultiByteToWideChar( CP_UTF8, 0, as.c_str(), (int)as.length(), &ret[0], (int)ret.length() );

            // return new string ( compiler should optimize this away )
    return ret;
}

YYEXPORT void interop_function(RValue& Result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    std::wstring wide = convert(currentInterop.function);
    component_entry_point_fn fn;
    printf("[VSLoader] calling interop fn %s on dll %s ns %s class %s fqtn %ls\n", currentInterop.function.c_str(), currentInterop.dll.c_str(), currentInterop.ns.c_str(), currentInterop.clazz.c_str(), (convert((currentInterop.ns + "." + currentInterop.clazz + ", " + currentInterop.dll)).c_str()));
    uint32 result = get_function_pointer(convert((currentInterop.ns + "." + currentInterop.clazz + ", " + currentInterop.dll)).c_str(), convert(currentInterop.function).c_str(), nullptr, nullptr, nullptr, reinterpret_cast<void**>(&fn));
    if (result != 0) {
        printf("[VSLoader] Failed to get function pointer 0x%x\n", result);
        return;
    }
    auto agv = new char*[0];
    int res = fn(&agv, 0);
    printf("[VSLoader] call ret 0x%x", res);
//     MonoImage *image = mods[currentInterop.dll];
//     MonoClass* klass = mono_class_from_name(image, currentInterop.ns.c_str(), currentInterop.clazz.c_str());
// //    MonoObject* instance = mono_object_new(domain, klass);
// //    mono_runtime_object_init(instance);
//     MonoMethod* method = mono_class_get_method_from_name(klass, currentInterop.function.c_str(), currentInterop.argc);
//     void** args = new void*[currentInterop.argc];
//     RValue elem;

//     for (int i = 0; i < argc; i++)
//     {
//         elem = arg[i];
//         switch (elem.kind)
//         {
//             case VALUE_REAL:
//                 args[i] = &arg[i].val;
//                 break;

//             case VALUE_BOOL:
//                 args[i] = &arg[i].val;
//                 break;

//             default:
//                 std::cout << "Unknown value type: " << elem.kind;
//                 return;

//             // This has to be at the bottom for some reason idfk why
//             case VALUE_STRING:
//                 MonoString* string = mono_string_new(domain, elem.GetString());
//                 args[i] = string;
//                 break;
//         }
//     }

//     MonoObject *exception;
//     exception = NULL;
//     MonoObject* returnValue = mono_runtime_invoke(method, NULL, args, &exception);
//     if (exception) {
//         std::cout << "Exception thrown in c# while calling " << currentInterop.function << std::endl;
//         MonoClass* pClass = mono_object_get_class(exception);
//         void* iter = NULL;
//         while (MonoClassField* field = mono_class_get_fields(pClass, &iter)) {
//             const char* fieldName = mono_field_get_name(field);
//             std::cout << "Field Name: " << fieldName << std::endl;
//             MonoString* trace;
//             mono_field_get_value(exception, field, &trace);
//             if (!trace) continue;
//             std::cout << mono_string_to_utf8(trace) << std::endl;
//         }

//         // std::cin.get(); // freeze the program to signify something is wrong
//         YYCreateString(&Result, "INTEROP ERROR");
//     }
//     else {
//         MonoClass* classPtr = mono_object_get_class(returnValue);
//         const char* typeName = mono_class_get_name(classPtr);

//         if (std::strcmp(typeName, "Int32") == 0)
//         {
//             Result.kind = VALUE_REAL;
//             Result.val = *(int*)mono_object_unbox(returnValue);
//         }
//         else if (std::strcmp(typeName, "Single") == 0)
//         {
//             Result.kind = VALUE_REAL;
//             Result.val = *(float*)mono_object_unbox(returnValue);
//         }
//         else if (std::strcmp(typeName, "Double") == 0)
//         {
//             Result.kind = VALUE_REAL;
//             Result.val = *(double*)mono_object_unbox(returnValue);
//         }
//         else if (std::strcmp(typeName, "String") == 0)
//         {
//             YYCreateString(&Result, mono_string_to_utf8((MonoString*)returnValue));
//         }
//         else
//         {
//             std::cout << "Cant Convert Type Name: " << typeName << std::endl;
//             YYCreateString(&Result, "INTEROP ERROR");
//         }
//     }
//     delete[] args;
}
