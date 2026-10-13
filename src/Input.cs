namespace Dungeon
{
    /// <summary>
    /// Platform-neutral keys the game reacts to. Each host maps its own key events
    /// onto these, so the core never depends on a windowing library.
    /// </summary>
    public enum Key
    {
        Unknown,
        Up, Down, Left, Right,
        W, A, S, D, Q, E, F, C, M,
        Space, Enter, Escape, Backspace, Tab,
    }
}
