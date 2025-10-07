using Kairc;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            var options = CommandLineParser.Parse(args);
            var compiler = new Compiler(options);
            compiler.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("KAIR Compiler v0.1.0");
        Console.WriteLine("Kernel Assembly IR - Cross-platform assembly abstraction layer");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  kair <input> [target] [options]        # Output assembly (.asm)");
        Console.WriteLine("  kair build <input> [target] [options]  # Output executable (.exe)");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <input>          Input file (.kir or .asm)");
        Console.WriteLine("  [target]         Target platform (optional)");
        Console.WriteLine("                   x64-win, x64-linux, arm64-linux, arm64-mac");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -o <output>      Output file path (default: auto-generated)");
        Console.WriteLine("  --nasm <path>    Path to NASM executable");
        Console.WriteLine("  --golink <path>  Path to GoLink executable");
        Console.WriteLine("  --emit-comments  Emit KIR source as comments in assembly");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  kair program.kir                    # → program.asm");
        Console.WriteLine("  kair program.kir x64-win            # → program.asm (x64-win target)");
        Console.WriteLine("  kair program.kir -o out.asm         # → out.asm");
        Console.WriteLine("  kair program.kir --emit-comments    # → program.asm (with comments)");
        Console.WriteLine("  kair build program.kir              # → program.exe");
        Console.WriteLine("  kair build program.kir x64-linux    # → program (Linux x64)");
        Console.WriteLine("  kair build program.kir -o app.exe   # → app.exe");
    }
}

