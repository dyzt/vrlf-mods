namespace VrlfMods.Patches;

public static class Ue3Package
{
    // UE3 compressed package -> canonical uncompressed image (Gildor-equivalent).
    public static byte[] Decompress(byte[] orig)
    {
        int nch = BitConverter.ToInt32(orig, 0x71);
        var chunks = new (uint uo, uint us, uint co, uint cs)[nch];
        for (int i = 0; i < nch; i++)
        {
            int b = 0x75 + 16 * i;
            chunks[i] = (BitConverter.ToUInt32(orig, b), BitConverter.ToUInt32(orig, b + 4),
                         BitConverter.ToUInt32(orig, b + 8), BitConverter.ToUInt32(orig, b + 12));
        }
        long total = (long)chunks[nch - 1].uo + chunks[nch - 1].us;
        var img = new byte[total];
        Array.Copy(orig, 0, img, 0, 0x75);
        img[0x18] &= unchecked((byte)~0x02);                 // clear PKG_StoreCompressed
        BitConverter.GetBytes((uint)0).CopyTo(img, 0x6D);    // CompressionFlags = 0
        BitConverter.GetBytes((uint)0).CopyTo(img, 0x71);    // chunk count = 0
        int tableEnd = 0x75 + 16 * nch;
        int firstData = (int)chunks[0].co;
        Array.Copy(orig, tableEnd, img, 0x75, firstData - tableEnd);

        foreach (var (uo, us, co, cs) in chunks)
        {
            uint magic = BitConverter.ToUInt32(orig, (int)co);
            uint blocksize = BitConverter.ToUInt32(orig, (int)co + 4);
            uint usz = BitConverter.ToUInt32(orig, (int)co + 12);
            if (magic != 0x9E2A83C1 || usz != us)
                throw new InvalidDataException($"bad chunk header at 0x{co:x}");
            int nblocks = (int)((usz + blocksize - 1) / blocksize);
            var pairs = new (uint cbs, uint ubs)[nblocks];
            for (int i = 0; i < nblocks; i++)
            {
                int pb = (int)co + 16 + 8 * i;
                pairs[i] = (BitConverter.ToUInt32(orig, pb), BitConverter.ToUInt32(orig, pb + 4));
            }
            int p = (int)co + 16 + 8 * nblocks;
            int outPos = (int)uo;
            foreach (var (cbs, ubs) in pairs)
            {
                if (cbs == ubs) Array.Copy(orig, p, img, outPos, (int)cbs);
                else Array.Copy(Lzo1x.Decompress(Slice(orig, p, (int)cbs), (int)ubs), 0, img, outPos, (int)ubs);
                p += (int)cbs;
                outPos += (int)ubs;
            }
        }
        return img;
    }

    static byte[] Slice(byte[] src, int off, int len)
    { var b = new byte[len]; Array.Copy(src, off, b, 0, len); return b; }
}
