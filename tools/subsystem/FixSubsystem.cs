using System;
using System.IO;

class FixSubsystem
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: FixSubsystem <exe-file>");
            return;
        }

        string filePath = args[0];

        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite))
        using (BinaryReader br = new BinaryReader(fs))
        using (BinaryWriter bw = new BinaryWriter(fs))
        {
            // Read DOS header
            fs.Seek(0x3C, SeekOrigin.Begin);
            int peOffset = br.ReadInt32();

            // Seek to PE signature
            fs.Seek(peOffset, SeekOrigin.Begin);
            int peSig = br.ReadInt32();

            if (peSig != 0x00004550) // "PE\0\0"
            {
                Console.WriteLine("Not a valid PE file");
                return;
            }

            // Skip COFF header (20 bytes) to get to Optional Header
            fs.Seek(peOffset + 4 + 20, SeekOrigin.Begin);

            // Read magic number
            short magic = br.ReadInt16();

            // Subsystem is at offset +68 in Optional Header (for PE32+)
            int subsystemOffset = (magic == 0x20b) ? 68 : 68; // PE32+ or PE32

            fs.Seek(peOffset + 4 + 20 + subsystemOffset, SeekOrigin.Begin);

            // Read current subsystem
            short subsystem = br.ReadInt16();
            Console.WriteLine($"Current subsystem: {subsystem} ({(subsystem == 2 ? "GUI" : subsystem == 3 ? "Console" : "Other")})");

            // Write new subsystem (3 = Console)
            fs.Seek(peOffset + 4 + 20 + subsystemOffset, SeekOrigin.Begin);
            bw.Write((short)3);

            Console.WriteLine("Changed subsystem to Console (3)");
        }
    }
}
