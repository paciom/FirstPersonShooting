using System;

/// <summary>
/// True when this play session was launched for an offline render
/// (-storyEpisode or -dogfightWar): no human is at the keyboard, so anything
/// keyed to one — the Escape hatch, the debug console and its FPS readout,
/// the broadcast camera hotkeys — must ignore whatever stray input reaches
/// the window, or a wandering keystroke ends up on the tape (a stray ESC
/// once cut a 4v4 render back to the main menu two minutes in).
/// </summary>
public static class UnattendedRender
{
    public static readonly bool Active =
        Array.IndexOf(Environment.GetCommandLineArgs(), DogfightWarBootstrap.Arg) >= 0
        || Array.IndexOf(Environment.GetCommandLineArgs(), StoryBootstrap.Arg) >= 0;
}
