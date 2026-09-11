using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace DYS.Molargo
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            HideStatusBar();
        }

        /// <summary>
        /// Re-hides the bar whenever the activity comes back to the front.
        /// </summary>
        /// <remarks>
        /// Hiding it once is not enough. Android restores the system bars when the activity
        /// loses and regains focus — after a task switch, a permission dialog, or the
        /// on-screen keyboard closing — and a bar that reappears halfway through a
        /// consultation is worse than one that was never hidden.
        /// </remarks>
        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);

            if (hasFocus) HideStatusBar();
        }

        /// <summary>
        /// Hides the status bar, leaving a swipe to bring it back temporarily.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Through <c>WindowInsetsControllerCompat</c> rather than the theme's
        /// <c>android:windowFullscreen</c>. That flag is deprecated and Android 15 onwards
        /// — which this tablet runs — ignores it outright, so the declarative version would
        /// look correct in the styles file and do nothing on the device it was written for.
        /// </para>
        /// <para>
        /// The navigation bar is deliberately left alone. Hiding it too takes away the back
        /// gesture's affordance, and this app has screens a user has to be able to leave.
        /// </para>
        /// </remarks>
        private void HideStatusBar()
        {
            if (Window is not { DecorView: { } decor } window) return;

            // Null before the window has a decor view, which is why this is not called
            // from the constructor. Skipped rather than asserted: a status bar that stays
            // visible is a blemish, and crashing the app over one is not a trade.
            if (WindowCompat.GetInsetsController(window, decor) is not { } insets) return;

            // Swiping from the top shows the bar briefly and then hides it again, so the
            // clock and the battery are still reachable without leaving the app.
            insets.SystemBarsBehavior =
                WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;

            insets.Hide(WindowInsetsCompat.Type.StatusBars());
        }
    }
}
