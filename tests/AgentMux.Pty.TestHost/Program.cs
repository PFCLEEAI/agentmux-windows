using System.Text;
using System.Runtime.InteropServices;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

if (args.Contains("--raw-bytes", StringComparer.OrdinalIgnoreCase))
{
    RunRawByteEcho();
    return;
}

if (args.Contains("--cleanup-cooperative", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine($"AGENTMUX_CLEANUP_COOPERATIVE_READY:{Environment.ProcessId}");
    Console.Out.Flush();
    while (Console.ReadLine() is not null)
    {
    }

    return;
}

if (args.Contains("--cleanup-stubborn", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine($"AGENTMUX_CLEANUP_STUBBORN_READY:{Environment.ProcessId}");
    Console.Out.Flush();
    Thread.Sleep(Timeout.InfiniteTimeSpan);
    return;
}

Console.WriteLine("AGENTMUX_READY");
Console.Out.Flush();

while (Console.ReadLine() is { } line)
{
    if (line.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("AGENTMUX_EXIT");
        Console.Out.Flush();
        return;
    }

    if (line.Equals("size", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"SIZE:{Console.WindowWidth}x{Console.WindowHeight}");
        Console.Out.Flush();
        continue;
    }

    Console.WriteLine($"ECHO:{line}");
    Console.Out.Flush();
}

static void RunRawByteEcho()
{
    TryEnableRawConsoleInput();
    Console.WriteLine("AGENTMUX_RAW_READY");
    Console.Out.Flush();

    using var input = Console.OpenStandardInput();
    while (true)
    {
        var value = input.ReadByte();
        if (value < 0)
        {
            return;
        }

        Console.WriteLine($"RAW:{value:X2}");
        Console.Out.Flush();
    }
}

static void TryEnableRawConsoleInput()
{
    var inputHandle = NativeMethods.GetStdHandle(NativeMethods.StdInputHandle);
    if (inputHandle == IntPtr.Zero || inputHandle == new IntPtr(-1))
    {
        return;
    }

    if (!NativeMethods.GetConsoleMode(inputHandle, out var mode))
    {
        return;
    }

    var rawMode = (mode | NativeMethods.EnableVirtualTerminalInput)
        & ~(NativeMethods.EnableLineInput | NativeMethods.EnableEchoInput | NativeMethods.EnableProcessedInput);
    _ = NativeMethods.SetConsoleMode(inputHandle, rawMode);
}

internal static class NativeMethods
{
    public const int StdInputHandle = -10;
    public const uint EnableProcessedInput = 0x0001;
    public const uint EnableLineInput = 0x0002;
    public const uint EnableEchoInput = 0x0004;
    public const uint EnableVirtualTerminalInput = 0x0200;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
