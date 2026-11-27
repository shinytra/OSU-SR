// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.OsuSR;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Select;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Navigation
{
    public partial class TestSceneButtonSystemNavigation : OsuGameTestScene
    {
        private ButtonSystem buttons => ((MainMenu)Game.ScreenStack.CurrentScreen).ChildrenOfType<ButtonSystem>().Single();

        // OSU SR: these tests cover the minimal menu, so make sure it is enabled even if another test scene left it off.
        public override void SetUpSteps()
        {
            AddStep("enable minimal mode", () => OsuSRFeatures.MinimalMenu = true);
            base.SetUpSteps();
        }

        [Test]
        public void TestGlobalActionHasPriority()
        {
            AddAssert("state is initial", () => buttons.State == ButtonSystemState.Initial);

            // triggering the cookie in the initial state with any key should only happen if no other action is bound to that key.
            // here, F10 is bound to GlobalAction.ToggleGameplayMouseButtons.
            AddStep("press F10", () => InputManager.Key(Key.F10));
            AddAssert("state is initial", () => buttons.State == ButtonSystemState.Initial);

            AddStep("press P", () => InputManager.Key(Key.P));
            AddAssert("state is top level", () => buttons.State == ButtonSystemState.TopLevel);
        }

        [Test]
        public void TestFastShortcutKeys()
        {
            AddAssert("state is initial", () => buttons.State == ButtonSystemState.Initial);

            AddStep("press P three times", () =>
            {
                InputManager.Key(Key.P);
                InputManager.Key(Key.P);
                InputManager.Key(Key.P);
            });

            AddAssert("entered song select", () => Game.ScreenStack.CurrentScreen is SoloSongSelect);
        }

        [Test]
        public void TestShortcutKeys()
        {
            AddAssert("state is initial", () => buttons.State == ButtonSystemState.Initial);

            AddStep("press P", () => InputManager.Key(Key.P));
            AddAssert("state is top level", () => buttons.State == ButtonSystemState.TopLevel);

            // OSU SR: with the minimal menu, "Play" goes straight to song select (no play submenu).
            AddStep("press P", () => InputManager.Key(Key.P));
            AddAssert("entered song select", () => Game.ScreenStack.CurrentScreen is SoloSongSelect);
        }
    }
}
