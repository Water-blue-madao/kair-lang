using System;
using System.IO;

class FixSubsystem
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("使い方: FixSubsystem <exe-file>");
            return;
        }

        string filePath = args[0];

        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite))
        using (BinaryReader br = new BinaryReader(fs))
        using (BinaryWriter bw = new BinaryWriter(fs))
        {
            // DOSヘッダーを読み込む
            fs.Seek(0x3C, SeekOrigin.Begin);
            int peOffset = br.ReadInt32();

            // PEシグネチャ位置へ移動
            fs.Seek(peOffset, SeekOrigin.Begin);
            int peSig = br.ReadInt32();

            if (peSig != 0x00004550) // "PE\0\0"
            {
                Console.WriteLine("有効なPEファイルではありません");
                return;
            }

            // オプショナルヘッダーに進むためCOFFヘッダー(20バイト)をスキップ
            fs.Seek(peOffset + 4 + 20, SeekOrigin.Begin);

            // マジックナンバーを読み込む
            short magic = br.ReadInt16();

            // サブシステムはオプショナルヘッダー内のオフセット+68 (PE32+ の場合)
            int subsystemOffset = (magic == 0x20b) ? 68 : 68; // PE32+ または PE32

            fs.Seek(peOffset + 4 + 20 + subsystemOffset, SeekOrigin.Begin);

            // 現在のサブシステムを読み込む
            short subsystem = br.ReadInt16();
            Console.WriteLine($"現在のサブシステム: {subsystem} ({(subsystem == 2 ? "GUI" : subsystem == 3 ? "Console" : "Other")})");

            // 新しいサブシステムを書き込む (3 = Console)
            fs.Seek(peOffset + 4 + 20 + subsystemOffset, SeekOrigin.Begin);
            bw.Write((short)3);

            Console.WriteLine("サブシステムを Console (3) に変更しました");
        }
    }
}
