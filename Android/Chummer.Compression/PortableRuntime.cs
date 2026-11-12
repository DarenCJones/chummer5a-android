// SPDX-License-Identifier: GPL-3.0-or-later
// Narrow adapters for the upstream codec's desktop threading helpers.
namespace Chummer;
internal static class Utils
{
    public static void SafelyRunSynchronously(Func<Task> work, CancellationToken token = default)
    { token.ThrowIfCancellationRequested(); work().GetAwaiter().GetResult(); }
    public static void RunWithoutThreadLock(Action first, Action second, Action third, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Task.WhenAll(Task.Run(first, token), Task.Run(second, token), Task.Run(third, token)).GetAwaiter().GetResult();
    }
}
internal static class TaskExtensions
{
    public static Task RunWithoutEC(Action<CancellationToken> action, CancellationToken token) => Task.Run(() => action(token), token);
    public static Task RunWithoutEC(Action action, CancellationToken token) => Task.Run(action, token);
}
internal static class IntegerExtensions
{
    public static int DivAwayFromZero(int value, int divisor) => value / divisor + (value % divisor == 0 ? 0 : Math.Sign(value) * Math.Sign(divisor));
}
internal static class MathExtensions
{
    public static int DivRem(this int value, int divisor, out int remainder) => Math.DivRem(value, divisor, out remainder);
}
