using Xunit;
using VrlfMods.Patches;

namespace VrlfMods.Tests;

public class Lzo1xTests
{
    [Fact] public void Decompresses_literal_run_then_eof()
    {
        // Minimal hand-verified LZO1X stream: first-op 0x15 (literal run len 4),
        // 4 literals "ABCD", then EOF marker 0x11,0x00,0x00.
        byte[] src = { 0x15, 0x41, 0x42, 0x43, 0x44, 0x11, 0x00, 0x00 };
        var outp = Lzo1x.Decompress(src, 4);
        Assert.Equal(new byte[] { 0x41, 0x42, 0x43, 0x44 }, outp);
    }

    [Fact] public void Wrong_expected_length_throws()
    {
        byte[] src = { 0x15, 0x41, 0x42, 0x43, 0x44, 0x11, 0x00, 0x00 };
        Assert.ThrowsAny<System.Exception>(() => Lzo1x.Decompress(src, 5));
    }
}
