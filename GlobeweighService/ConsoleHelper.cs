using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class ConsoleHelper
{
    [DllImport("kernel32.dll")]
    public static extern bool AllocConsole();

    public static void EnsureConsole()
    {
        AllocConsole();

        // Re-open the console streams explicitly
        var outStream = new FileStream(
            new Microsoft.Win32.SafeHandles.SafeFileHandle(
                GetStdHandle(-11), false), FileAccess.Write);

        var writer = new StreamWriter(outStream, Encoding.UTF8) { AutoFlush = true };
        Console.SetOut(writer);
        Console.SetError(writer);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetStdHandle(int nStdHandle); // -11 = STD_OUTPUT_HANDLE
}