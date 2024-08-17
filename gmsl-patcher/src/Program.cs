using System.Reflection;
using UndertaleModLib;
using System.Diagnostics;
using UndertaleModLib.Models;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
		var gameExe = args[0];

		var loaderVer = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
		Logger.Info($"VSLoader {loaderVer} - {gameExe}");

		if (modDirs.Length == 0)
		{
			Logger.Info($"No mods installed in {modDir}! Press enter to launch game...");
			Console.ReadLine();
			File.Copy(Path.Combine(baseDir!, "data.win"), Path.Combine(baseDir!, "cache.win"), true);
			StartGame(args, baseDir!);
			return;
		}

		LoadList(Path.Combine(modDir, "whitelist.txt"), _whitelist);
		LoadList(Path.Combine(modDir, "blacklist.txt"), _blacklist);

		var statePath = Path.Combine(gmslDir!, "state");

		var prevLoaderState = "";
		if (File.Exists(statePath)) prevLoaderState = File.ReadAllText(statePath);
		Logger.Info($"Previous loader state: {prevLoaderState}");

		Logger.Info("Hashing data.win...");
		var stream = File.OpenRead(Path.Combine(baseDir!, "data.win"));
		var dataHash = HashFile(stream);
		Logger.Info($"data.win hash: {dataHash}");

		stream.Position = 0;

		Logger.Info("Loading modinfo files...");

		string loaderState = $"{Path.GetFileNameWithoutExtension(gameExe)}+data.win[{dataHash}]+VSLoader[{loaderVer}]";

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

		Logger.Info("Preparing mods...");

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
			mod.Assembly = modAssembly;
			var modVerAttr = modAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
			var modDllStream = File.OpenRead(modPath);
			var modDllHash = HashFile(modDllStream);
			modDllStream.Dispose();
			if (modVerAttr?.InformationalVersion != null)
			{
				loaderState += $"+{mod.Name}[{mod.Version}-{modVerAttr.InformationalVersion}+{modDllHash}]";
			}
			else
			{
				Logger.Warn($"Mod {mod.ID} has no assembly version! Cannot produce a reliable hash for assets!");
				loaderState += $"+{mod.Name}[{mod.Version}-{modDllHash}]";
			}

			foreach (var type in modAssembly.GetTypes())
			{
				// TODO this is absolutely horrible
				if (type.GetMethod("Patch") != null && type.GetMethod("Start") != null)
				{
					try
					{
						mod.Instance = (GMSLMod)Activator.CreateInstance(type)!;
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

						stream.Dispose();

						StartGame(args, baseDir!, false);
						return;
					}
				}
			}
		}
		Logger.Info($"Loader state: {loaderState}");

		if (loaderState != prevLoaderState)
		{
			Logger.Info("Loader state differs, rebuilding data.win...");

			Logger.Info("Reading data.win...");
			var data = UndertaleIO.Read(stream, Logger.Error, msg =>
			{
				Logger.Info($"[UMT]: {msg}");
			});
			stream.Dispose();

			if (File.Exists(Path.Combine(baseDir!, "cache.win")))
				File.Delete(Path.Combine(baseDir!, "cache.win"));

			// SetupInterop(data, baseDir!);

			foreach (var mod in loadOrder)
			{
				try
				{
					mod.Instance.Prepare(data, mod, mod.ModDir);
					mod.Instance.Patch();
					mod.Instance.Finalize();
				}
				catch (Exception ex)
				{
					Logger.Info($"The mod {mod.ID} had an error while patching");
					Logger.Info("It has been logged to a file and the normal game will launch");
					Logger.Error(ex.ToString());

					if (File.Exists("error.txt")) File.Delete("error.txt");

					File.WriteAllText("error.txt", ex.ToString());

					if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) ShowWindow(GetConsoleWindow(), 1);

					Logger.Info("Please press enter to continue launching...");
					Console.ReadLine();

					stream.Dispose();

					StartGame(args, baseDir!, false);
					return;
				}

				//foreach (var type in mod.Assembly.GetTypes())
				//{
				//	foreach (var method in type.GetMembers())
				//	{
				//		var interop = method.GetCustomAttribute<GmlInterop>();
				//		if (interop == null) continue;

				//		CreateInteropFunction(
				//			interop,
				//			method,
				//			mod.Name,
				//			data
				//		);
				//	}
				//}
			}

			Logger.Info("Saving modified data.win...");
			stream = File.OpenWrite(Path.Combine(baseDir!, "cache.win"));
			UndertaleIO.Write(stream, data, msg =>
			{
				Logger.Info($"[UMT]: {msg}");
			});
			stream.Dispose();
		} else
		{
			Logger.Info("Loader state hasn't changed, skipping data.win rebuild.");
			stream.Dispose();
		}

		foreach (var mod in loadOrder)
		{
			try
			{
				mod.Instance.Start();
			}
			catch (Exception ex)
			{
				Logger.Info($"The mod {mod.ID} had an error while starting");
				Logger.Info("It has been logged to a file and the normal game will launch");
				Logger.Error(ex.ToString());

				if (File.Exists("error.txt")) File.Delete("error.txt");

				File.WriteAllText("error.txt", ex.ToString());

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) ShowWindow(GetConsoleWindow(), 1);

				Logger.Info("Please press enter to continue launching...");
				Console.ReadLine();

				stream.Dispose();

				StartGame(args, baseDir!, false);
				return;
			}
		}

		Logger.Info("Writing new loader state...");
		File.WriteAllText(statePath, loaderState);

		Logger.Info("Launching game...");
		// Console.ReadLine();
		StartGame(args, baseDir!);
	}

	private static string HashFile(FileStream stream)
	{
		using (var md5 = MD5.Create())
		{
			var hash = md5.ComputeHash(stream);
			return Convert.ToHexString(hash).ToLower();
		}
	}

	private static string HashString(string str)
	{
		using (var md5 = MD5.Create())
		{
			var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(str));
			return Convert.ToHexString(hash).ToLower();
		}
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
