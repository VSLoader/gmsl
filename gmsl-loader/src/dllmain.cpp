#define WIN32_LEAN_AND_MEAN

#include "windows.h"
#include <filesystem>
#include <iostream>
#include <shellapi.h>

// https://github.com/cgytrus/gmml/blob/main/gmml/src/dllmain.cpp#L26
constexpr auto PROXY_DLL = TEXT("version.dll");
constexpr auto PROXY_MAX_PATH = 260;
constexpr auto MAX_COMMANDLINE = 5120;

#define DLL_PROXY_ORIGINAL(name) original_##name

#define DLL_NAME(name)                \
    FARPROC DLL_PROXY_ORIGINAL(name); \
    void _##name() {                  \
        DLL_PROXY_ORIGINAL(name)();   \
    }
#include "../include/proxy.h"
#undef DLL_NAME

std::filesystem::path getSystemDirectory() {
    wchar_t SystemDirectoryPath[MAX_PATH] = { 0 };
    if (!GetSystemDirectoryW(SystemDirectoryPath, MAX_PATH))
        std::cout << "GetSystemDirectoryW fails: " << GetLastError() << std::endl;
    return SystemDirectoryPath;
}

bool loadProxy() {
    const auto libPath = getSystemDirectory() / PROXY_DLL;
    const auto lib = LoadLibrary(libPath.string().c_str());
    if(!lib) return false;

    #define DLL_NAME(name) DLL_PROXY_ORIGINAL(name) = GetProcAddress(lib, ###name);
    #include "../include/proxy.h"
    #undef DLL_NAME

    return true;
}

void RunPatcher()
{
    HMODULE hModule = GetModuleHandle(NULL);
    TCHAR path[MAX_PATH];
    
    if (hModule != NULL) {
        DWORD length = GetModuleFileName(hModule, path, MAX_PATH);
        if (length > 0 && length < MAX_PATH) {
            std::cout << "Found game path: " << path << std::endl;
        } else {
            std::cout << "Error: Unable to retrieve the path." << std::endl;
            return;
        }
    } else {
        std::cout << "Error: Unable to get the module handle." << std::endl;
        return;
    }

    LPSTR lpCmdLine = GetCommandLine();
    LPSTR lpPad = "blah ";

    int len = lstrlen(lpPad) + lstrlen(lpCmdLine) + 1;
    LPSTR result = (LPSTR)malloc(len);

    if (result != nullptr) {
        lstrcpy(result, lpPad);
        lstrcat(result, lpCmdLine);
    } else {
        std::cerr << "Memory allocation failed." << std::endl;
    }

    std::filesystem::path gameFile = path;
    std::filesystem::path patcher = gameFile.parent_path() / "gmsl" / "patcher" / "gmsl-patcher.exe";
    STARTUPINFO startupInfo;
    PROCESS_INFORMATION processInfo;

    ZeroMemory(&startupInfo, sizeof(STARTUPINFO));
    startupInfo.cb = sizeof(STARTUPINFO);

    int error;
    error = CreateProcess(patcher.string().c_str(), result, NULL, NULL, FALSE, 0, NULL, patcher.parent_path().string().c_str(), &startupInfo, &processInfo);

    if (error == 0)
    {
        std::cout << "Error in CreateProcess: " << GetLastError() << std::endl;
    }
    else
    {
        CloseHandle(processInfo.hProcess);
        CloseHandle(processInfo.hThread);
    }

    free(result);
}

DWORD WINAPI Loader(LPVOID lpParam)
{
    SuspendThread(lpParam); 
    RunPatcher();
    ResumeThread(lpParam);
    exit(0);
    return 0;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    if (ul_reason_for_call != DLL_PROCESS_ATTACH) return TRUE;

        LPWSTR lpCmdLine = GetCommandLineW();
    std::wstring cmdLine(lpCmdLine);
    std::string cmdLineStr(cmdLine.begin(), cmdLine.end());

    size_t found = cmdLineStr.find("gmsl_console");
    if (found != std::string::npos)
    {
        AllocConsole();
        freopen_s((FILE**)stdout, "CONOUT$", "w", stdout);
    }
    
    if (!loadProxy()) return FALSE;

    found = cmdLineStr.find("game");
    if (found != std::string::npos) return TRUE;

    // https://github.com/OmegaMetor/GS2ML/blob/main/gs2ml-cxx/src/dllmain.cpp#L166
    HANDLE curThread = OpenThread(THREAD_ALL_ACCESS, FALSE, GetCurrentThreadId());
    HANDLE loaderThread = CreateThread(NULL, 0, Loader, curThread, 0, NULL);
    if (loaderThread == 0) 
        return FALSE;

    return TRUE;
}