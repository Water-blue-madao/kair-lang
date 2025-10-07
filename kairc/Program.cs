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
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return 1;
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("KAIR Compiler v0.1.0");
        Console.WriteLine("Kernel Assembly IR - クロスプラットフォーム向けアセンブリ抽象化レイヤー");
        Console.WriteLine();
        Console.WriteLine("使い方:");
        Console.WriteLine("  kair <input> [target] [options]        # アセンブリ (.asm) を出力");
        Console.WriteLine("  kair build <input> [target] [options]  # 実行ファイル (.exe) を出力");
        Console.WriteLine();
        Console.WriteLine("引数:");
        Console.WriteLine("  <input>          入力ファイル (.kir または .asm)");
        Console.WriteLine("  [target]         ターゲットプラットフォーム（省略可）");
        Console.WriteLine("                   x64-win, x64-linux, arm64-linux, arm64-mac");
        Console.WriteLine();
        Console.WriteLine("オプション:");
        Console.WriteLine("  -o <output>      出力ファイルパス (既定: 自動生成)");
        Console.WriteLine("  --nasm <path>    NASM 実行ファイルのパス");
        Console.WriteLine("  --golink <path>  GoLink 実行ファイルのパス");
        Console.WriteLine("  --emit-comments  アセンブリに KIR ソースをコメントとして出力");
        Console.WriteLine();
        Console.WriteLine("使用例:");
        Console.WriteLine("  kair program.kir                    # → program.asm");
        Console.WriteLine("  kair program.kir x64-win            # → program.asm (ターゲット x64-win)");
        Console.WriteLine("  kair program.kir -o out.asm         # → out.asm");
        Console.WriteLine("  kair program.kir --emit-comments    # → program.asm (コメント付き)");
        Console.WriteLine("  kair build program.kir              # → program.exe");
        Console.WriteLine("  kair build program.kir x64-linux    # → program (Linux x64)");
        Console.WriteLine("  kair build program.kir -o app.exe   # → app.exe");
    }
}

