using System.Reflection.PortableExecutable;

namespace Lab.Game;

internal sealed record GameExecutableInfo(string Version, bool NeedsLightingFix)
{
    private readonly record struct Profile(string Version, int Timestamp, int ImageSize, int FingerprintRva, bool NeedsLightingFix = false);

    // Audited executable identities shared with the LAB Plus patch adapters.
    // Read the selected EXE, never infer its version from the installation folder.
    private static readonly Profile[] Profiles =
    [
        new("1.0", 0x5e794131, 0x1e706000, 0x336210, true),
        new("1.1", 0x5ec7deca, 0x1d551000, 0x339b10, true),
        new("2.0", 0x5eeb0267, 0x1d396000, 0x303dc0, true),
        new("2.1", 0x5f203d59, 0x1e1ef000, 0x304520),
        new("3.0", 0x5f8065da, 0x1c587000, 0x316270),
        new("3.1", 0x5f90c129, 0x1e0c1000, 0x316270),
        new("4.0", 0x5fb68f01, 0x1c7e9000, 0x3172c0),
        new("4.1", 0x5fc9da94, 0x1c2eb000, 0x317360),
        new("5.0", 0x603896f3, 0x1c5bf000, 0x31f940),
        new("5.1", 0x607e69b0, 0x1b5dd000, 0x31f940),
        new("6.0", 0x60c98dd6, 0x1cb22000, 0x31f050),
        new("6.1", 0x60dff532, 0x1bc55000, 0x31f050),
        new("6.2", 0x60f26b2f, 0x1bb06000, 0x31f050),
        new("6.3", 0x6116d6b5, 0x1cd60000, 0x31f050),
        new("6.4", 0x61202c76, 0x1d77e000, 0x31f050),
        new("6.66", 0x61692c83, 0x1e4cd000, 0x32fa00),
        new("6.66 Rev 1", 0x618dfc01, 0x1c835000, 0x32fac0),
        new("6.66 Rev 1.1", 0x61d7704e, 0x1c58a000, 0x32fcb0),
        new("6.66 Rev 2", 0x6256614c, 0x1e69d000, 0x331e50),
        new("6.66 Rev 2.2", 0x64cc8a9b, 0x07421000, 0x331980),
        new("6.66 Rev 3", 0x669f0dcf, 0x0744d000, 0x331160),
        new("6.66 Rev 3.2", 0x6a7b9b8c, 0x07431000, 0x335b70)
    ];

    internal static GameExecutableInfo? Read(string gamePath)
    {
        using var stream = File.OpenRead(gamePath);
        try
        {
            using var pe = new PEReader(stream);
            if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64 ||
                pe.PEHeaders.PEHeader?.Magic != PEMagic.PE32Plus)
                return null;

            foreach (Profile profile in Profiles)
            {
                if (pe.PEHeaders.CoffHeader.TimeDateStamp != profile.Timestamp ||
                    pe.PEHeaders.PEHeader.SizeOfImage != profile.ImageSize)
                    continue;

                ReadOnlySpan<byte> expected = [0x48, 0x89, 0x5c, 0x24, 0x08, 0x57, 0x48, 0x83, 0xec, 0x20];
                if (pe.GetSectionData(profile.FingerprintRva).GetContent(0, expected.Length).AsSpan().SequenceEqual(expected))
                    return new GameExecutableInfo(profile.Version, profile.NeedsLightingFix);
            }
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }
}
