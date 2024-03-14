#include "extensions/Extension_Interface.h"
#include "extensions/YYRValue.h"
#include <iostream>
#include "intrin.h"
#include <mono/jit/jit.h>
#include <mono/metadata/assembly.h>
#include <mono/metadata/debug-helpers.h>
#include <filesystem>
#include <map>
#include <cstring>

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
std::map<std::string, MonoImage*> mods;
MonoDomain *domain;

YYEXPORT void YYExtensionInitialise(const struct YYRunnerInterface* _pFunctions, size_t _functions_size)
{
	memcpy(&gs_runnerInterface, _pFunctions, sizeof(YYRunnerInterface));
	g_pYYRunnerInterface = &gs_runnerInterface;

	if (_functions_size < sizeof(YYRunnerInterface)) {
		DebugConsoleOutput("ERROR : runner interface mismatch in extension DLL\n");
	}

	DebugConsoleOutput("YYExtensionInitialise CONFIGURED\n");

    DebugConsoleOutput("Loading mods for interop...\n");
    mono_set_assemblies_path("gmsl/interop/lib");
    domain = mono_jit_init("gmsl");

    std::filesystem::path directoryPath("gmsl/mods");

    for (const auto& entry : std::filesystem::directory_iterator(directoryPath)) {
        if (std::filesystem::is_directory(entry)) {
            std::filesystem::path fn = entry.path().filename();
            std::filesystem::path modpath = directoryPath / fn / (fn.string() + ".dll");
            std::cout << modpath << std::endl;
	    if (!std::filesystem::exists(modpath)) continue;
            MonoAssembly *assembly = mono_domain_assembly_open(domain, modpath.string().c_str());
            MonoImage *image = mono_assembly_get_image(assembly);
            mods[fn.string()] = image;
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

YYEXPORT void interop_function(RValue& Result, CInstance* selfinst, CInstance* otherinst, int argc, RValue* arg)
{
    MonoImage *image = mods[currentInterop.dll];
    MonoClass* klass = mono_class_from_name(image, currentInterop.ns.c_str(), currentInterop.clazz.c_str());
    MonoObject* instance = mono_object_new(domain, klass);
    mono_runtime_object_init(instance);
    MonoMethod* method = mono_class_get_method_from_name(klass, currentInterop.function.c_str(), currentInterop.argc);
    
    void** args = new void*[currentInterop.argc];
    RValue elem;

    for (int i = 0; i < argc; i++)
    {
        elem = arg[i];
        switch (elem.kind)
        {
            case VALUE_REAL:
                args[i] = &arg[i].val;
                break;

            case VALUE_STRING:
                MonoString* string = mono_string_new(domain, elem.GetString());
                args[i] = string;
                break;
        }
    }

    MonoObject *exception;
    exception = NULL;
    MonoObject* returnValue = mono_runtime_invoke(method, instance, args, &exception);
    if (exception) {
        std::cout << "Exception thrown in c#" << std::endl;
        MonoClass* pClass = mono_object_get_class(exception);
        void* iter = NULL;
        while (MonoClassField* field = mono_class_get_fields(pClass, &iter)) {
            const char* fieldName = mono_field_get_name(field);
            std::cout << "Field Name: " << fieldName << std::endl;
            MonoString* trace;
            mono_field_get_value(exception, field, &trace);
            std::cout << mono_string_to_utf8(trace) << std::endl;
        }

        YYError("Exception from c# thrown check console for more info!");
    }
    MonoClass* classPtr = mono_object_get_class(returnValue);
    const char* typeName = mono_class_get_name(classPtr);

    if (std::strcmp(typeName, "Int32") == 0)
    {
        Result.kind = VALUE_REAL;
        Result.val = *(int*)mono_object_unbox(returnValue);
    }
    else if (std::strcmp(typeName, "Single") == 0)
    {
        Result.kind = VALUE_REAL;
        Result.val = *(float*)mono_object_unbox(returnValue);
    }
    else if (std::strcmp(typeName, "Double") == 0)
    {
        Result.kind = VALUE_REAL;
        Result.val = *(double*)mono_object_unbox(returnValue);
    }
    else if (std::strcmp(typeName, "String") == 0)
    {
        YYCreateString(&Result, mono_string_to_utf8((MonoString*)returnValue));
    }
    else
    {
        std::cout << "Cant Convert Type Name: " << typeName << std::endl;
        YYCreateString(&Result, "INTEROP ERROR");
    }

    delete[] args;
}
