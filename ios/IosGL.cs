namespace Dungeon
{
    /// <summary>OpenGL ES specifics for the iOS host.</summary>
    public static class IosGL
    {
        /// <summary>Replaces the desktop "#version 330 core" line in every shader.</summary>
        public const string GlslHeader = "#version 300 es\nprecision highp float;\nprecision highp int;";
    }
}
