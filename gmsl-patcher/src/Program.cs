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
    private static UndertaleExtensionFile _interopExtension = null!;
    private static List<string> _whitelist = new();
    private static List<string> _blacklist = new();

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

        if (modDirs.Length == 0)
        {
            File.Copy(Path.Combine(baseDir!, "data.win"), Path.Combine(baseDir!, "cache.win"), true);
            StartGame(args, baseDir!);
            return;
        }

        LoadList(Path.Combine(modDir, "whitelist.txt"), _whitelist);
        LoadList(Path.Combine(modDir, "blacklist.txt"), _blacklist);

        Logger.Info("Reading data.win...");
        var stream = File.OpenRead(Path.Combine(baseDir!, "data.win"));
        var data = UndertaleIO.Read(stream, Logger.Error, _ => { });
        stream.Dispose();

        if (File.Exists(Path.Combine(baseDir!, "cache.win")))
            File.Delete(Path.Combine(baseDir!, "cache.win"));

        SetupInterop(data, baseDir!);

        Logger.Info("Loading mod info's...");

        List<ModInfo> mods = new();

        foreach (var mod in modDirs)
        {
            var modInfoPath = Path.Combine(mod, "modinfo.json");
            if (!File.Exists(modInfoPath))
            {
                Logger.Warn($"Not loading mod {mod} because it doesn't have a modinfo.json");
                continue;
            }

            var info = JsonSerializer.Deserialize<ModInfo>(File.ReadAllText(modInfoPath));

            if (info == null)
            {
                Logger.Warn($"Not loading mod {mod} because it's modinfo is invalid");
                continue;
            }
            info.ModDir = mod;
            mods.Add(info);
        }

        Logger.Info("Got mods:");
        foreach (var mod in mods)
        {
            Logger.Info($"{mod.Name} : {mod.ID}");
        }

        Logger.Info("Building load order...");

        var loadOrder = BuildLoadOrder(mods);

        Logger.Info("Built load order:");
        foreach (var mod in loadOrder)
        {
            Logger.Info($"{mod.Name} : {mod.ID}");
        }

        Logger.Info("Loading mods...");

        foreach (var mod in loadOrder)
        {
            Logger.Info($"Loading {mod.ID}");

            var modPath = Path.Combine(mod.ModDir, mod.Name + ".dll");

            if (!File.Exists(modPath))
            {
                Logger.Error($"Error loading mod {mod.ID} cant find {modPath}");
                continue;
            }

            var modAssembly = Assembly.LoadFrom(modPath);

            foreach (var type in modAssembly.GetTypes())
            {
                // TODO this is absolutely horrible
                if (type.GetMethod("Load") != null)
                {
                    Logger.Info("calling load method");
                    var instance = (IGMSLMod)Activator.CreateInstance(type)!;
                    Environment.CurrentDirectory = mod.ModDir;

                    try
                    {
                        instance.Load(data, mod);
                    }
                    catch (Exception ex)
                    {
                        Logger.Info($"The mod {mod.ID} had an error while loading");
                        Logger.Info("It has been logged to a file and the normal game will launch");
                        Logger.Error(ex.ToString());

                        if (File.Exists("error.txt")) File.Delete("error.txt");

                        File.WriteAllText("error.txt", ex.ToString());

                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) ShowWindow(GetConsoleWindow(), 1);

                        Logger.Info("Please press enter to continue launching...");
                        Console.ReadLine();

                        StartGame(args, baseDir!, false);
                        return;
                    }
                }

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
        //Console.ReadLine();
        StartGame(args, baseDir!);
    }

    private static List<ModInfo> BuildLoadOrder(List<ModInfo> mods)
    {
        var order = new List<ModInfo>();
        bool useWhitelist = _whitelist.Count > 0;

        foreach (var mod in mods)
        {
            if (useWhitelist)
            {
                if (!_whitelist.Contains(mod.ID)) continue;
            }
            else
            {
                if (_blacklist.Contains(mod.ID)) continue;
            }

            bool missing = false;
            foreach (var dependency in mod.Dependencies)
            {
                var modInfo = mods.FirstOrDefault(x => x.ID.Equals(dependency));

                if (modInfo == null)
                {
                    Logger.Info($"Mod {mod.ID} is missing dependency {dependency}");
                    missing = true;
                    break;
                }
                order.Add(modInfo);
            }

            if (!missing && !order.Contains(mod))
                order.Add(mod);
        }

        return order;
    }

    private static void CreateInteropFunction(GmlInterop interop, MemberInfo method, string file, UndertaleData data)
    {
        UndertaleExtensionFunction function = new()
        {
            Name = data.Strings.MakeString($"{interop.Name}_interop"),
            ExtName = data.Strings.MakeString("interop_function"),
            Kind = 11,
            ID = Extension.NextId()
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
        Extension.Init(data);

        UndertaleExtensionFunction setFunction = new()
        {
            Name = data.Strings.MakeString("interop_set_function"),
            ExtName = data.Strings.MakeString("interop_set_function"),
            Kind = 11,
            ID = Extension.NextId()
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
