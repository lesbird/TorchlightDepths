using Foundation;
using UIKit;

namespace Dungeon
{
    public static class IosMain
    {
        static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
    }

    [Register("AppDelegate")]
    public sealed class AppDelegate : UIApplicationDelegate
    {
        public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions) => true;

        public override UISceneConfiguration GetConfiguration(UIApplication application,
            UISceneSession connectingSceneSession, UISceneConnectionOptions options)
            => new UISceneConfiguration("Default Configuration", connectingSceneSession.Role);
    }

    /// <summary>Owns the window and forwards foreground/background transitions to the game.</summary>
    [Register("SceneDelegate")]
    public sealed class SceneDelegate : UIResponder, IUIWindowSceneDelegate
    {
        [Export("window")]
        public UIWindow Window { get; set; }

        GameViewController _vc;

        [Export("scene:willConnectToSession:options:")]
        public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
        {
            if (scene is not UIWindowScene ws) return;
            _vc = new GameViewController();
            Window = new UIWindow(ws) { RootViewController = _vc };
            Window.MakeKeyAndVisible();
        }

        [Export("sceneDidBecomeActive:")]
        public void DidBecomeActive(UIScene scene) => _vc?.Resume();

        [Export("sceneWillResignActive:")]
        public void WillResignActive(UIScene scene) => _vc?.Suspend();
    }
}
