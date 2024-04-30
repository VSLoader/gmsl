using System.Reflection;
using UndertaleModLib;
using System.Diagnostics;
using UndertaleModLib.Models;
using System.Text.Json;
using System.Runtime.InteropServices;
using GMSL;
using GMSL.Logger;

namespace gmsl_patcher;

public static class Program
{
    private static uint _currentId = 0;
    private static UndertaleExtensionFile _interopExtension = null!;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static void Main(string[] args)
    {
        if (!args.Contains("-gmsl_console") && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ShowWindow(GetConsoleWindow(), 0);
    
        var gmslDir = Path.GetDirectoryName(Environment.CurrentDirectory);
        var modDir = Path.Combine(gmslDir!, "mods");
        var baseDir = Path.GetDirectoryName(gmslDir);
        var modDirs = Directory.GetDirectories(modDir);
        List<string> whitelist = new();
        List<string> blacklist = new();

        if (modDirs.Length == 0)
        {
            File.Copy(Path.Combine(baseDir!, "data.win"), Path.Combine(baseDir!, "cache.win"), true);
            StartGame(args, baseDir!);
            return;
        }

        LoadList(Path.Combine(modDir, "whitelist.txt"), whitelist);
        LoadList(Path.Combine(modDir, "blacklist.txt"), blacklist);

        Logger.Info("Reading data.win...");
        var stream = File.OpenRead(Path.Combine(baseDir!, "data.win"));
        var data = UndertaleIO.Read(stream, Logger.Error, _ => { });
        stream.Dispose();

        if (File.Exists(Path.Combine(baseDir!, "cache.win")))
            File.Delete(Path.Combine(baseDir!, "cache.win"));

        SetupInterop(data, baseDir!);

        Logger.Info("Loading mods...");
        foreach (var modname in modDirs)
        {
            if (!whitelist.Contains(Path.GetFileName(modname)) && whitelist.Count != 0) continue;
            if (blacklist.Contains(Path.GetFileName(modname)) && blacklist.Count != 0) continue;

            Logger.Info($"Loading mod {Path.GetFileName(modname)}...");

            var modPath = Path.Combine(modname, Path.GetFileName(modname) + ".dll");

            if (!File.Exists(modPath))
            {
                Logger.Error($"Error loading mod {modname} cant find {modPath}");
                continue;
            }

            var modAssembly = Assembly.LoadFrom(modPath);
            var modClass = modAssembly.GetTypes()
                    .FirstOrDefault(modType => modType.GetInterfaces().Contains(typeof(IGMSLMod)));

            if (modClass == null && !File.Exists(Path.Combine(modname, "modinfo.json")))
            {
                Logger.Warn($"Cant load mod {modname} cant find class inheriting IGMSLMod, trying to load as gs2ml");

                var loaded = false;
                foreach (var type in modAssembly.GetTypes())
                {
                    var load = type.GetMethod("Load");
                    if (load == null) continue;

                    var gs2mlClass = Activator.CreateInstance(type);
                    load.Invoke(gs2mlClass, new object[] { 0, data });
                    loaded = true;

                    break;
                }

                if (!loaded)
                    Logger.Error($"Couldn't load mod {modname} as gmsl or gs2ml mod");

                continue;
            }

            Environment.CurrentDirectory = modname;

            var info = JsonSerializer.Deserialize<ModInfo>(File.ReadAllText(Path.Combine(modname, "modinfo.json")));

            var missing = false;
            foreach (var dependency in info!.Dependencies)
            {
                if (modDirs.Contains(Path.Combine(Path.GetDirectoryName(modDirs[0]), dependency))) continue;
                
                Logger.Error($"Mod {modname} is missing dependency {dependency}");
                missing = true;
            }
            if (missing) continue;

            var mod = (IGMSLMod)Activator.CreateInstance(modClass!)!;

            try
            {
                mod!.Load(data, info!);
            }
            catch (Exception ex)
            {
                Logger.Info($"The mod ${modname} had an error while loading");
                Logger.Info("It has been logged to a file and the normal game will launch");
                Logger.Error(ex.ToString());

                if (File.Exists("error.txt")) File.Delete("error.txt");

                File.WriteAllText("error.txt", ex.ToString());

                ShowWindow(GetConsoleWindow(), 1);
                
                Logger.Info("Please press enter to continue launching...");
                Console.ReadLine();

                StartGame(args, baseDir!, false);
                return;
            }

            foreach (var type in modAssembly.GetTypes())
            {
                foreach (var method in type.GetMembers())
                {
                    var interop = method.GetCustomAttribute<GmlInterop>();
                    if (interop == null) continue;

                    CreateInteropFunction(
                        interop,
                        method,
                        Path.GetFileNameWithoutExtension(modPath),
                        data
                    );
                }
            }
        }
        
        Logger.Info("Saving modified data.win...");
        stream = File.OpenWrite(Path.Combine(baseDir!, "cache.win"));
        UndertaleIO.Write(stream, data);
        stream.Dispose();

        Logger.Info("Launching game...");
        StartGame(args, baseDir!);
    }

    private static void CreateInteropFunction(GmlInterop interop, MemberInfo method, string file, UndertaleData data)
    {
        foreach (var extension in data.Extensions)
        {
            foreach (var file in extension.Files)
            {
                foreach (var function in file.Functions)
                {
                    if (function.ID >= _currentId)
                    {
                        _currentId = function.ID;
                    }
                }
            }
        }
        _currentId++;
        
        UndertaleExtensionFunction function = new()
        {
            Name = data.Strings.MakeString($"{interop.Name}_interop"),
            ExtName = data.Strings.MakeString("interop_function"),
            Kind = 11,
            ID = _currentId
        };
        _interopExtension.Functions.Add(function);
        var args = "";
        for (var i = 0; i < interop.Argc; i++)
        {
            args += $"argument{i}{(i != interop.Argc - 1 ? ", " : "")}";
        }
        CreateLegacyScript(
            data,
            interop.Name,
            $"interop_set_function(\"{file}\", \"{method.DeclaringType!.Namespace}\", \"{method.DeclaringType.Name}\", \"{method.Name}\", {interop.Argc});\nreturn {interop.Name}_interop({args});",
            interop.Argc);
    }

    private static void SetupInterop(UndertaleData data, string baseDir)
    {
        UndertaleExtensionFunction setFunction = new()
        {
            Name = data.Strings.MakeString("interop_set_function"),
            ExtName = data.Strings.MakeString("interop_set_function"),
            Kind = 11,
            ID = _currentId
        };

        UndertaleExtensionFile extensionFile = new()
        {
            Kind = UndertaleExtensionKind.Dll,
            Filename = data.Strings.MakeString(Path.Combine(baseDir, "gmsl", "interop", "gmsl-interop.dll")),
            InitScript = data.Strings.MakeString(""),
            CleanupScript = data.Strings.MakeString("")
        };
        extensionFile.Functions.Add(setFunction);

        UndertaleExtension interop = new()
        {
            Name = data.Strings.MakeString("gmsl"),
            ClassName = data.Strings.MakeString(""),
            Version = data.Strings.MakeString("1.0.0"),
            FolderName = data.Strings.MakeString("")
        };
        interop.Files.Add(extensionFile);
        data.Extensions.Add(interop);

        _interopExtension = extensionFile;
    }

    private static UndertaleCode CreateCode(UndertaleData data, UndertaleString name, out UndertaleCodeLocals locals)
    {
        locals = new UndertaleCodeLocals
        {
            Name = name
        };
        locals.Locals.Add(new UndertaleCodeLocals.LocalVar
        {
            Name = data.Strings.MakeString("arguments"),
            Index = 2
        });
        data.CodeLocals.Add(locals);

        UndertaleCode mainCode = new()
        {
            Name = name,
            LocalsCount = 1,
            ArgumentsCount = 0
        };
        data.Code.Add(mainCode);

        return mainCode;
    }

    private static UndertaleScript CreateLegacyScript(UndertaleData data, string name, string code, ushort argCount)
    {
        var mainName = data.Strings.MakeString(name, out var nameIndex);
        var mainCode = CreateCode(data, mainName, out _);
        mainCode.ArgumentsCount = argCount;

        mainCode.ReplaceGML(code, data);

        UndertaleScript script = new()
        {
            Name = mainName,
            Code = mainCode
        };
        data.Scripts.Add(script);

        UndertaleFunction function = new()
        {
            Name = mainName,
            NameStringID = nameIndex
        };
        data.Functions.Add(function);

        return script;
    }

    private static void StartGame(string[] args, string baseDir, bool loadmods = true)
    {
        ProcessStartInfo processStartInfo = new()
        {
            FileName = Path.Combine(baseDir, args[0]),
            WorkingDirectory = baseDir,
        };

        processStartInfo.ArgumentList.Add("-game");

        if (loadmods)
            processStartInfo.ArgumentList.Add("cache.win");
        else
            processStartInfo.ArgumentList.Add("data.win");

        for (var i = 1; i < args.Length; i++)
        {
            processStartInfo.ArgumentList.Add(args[i]);
        }

        Process.Start(processStartInfo);
    }

    private static void LoadList(string path, List<string> populate)
    {
        if (!File.Exists(path)) return;

        populate.Add("");
        populate.AddRange(File.ReadAllLines(path));
    }
}
