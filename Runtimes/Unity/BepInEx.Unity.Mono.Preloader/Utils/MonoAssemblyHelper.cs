using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Preloader.Core;
using Mono.Cecil;
using MonoMod.Utils;

namespace BepInEx.Unity.Mono.Preloader.Utils;

internal static class MonoAssemblyHelper
{
    static MonoAssemblyHelper()
    {
        // We can't use mono's __Internal because on Windows it will use GetModuleHandleW(NULL) that will
        // in turn return the module to the EXE and not mono.dll (at least on Unity versions < 5).
        // MonoMod 25 removed DynDllImport; resolve the exports manually.
        var candidates = new List<string>();
        var monoLibPath = EnvVars.DOORSTOP_MONO_LIB_PATH;
        if (!string.IsNullOrEmpty(monoLibPath))
            candidates.Add(monoLibPath);
        candidates.Add("mono");

        foreach (var candidate in candidates)
        {
            var module = NativeLibLoader.LoadLibrary(candidate);
            if (module == 0)
                continue;

            var imageOpenPtr = NativeLibLoader.GetSymbol(module, "mono_image_open_from_data_with_name");
            var assemblyLoadPtr = NativeLibLoader.GetSymbol(module, "mono_assembly_load_from_full");
            if (imageOpenPtr == 0 || assemblyLoadPtr == 0)
                continue;

            imageOpen = (ImageOpenDelegate) Marshal.GetDelegateForFunctionPointer(
                imageOpenPtr, typeof(ImageOpenDelegate));
            assemblyLoad = (AssemblyLoadDelegate) Marshal.GetDelegateForFunctionPointer(
                assemblyLoadPtr, typeof(AssemblyLoadDelegate));
            break;
        }
    }

    private static ReadAssemblyResult ReadAssemblyData(string filePath)
    {
        return ReadAssemblyData(File.ReadAllBytes(filePath));
    }

    private static ReadAssemblyResult ReadAssemblyData(byte[] assemblyData)
    {
        using var ms = new MemoryStream(assemblyData);
        using var ad = AssemblyDefinition.ReadAssembly(ms);

        return new ReadAssemblyResult
        {
            AssemblyName = ad.Name.Name,
            AssemblyData = assemblyData
        };
    }

    private static Assembly GetAssemblyByName(string assemblyName)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => Utility.TryParseAssemblyName(a.FullName, out var name) &&
                                             name.Name == assemblyName);
    }

    public static bool TryResolveDllAssembly(AssemblyName assemblyName, string directory, out Assembly assembly) =>
        Utility.TryResolveDllAssembly(assemblyName, directory, Load, out assembly);

    public static Assembly LoadFromMemory(byte[] data, string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        return ReadAssemblyData(data).Load(fullPath);
    }

    public static Assembly Load(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        return ReadAssemblyData(fullPath).Load(fullPath);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ImageOpenDelegate(nint data,
                                            uint dataLength,
                                            bool needCopy,
                                            out MonoImageOpenStatus status,
                                            bool refOnly,
                                            [MarshalAs(UnmanagedType.LPStr)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint AssemblyLoadDelegate(nint image,
                                               [MarshalAs(UnmanagedType.LPStr)] string fileName,
                                               out MonoImageOpenStatus status,
                                               bool refOnly);

    private enum MonoImageOpenStatus
    {
        MONO_IMAGE_OK,
        MONO_IMAGE_ERROR_ERRNO,
        MONO_IMAGE_MISSING_ASSEMBLYREF,
        MONO_IMAGE_IMAGE_INVALID
    }

    private class ReadAssemblyResult
    {
        public byte[] AssemblyData;
        public string AssemblyName;

        public unsafe Assembly Load(string fullPath)
        {
            var assembly = GetAssemblyByName(AssemblyName);
            if (assembly != null)
                return assembly;

            fixed (byte* data = &AssemblyData[0])
            {
                var image = imageOpen((nint) data, (uint) AssemblyData.Length, true,
                                      out var status, false, fullPath);
                if (status != MonoImageOpenStatus.MONO_IMAGE_OK)
                    throw new BadImageFormatException($"Failed to load image {fullPath}: {status}");
                assemblyLoad(image, fullPath, out status, false);
                if (status != MonoImageOpenStatus.MONO_IMAGE_OK)
                    throw new BadImageFormatException($"Failed to load assembly {fullPath}: {status}");
                return GetAssemblyByName(AssemblyName);
            }
        }
    }
#pragma warning disable CS0649
    private static ImageOpenDelegate imageOpen;

    private static AssemblyLoadDelegate assemblyLoad;
#pragma warning restore CS0649

    // Minimal cross-platform dynamic library loader replacing MonoMod 22's DynDll.
    private static class NativeLibLoader
    {
        private const int RTLD_NOW = 2;

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = true)]
        private static extern nint LoadLibraryA(string path);

        [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = true)]
        private static extern nint GetProcAddress(nint module, string name);

        // Only invoked on Unix; DllImport resolution is lazy so these never load on Windows.
        [DllImport("__Internal", EntryPoint = "dlopen")]
        private static extern nint dlopen(string file, int flags);

        [DllImport("__Internal", EntryPoint = "dlsym")]
        private static extern nint dlsym(nint handle, string name);

        public static nint LoadLibrary(string path)
        {
            if (PlatformHelper.Is(Platform.Windows))
                return LoadLibraryA(path);
            return dlopen(path, RTLD_NOW);
        }

        public static nint GetSymbol(nint module, string name)
        {
            if (PlatformHelper.Is(Platform.Windows))
                return GetProcAddress(module, name);
            return dlsym(module, name);
        }
    }
}
