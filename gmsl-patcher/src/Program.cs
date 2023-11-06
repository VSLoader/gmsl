using System.Reflection;
using GMSL;
using UndertaleModLib;
using System.Diagnostics;
using UndertaleModLib.Models;
using System.Text.Json;

class Program
{

    public static uint currentId = 1;
    public static UndertaleExtensionFile interopExtension;

    public static void Main(string[] args)
    {
        string gmslDir = Path.GetDirectoryName(Environment.CurrentDirectory) ?? "error";
        string modDir = Path.Combine(gmslDir, "mods");
        string baseDir = Path.GetDirectoryName(gmslDir) ?? "error";
        string[] modDirs = Directory.GetDirectories(modDir);
        List<string> whitelist = new();
        List<string> blacklist = new();

        if (modDirs.Length == 0) 
        {
            File.Copy(Path.Combine(baseDir, "data.win"), Path.Combine(baseDir, "cache.win"), true);
            StartGame(args, baseDir);
            return;
        }

        LoadList(Path.Combine(modDir, "whitelist.txt"), whitelist);
        LoadList(Path.Combine(modDir, "blacklist.txt"), blacklist);

        Console.WriteLine("Reading data.win...");
        FileStream stream = File.OpenRead(Path.Combine(baseDir, "data.win"));
        void handler(string e) {};
        UndertaleData data = UndertaleIO.Read(stream, handler, handler);
        stream.Dispose();

        if (File.Exists(Path.Combine(baseDir, "cache.win")))
            File.Delete(Path.Combine(baseDir, "cache.win"));

        SetupInterop(data, baseDir);

        Console.WriteLine("Loading mods...");
        foreach (var modname in modDirs)
        {
            if (!whitelist.Contains(Path.GetFileName(modname)) && whitelist.Count != 0) continue;
            if (blacklist.Contains(Path.GetFileName(modname)) && blacklist.Count != 0) continue;

            Console.WriteLine($"Loading mod {Path.GetFileName(modname)}...");

            string modPath = Path.Combine(modname, Path.GetFileName(modname) + ".dll");

            if (!File.Exists(modPath))
            {
                Console.WriteLine($"Problem loading mod {modname} cant find {modPath}");
                continue;
            }

            Assembly modAssembly = Assembly.LoadFrom(modPath);
            Type modClass = modAssembly.GetTypes()
                    .FirstOrDefault(modType => modType.GetInterfaces().Contains(typeof(IGMSLMod)));

            if(modClass == null) 
            {
                Console.WriteLine($"Cant load mod {modname} cant find class inheriting IGMSLMod");
                continue;
            }

            Environment.CurrentDirectory = modname;

            IGMSLMod mod = (IGMSLMod)Activator.CreateInstance(modClass);
            mod.Load(data);

            foreach (var type in modAssembly.GetTypes())
            {
                foreach (var method in type.GetMembers())
                {
                    GmlInterop? interop = method.GetCustomAttribute<GmlInterop>();
                    if (interop == null) continue;

                    CreateInteropFunction(
                        interop.Name, 
                        interop.Argc, 
                        method.DeclaringType.Namespace,
                        method.DeclaringType.Name,
                        method.Name,
                        Path.GetFileNameWithoutExtension(modPath),
                        interopExtension,
                        data
                    );
                }
            }
        }

        Console.WriteLine("Saving modified data.win...");
        stream = File.OpenWrite(Path.Combine(baseDir, "cache.win"));
        UndertaleIO.Write(stream, data);
        stream.Dispose();

        Console.WriteLine("Launching game...");
        StartGame(args, baseDir);
    }

    public static void CreateInteropFunction(string name, ushort argc, string ns, string clazz, string method, string file, UndertaleExtensionFile extension, UndertaleData data)
    {
        currentId++;
        UndertaleExtensionFunction function = new() {
            Name = data.Strings.MakeString(name + "_interop"),
            ExtName = data.Strings.MakeString("interop_function"),
            Kind = 11,
            ID = currentId
        };
        extension.Functions.Add(function);
        string args = "";
        for (int i = 0; i < argc; i++)
        {
            args += $"argument{i}{(i != argc - 1 ? ", " : "")}";
        }
        CreateLegacyScript(data, name, $"interop_set_function(\"{file}\", \"{ns}\", \"{clazz}\", \"{method}\", {argc});\nreturn {name}_interop({args});", argc);
    }

    public static void SetupInterop(UndertaleData data, string baseDir)
    {
        UndertaleExtensionFunction setFunction = new() {
            Name = data.Strings.MakeString("interop_set_function"),
            ExtName = data.Strings.MakeString("interop_set_function"),
            Kind = 11,
            ID = currentId
        };

        UndertaleExtensionFile extensionFile = new() {
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

        interopExtension = extensionFile;
    }

    public static UndertaleCode CreateCode(UndertaleData data, UndertaleString name, out UndertaleCodeLocals locals) {
        locals = new UndertaleCodeLocals {
            Name = name
        };
        locals.Locals.Add(new UndertaleCodeLocals.LocalVar {
            Name = data.Strings.MakeString("arguments"),
            Index = 2
        });
        data.CodeLocals.Add(locals);

        UndertaleCode mainCode = new() {
            Name = name,
            LocalsCount = 1,
            ArgumentsCount = 0
        };
        data.Code.Add(mainCode);

        return mainCode;
    }

    public static UndertaleScript CreateLegacyScript(UndertaleData data, string name, string code, ushort argCount) {
        UndertaleString mainName = data.Strings.MakeString(name, out int nameIndex);
        UndertaleCode mainCode = CreateCode(data, mainName, out _);
        mainCode.ArgumentsCount = argCount;

        mainCode.ReplaceGML(code, data);

        UndertaleScript script = new() {
            Name = mainName,
            Code = mainCode
        };
        data.Scripts.Add(script);

        UndertaleFunction function = new() {
            Name = mainName,
            NameStringID = nameIndex
        };
        data.Functions.Add(function);

        return script;
    }

    public static void StartGame(string[] args, string baseDir)
    {
        ProcessStartInfo processStartInfo = new()
        {
            FileName = Path.Combine(baseDir, args[0]),
            WorkingDirectory = baseDir
        };

        processStartInfo.ArgumentList.Add("-game");
        processStartInfo.ArgumentList.Add("cache.win");
        for (int i = 1; i < args.Length; i++)
        {
            processStartInfo.ArgumentList.Add(args[i]);
        }

        Process.Start(processStartInfo);
    }

    public static void LoadList(string path, List<string> populate)
    {
        if (File.Exists(path))
        {
            populate.Add("");
            foreach (var line in File.ReadAllLines(path))
            {
                populate.Add(line);
            }
        }
    }
}