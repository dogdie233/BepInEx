using System;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;

namespace BepInEx.Unity.IL2CPP.Hook;

/// <summary>
///     Generates a managed method that forwards to a native function pointer.
///     Replaces MonoMod 22's DetourHelper.GenerateNativeProxy, which was removed in MonoMod 25.
///     The body is emitted via raw Cecil because MonoMod 25's DMD ILGenerator proxy
///     does not support the unmanaged EmitCalli overload.
/// </summary>
internal static class NativeProxyGenerator
{
    public static MethodInfo Generate(nint target, MethodBase signature)
    {
        if (target == 0)
            throw new ArgumentNullException(nameof(target));
        if (signature == null)
            throw new ArgumentNullException(nameof(signature));

        var returnType = (signature as MethodInfo)?.ReturnType ?? typeof(void);
        var parameters = signature.GetParameters();
        var argTypes = new Type[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            argTypes[i] = parameters[i].ParameterType;

        using var dmd = new DynamicMethodDefinition($"Native<{target:X16}>", returnType, argTypes);
        var method = dmd.Definition;
        var body = method.Body;
        body.Instructions.Clear();
        var il = body.GetILProcessor();

        foreach (var param in method.Parameters)
            il.Append(Instruction.Create(OpCodes.Ldarg, param));

        il.Append(Instruction.Create(OpCodes.Ldc_I8, target));
        il.Append(Instruction.Create(OpCodes.Conv_I));

        var callSite = new CallSite(method.ReturnType)
        {
            // il2cpp native method pointers follow the C (cdecl) convention
            CallingConvention = MethodCallingConvention.C
        };
        foreach (var param in method.Parameters)
            callSite.Parameters.Add(new ParameterDefinition(param.ParameterType));

        il.Append(Instruction.Create(OpCodes.Calli, callSite));
        il.Append(Instruction.Create(OpCodes.Ret));

        return dmd.Generate();
    }
}
