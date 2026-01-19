using UnityEngine;

// XUI controllers must be in the global namespace for 7DTD to find them
/// <summary>
/// Controller for the pause menu button that opens NPCLLMChat configuration.
/// </summary>
public class XUiC_NPCLLMChatInGameMenu : XUiController
    {
        private XUiC_SimpleButton btnConfig;

        public override void Init()
        {
            base.Init();

            // Get the config button
            btnConfig = GetChildById("btnNPCLLMChatConfig") as XUiC_SimpleButton;
            if (btnConfig != null)
            {
                btnConfig.OnPressed += BtnConfig_OnPressed;
            }
        }

        /// <summary>
        /// Opens the NPCLLMChat configuration window
        /// </summary>
        private void BtnConfig_OnPressed(XUiController _sender, int _mouseButton)
        {
            xui.playerUI.windowManager.Open("NPCLLMChatConfigGroup", true, false, true);
        }

        public override void OnOpen()
        {
            base.OnOpen();

            // Only show if player is in game (not in main menu)
            var world = GameManager.Instance?.World;
            if (world != null && world.GetPrimaryPlayer() != null)
            {
                ViewComponent.IsVisible = true;
            }
            else
            {
                ViewComponent.IsVisible = false;
            }
        }
    }
