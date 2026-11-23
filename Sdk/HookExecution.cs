namespace Gkms.Sdk;

/// <summary>Marks side effects in a replacement prefix before invoking game callbacks.</summary>
public static class HookExecution
{
    [ThreadStatic] private static Scope? current;
    public static Scope Begin() => new();
    public static void MarkReplacement() { if (current != null) current.ReplacementStarted = true; }
    public sealed class Scope : IDisposable
    {
        private readonly Scope? previous;
        public bool ReplacementStarted { get; internal set; }
        internal Scope() { previous = current; current = this; }
        public void Dispose() => current = previous;
    }
}
