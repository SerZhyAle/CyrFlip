namespace CyrFlip
{
    internal static class FirstRun
    {
        // The load normalizes existing/legacy profiles to shown=true. An explicit zero is
        // the owner's replay override (S0055 section 2), even when the key already exists.
        public static bool ShouldShow(bool isFirstRun, bool welcomeShown) => !welcomeShown;
    }
}
