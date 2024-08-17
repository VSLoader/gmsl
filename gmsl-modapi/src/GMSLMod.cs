using Underanalyzer;
using UndertaleModLib;
using UndertaleModLib.Models;
using GMSL.Hooker;

namespace GMSL;

public abstract class GMSLMod
{
	public string modDir;
	public string scriptsDir;
	public string assetsDir;
	private Dictionary<string, string> scripts = new Dictionary<string, string>();

	public UndertaleData moddingData;
	public ModInfo modInfo;

	public abstract void Patch();
	public abstract void Start();

	// TODO make this automatic? or just use a constructor maybe
	public void Prepare(UndertaleData data, ModInfo info, string dir)
	{
		Logger.Logger.Info("Preparing mod");
		moddingData = data;
		modInfo = info;
		modDir = dir;
		scriptsDir = Path.Combine(dir, "scripts");
		assetsDir = Path.Combine(dir, "assets");
		if (Path.Exists(scriptsDir)) {
			Logger.Logger.Info($"Loading scripts from {scriptsDir}");
			LoadCodeFromFiles(scriptsDir);
		}
	}

	public void Finalize()
	{
		Logger.Logger.Info("Finalizing mod");
		moddingData.FinalizeHooks();
	}

	public UndertaleGameObject NewObject(string objectName, UndertaleSprite sprite = null, bool visible = true, bool solid = false, bool persistent = false, UndertaleGameObject parentObject = null)
	{
		UndertaleString name = new UndertaleString(objectName);
		UndertaleGameObject newObject = new UndertaleGameObject()
		{
			Sprite = sprite,
			Persistent = persistent,
			Visible = visible,
			Solid = solid,
			Name = name,
			ParentId = parentObject
		};

		moddingData.Strings.Add(name);
		moddingData.GameObjects.Add(newObject);

		return newObject;
	}

	public UndertaleRoom.GameObject AddObjectToRoom(string roomName, UndertaleGameObject objectToAdd, string layerName)
	{
		UndertaleRoom room = GetRoomFromData(roomName);

		UndertaleRoom.GameObject object_inst = new UndertaleRoom.GameObject()
		{
			InstanceID = moddingData.GeneralInfo.LastObj,
			ObjectDefinition = objectToAdd,
			X = -120,
			Y = -120
		};
		moddingData.GeneralInfo.LastObj++;

		room.Layers.First(layer => layer.LayerName.Content == layerName).InstancesData.Instances.Add(object_inst);


		room.GameObjects.Add(object_inst);

		return object_inst;
	}


	public UndertaleGameObject GetObjectFromData(string name)
	{
		return moddingData.GameObjects.ByName(name);
	}
	public UndertaleSprite GetSpriteFromData(string name)
	{
		return moddingData.Sprites.ByName(name);
	}
	public UndertaleRoom GetRoomFromData(string name)
	{
		return moddingData.Rooms.ByName(name);
	}
	public UndertaleCode GetObjectCodeFromData(string name)
	{
		return moddingData.Code.ByName(name);
	}
	public UndertaleFunction GetFunctionFromData(string name)
	{
		return moddingData.Functions.ByName(name);
	}
	public UndertaleScript GetScriptFromData(string name)
	{
		return moddingData.Scripts.ByName(name);
	}
	public UndertaleSound GetSoundFromData(string name)
	{
		return moddingData.Sounds.ByName(name);
	}
	public UndertaleVariable GetVariableFromData(string name)
	{
		return moddingData.Variables.ByName(name);
	}



	public void HookFunctionFromFile(string path, string function)
	{
		string value = "";
		if (scripts.TryGetValue(path, out value))
		{
			Logger.Logger.Info($"loading {path}");
			moddingData.HookFunction(function, value);
		}
		else
		{
			Logger.Logger.Warn($"Couldn't hook function {path}, it wasn't in the files dictionary.");
		}
	}
	public void CreateFunctionFromFile(string path, string function, ushort argumentCount = 0)
	{
		string value = "";
		if (scripts.TryGetValue(path, out value))
		{
			Logger.Logger.Info($"loading {path}");
			moddingData.CreateFunction(function, value, argumentCount);
		}
		else
		{
			Logger.Logger.Warn($"Couldn't create function {path}, it wasn't in the files dictionary.");
		}
	}

	public void HookCodeFromFile(string path, string function)
	{
		string value = "";
		if (scripts.TryGetValue(path, out value))
		{
			Logger.Logger.Info($"loading {path}");
			moddingData.HookCode(function, value);
		}
		else
		{
			Logger.Logger.Warn($"Couldn't hook object script {path}, it wasn't in the files dictionary.");
		}
	}


	public void CreateObjectCodeFromFile(string path, string objName, EventType eventType)
	{
		string value = "";
		UndertaleGameObject obj = moddingData.GameObjects.ByName(objName);

		if (scripts.TryGetValue(path, out value))
		{
			obj.EventHandlerFor(eventType, moddingData)
			.ReplaceGmlSafe(value, moddingData);
		}
		else
		{
			Logger.Logger.Warn($"Couldn't change/create object script {path}, it wasn't in the files dictionary.");
		}
	}

	public void CreateObjectCodeFromFile(string path, string objName, EventType eventType, EventSubtypeDraw EventSubtype)
	{
		string value = "";
		UndertaleGameObject obj = moddingData.GameObjects.ByName(objName);

		if (scripts.TryGetValue(path, out value))
		{
			obj.EventHandlerFor(eventType, EventSubtype, moddingData)
			.ReplaceGmlSafe(value, moddingData);
		}
		else
		{
			Logger.Logger.Warn($"Couldn't change/create object script {path}, it wasn't in the files dictionary.");
		}
	}
	public void CreateObjectCodeFromFile(string path, string objName, EventType eventType, uint EventSubtype)
	{
		string value = "";
		UndertaleGameObject obj = moddingData.GameObjects.ByName(objName);

		if (scripts.TryGetValue(path, out value))
		{
			obj.EventHandlerFor(eventType, EventSubtype, moddingData)
			.ReplaceGmlSafe(value, moddingData);
		}
		else
		{
			Logger.Logger.Warn($"Couldn't change/create object script {path}, it wasn't in the files dictionary.");
		}
	}
	public void CreateObjectCodeFromFile(string path, string objName, EventType eventType, EventSubtypeKey EventSubtype)
	{
		string value = "";
		UndertaleGameObject obj = moddingData.GameObjects.ByName(objName);

		if (scripts.TryGetValue(path, out value))
		{
			obj.EventHandlerFor(eventType, EventSubtype, moddingData)
			.ReplaceGmlSafe(value, moddingData);
		}
		else
		{
			Console.WriteLine($"[vsCoreMod]: Couldn't change/create object script {path}, it wasn't in the files dictionary.");
		}
	}

	public void CreateObjectCodeFromFile(string path, string objName, EventType eventType, EventSubtypeMouse EventSubtype)
	{
		string value = "";
		UndertaleGameObject obj = moddingData.GameObjects.ByName(objName);

		if (scripts.TryGetValue(path, out value))
		{
			obj.EventHandlerFor(eventType, EventSubtype, moddingData)
			.ReplaceGmlSafe(value, moddingData);
		}
		else
		{
			Logger.Logger.Warn($"Couldn't change/create object script {path}, it wasn't in the files dictionary.");
		}
	}


	public void CreateObjectCodeFromFile(string path, string objName, EventType eventType, EventSubtypeOther EventSubtype)
	{
		string value = "";
		UndertaleGameObject obj = moddingData.GameObjects.ByName(objName);

		if (scripts.TryGetValue(path, out value))
		{
			obj.EventHandlerFor(eventType, EventSubtype, moddingData)
			.ReplaceGmlSafe(value, moddingData);
		}
		else
		{
			Logger.Logger.Warn($"Couldn't change/create object script {path}, it wasn't in the files dictionary.");
		}
	}

	public void CreateObjectCodeFromFile(string path, string objName, EventType eventType, EventSubtypeStep EventSubtype)
	{
		string value = "";
		UndertaleGameObject obj = moddingData.GameObjects.ByName(objName);

		if (scripts.TryGetValue(path, out value))
		{
			obj.EventHandlerFor(eventType, EventSubtype, moddingData)
			.ReplaceGmlSafe(value, moddingData);
		}
		else
		{
			Logger.Logger.Warn($"Couldn't change/create object script {path}, it wasn't in the files dictionary.");
		}
	}

	public void LoadCodeFromFiles(string path)
	{
		Dictionary<string, string> files = new Dictionary<string, string>();
		string[] codeF = Directory.GetFiles(path, "*.gml");
		Logger.Logger.Info($"Loading code from {path}");
		foreach (string f in codeF)
		{
			if (!files.ContainsKey(Path.GetFileName(f)))
			{
				files.Add(Path.GetFileName(f), File.ReadAllText(f));
			}
		}
		scripts = files;
	}
}