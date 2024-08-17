
using UndertaleModLib;
using UndertaleModLib.Models;

namespace GMSL.Hooker;

public static class HookExtensions {
    private static readonly Dictionary<string, UndertaleCode> originalCodes = new();
    private static Dictionary<string, (string, ushort)> hooksToWrite = new Dictionary<string, (string, ushort)>{};
    private static UndertaleCode MoveCodeForHook(UndertaleData data, string cloneName, UndertaleCode cloning,
        UndertaleCodeLocals cloningLocals) {
        UndertaleCode codeClone = new() {
            Name = data.Strings.MakeString(cloneName),
            LocalsCount = cloning.LocalsCount,
            ArgumentsCount = cloning.ArgumentsCount,
            // WeirdLocalsFlag = cloning.WeirdLocalsFlag,
            WeirdLocalFlag = cloning.WeirdLocalFlag
        };
        codeClone.Replace(cloning.Instructions);
        data.Code.Insert(data.Code.IndexOf(cloning) + 1, codeClone);
        data.Scripts.Add(new UndertaleScript {
            Name = codeClone.Name,
            Code = codeClone
        });

        foreach(UndertaleCode childEntry in cloning.ChildEntries) {
            childEntry.ParentEntry = codeClone;
            codeClone.ChildEntries.Add(childEntry);
        }

        cloning.ChildEntries.Clear();
        cloning.Instructions.Clear();
        cloning.UpdateAddresses();

        UndertaleCodeLocals localsClone = new() {
            Name = codeClone.Name
        };
        foreach(UndertaleCodeLocals.LocalVar localVar in cloningLocals.Locals)
            localsClone.Locals.Add(new UndertaleCodeLocals.LocalVar {
                Name = localVar.Name,
                Index = localVar.Index
            });
        cloningLocals.Locals.Clear();
        data.CodeLocals.Add(localsClone);

        return codeClone;
    }

    public static void HookCode(this UndertaleData data, string code, string hook) =>
        data.Code.ByName(code).Hook(data, data.CodeLocals.ByName(code), hook);

    public static void Hook(this UndertaleCode code, UndertaleData data, UndertaleCodeLocals locals, string hook) {
        string originalName = GetDerivativeName(code.Name.Content, "orig");
        originalCodes.TryAdd(code.Name.Content, MoveCodeForHook(data, originalName, code, locals));
        code.ReplaceGmlSafe(hook.Replace("#orig#", $"{originalName}"), data);
    }

    public static void HookFunction(this UndertaleData data, string function, string hook) {
        ushort argCount = data.Code.ByName("gml_Script_" + function).ArgumentsCount;
        HardHook(data, function, hook, argCount);
    }

    public delegate void AsmHook(UndertaleCode code, UndertaleCodeLocals locals);

    public static void HookAsm(this UndertaleData data, string name, AsmHook hook) {
        if(!originalCodes.TryGetValue(name, out UndertaleCode? code))
            code = data.Code.ByName(name);
        code.Hook(data.CodeLocals.ByName(code.Name.Content), hook);
    }

    public static void Hook(this UndertaleCode code, UndertaleCodeLocals locals, AsmHook hook) {
        hook(code, locals);
        code.UpdateAddresses();
    }

    public static void HardHook(this UndertaleData data, string function, string hook, ushort argCount) {
        function = "gml_Script_" + function;
        string hookName = GetDerivativeName(function, "hook");
        UndertaleCode hookCode = data.CreateLegacyScript(hookName, hook.Replace("#orig#", function), argCount).Code;
        hooksToWrite.Add(function, (hookName, argCount));
    }

    public static void FinalizeHooks(this UndertaleData data){
        foreach(UndertaleCode code in data.Code) {
            if(code.ParentEntry is not null) continue;
            code.Hook(data.CodeLocals.ByName(code.Name.Content), (origCode, locals) => {
                if (origCode.Name.Content.StartsWith("gmml_")) {
                    Logger.Logger.Info("skipping hook rewrite for " + origCode.Name.Content);
                    return;
                }
                foreach(string function in hooksToWrite.Keys) {
                    var newHookInfo = hooksToWrite[function];
                    string hookName = newHookInfo.Item1;
                    ushort argCount = newHookInfo.Item2;
                    AsmCursor cursor = new(data, origCode, locals);
                    while(cursor.GotoNext($"call.i {function}(argc={argCount})")){
                        cursor.Replace($"call.i {hookName}(argc={argCount})");
                    }
                    AsmCursor cursor2 = new(data, origCode, locals);
                    while(cursor2.GotoNext($"push.i {function}")){
                        cursor2.Replace($"push.i {hookName}");
                    }
                }
            });
        }
    }

    public static void HardHook(this UndertaleFunction function, UndertaleData data, string hook, ushort argCount) =>
        data.HardHook(function.Name.Content, hook, argCount);

    public static Dictionary<string, UndertaleVariable> GetLocalVars(this UndertaleCodeLocals locals,
        UndertaleData data) => locals.Locals.ToDictionary(local => local.Name.Content, local =>
        data.Variables.First(variable => variable.VarID == (int)local.Index));

    private static string GetDerivativeName(string name, string suffix) =>
        $"gmml_{name}_{suffix}_{Guid.NewGuid().ToString().Replace('-', '_')}";
}
