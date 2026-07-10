namespace VrlfMods.Patches;

public static class Lzo1x
{
    public static byte[] Decompress(byte[] src, int expectedLen)
    {
        var outp = new byte[expectedLen];
        int op = 0, ip = 0;

        void Match(int dist, int cnt)
        {
            int pos = op - dist;
            if (pos < 0) throw new InvalidDataException("bad LZO match distance");
            for (int i = 0; i < cnt; i++) outp[op++] = outp[pos++];
        }
        void Lits(int n) { Array.Copy(src, ip, outp, op, n); op += n; ip += n; }

        int state = 0;
        int t = src[ip];
        if (t > 17)
        {
            ip += 1; t -= 17; Lits(t);
            state = t >= 4 ? 4 : t;
            t = src[ip]; ip += 1;
        }
        else { t = src[ip]; ip += 1; }

        while (true)
        {
            if (t < 16)
            {
                if (state == 0)
                {
                    if (t == 0)
                    {
                        while (src[ip] == 0) { t += 255; ip += 1; }
                        t += 15 + src[ip]; ip += 1;
                    }
                    t += 3; Lits(t);
                    state = 4;
                    t = src[ip]; ip += 1;
                    continue;
                }
                else if (state == 4)
                {
                    int dist = 1 + 0x800 + (t >> 2) + (src[ip] << 2); ip += 1;
                    Match(dist, 3); state = t & 3;
                }
                else
                {
                    int dist = 1 + (t >> 2) + (src[ip] << 2); ip += 1;
                    Match(dist, 2); state = t & 3;
                }
            }
            else if (t >= 64)
            {
                int dist = 1 + ((t >> 2) & 7) + (src[ip] << 3); ip += 1;
                Match(dist, (t >> 5) + 1); state = t & 3;
            }
            else if (t >= 32)
            {
                int cnt = t & 31;
                if (cnt == 0) { while (src[ip] == 0) { cnt += 255; ip += 1; } cnt += 31 + src[ip]; ip += 1; }
                int ds = src[ip] | (src[ip + 1] << 8); ip += 2;
                Match(1 + (ds >> 2), cnt + 2); state = ds & 3;
            }
            else
            {
                int bas = (t & 8) << 11;
                int cnt = t & 7;
                if (cnt == 0) { while (src[ip] == 0) { cnt += 255; ip += 1; } cnt += 7 + src[ip]; ip += 1; }
                int ds = src[ip] | (src[ip + 1] << 8); ip += 2;
                int dist = bas + (ds >> 2);
                if (dist == 0) break;                 // EOF
                Match(dist + 0x4000, cnt + 2); state = ds & 3;
            }
            if (state != 0) Lits(state);
            t = src[ip]; ip += 1;
        }

        if (op != expectedLen)
            throw new InvalidDataException($"LZO block decompressed to {op}, expected {expectedLen}");
        return outp;
    }
}
