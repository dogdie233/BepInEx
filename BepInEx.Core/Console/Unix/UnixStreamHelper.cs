using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BepInEx.Unix;

internal static class UnixStreamHelper
{
    // MonoMod 22's DynDllImport was removed in MonoMod 25; these libc exports are
    // stable enough to bind via plain P/Invoke ("libc" resolves on glibc/musl Linux
    // and libSystem on macOS).
    [DllImport("libc", SetLastError = true)]
    public static extern int dup(int fd);

    [DllImport("libc", SetLastError = true)]
    public static extern int fclose(IntPtr stream);

    [DllImport("libc", SetLastError = true)]
    public static extern IntPtr fdopen(int fd, string mode);

    [DllImport("libc", SetLastError = true)]
    public static extern int fflush(IntPtr stream);

    [DllImport("libc", SetLastError = true)]
    public static extern IntPtr fread(IntPtr ptr, IntPtr size, IntPtr nmemb, IntPtr stream);

    [DllImport("libc", SetLastError = true)]
    public static extern int fwrite(IntPtr ptr, IntPtr size, IntPtr nmemb, IntPtr stream);

    [DllImport("libc", SetLastError = true)]
    public static extern int isatty(int fd);

    public static Stream CreateDuplicateStream(int fileDescriptor)
    {
        var newFd = dup(fileDescriptor);

        return new UnixStream(newFd, FileAccess.Write);
    }
}
