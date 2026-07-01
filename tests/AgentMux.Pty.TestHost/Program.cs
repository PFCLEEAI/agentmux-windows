using System.Text;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

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
