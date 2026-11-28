namespace MiningGame.Core;

// 确定性随机：只依赖传入的 seed 与坐标/序号，同输入必得同输出（不碰系统时钟、不碰全局态）。
internal static class Rng
{
    public static uint Mix(int seed, int a, int b)
    {
        unchecked
        {
            uint h = (uint)seed * 2166136261u;
            h = (h ^ (uint)a) * 16777619u;
            h = (h ^ (uint)b) * 16777619u;
            h ^= h >> 15;
            return h == 0 ? 0x9E3779B9u : h;
        }
    }

    public static int Next(ref uint state)
    {
        unchecked
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            if (state == 0) state = 0x9E3779B9u;
            return (int)(state & 0x7FFFFFFF);
        }
    }
}
