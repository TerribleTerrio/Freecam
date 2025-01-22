using BepInEx;
using GameNetcodeStuff;
using HarmonyLib;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace LCCinematicFreecam
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "Terrio.ExtendedFreecam";
        public const string PLUGIN_NAME = "ExtendedFreecam";
        public const string PLUGIN_VERSION = "1.0.0";

        private void Awake()
        {
            LCCinematicFreecam.Logger.SetSource(Logger);

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());

            Logger.LogInfo($"{PLUGIN_NAME} is loaded!");
        }
    }

    [HarmonyPatch]
    internal static class CameraPatches
    {
        private static GameObject helmetObject;

        private static SkinnedMeshRenderer playerRenderer;

        private static Transform playerAudioListener;

        private static Transform playerCamera;

        private static Camera extCamera;

        private static Transform extCameraTurnCompass;

        private static StartOfRound startOfRound;

        private static PlayerControllerB localPlayer;

        private static Coroutine lookCoroutine;

        private static Coroutine moveCoroutine;

        private static Coroutine hideScanNodesCoroutine;

        private static Coroutine parentHeldItemCoroutine;

        private static float maxZoomInManual = 5f;

        private static float maxZoomInAuto = 35f;

        private static float maxZoomOutManual = 140f;

        private static float maxZoomOutAuto = 65f;

        private static float lerpTargetZoom;

        private static float cameraUp;

        private static float positionLerpMult = 3f;

        private static float rotationLerpMult = 3f;

        private static bool hudToggled;

        private static bool? zoomInFlag = null;

        private enum State
        {
            Off,

            ControlCamera,

            ControlPlayer,

            FollowPlayer
        };

        private static State currentState = State.Off;

        private static State prevState = State.Off;

        private static InputAction SlowMovement = new InputAction(nameof(SlowMovement), binding: "<Keyboard>/leftAlt");

        private static InputAction ToggleCamera = new InputAction(nameof(ToggleCamera), binding: "<Keyboard>/z");

        private static InputAction ToggleHud = new InputAction(nameof(ToggleHud), binding: "<Keyboard>/x");

        private static InputAction ToggleControl = new InputAction(nameof(ToggleControl), binding: "<Keyboard>/c");

        private static InputAction FollowPlayer = new InputAction(nameof(FollowPlayer), binding: "<Keyboard>/v");

        private static InputAction BringToPlayer = new InputAction(nameof(BringToPlayer), binding: "<Keyboard>/b");

        [HarmonyPatch(typeof(StartOfRound), "Start")]
        [HarmonyPostfix]
        private static void OnGameEntered(StartOfRound __instance)
        {
            startOfRound = __instance;
            startOfRound.StartCoroutine(SetupFreecam());
        }

        [HarmonyPatch(typeof(StartOfRound), "OnDestroy")]
        [HarmonyPostfix]
        private static void OnGameLeft()
        {
            startOfRound = null;
            Object.Destroy(extCamera.gameObject);
            RemoveInputs();
        }

        [HarmonyPatch(typeof(PlayerControllerB), "KillPlayer")]
        [HarmonyPostfix]
        private static void OnPlayerDeath(PlayerControllerB __instance)
        {
            if (!__instance.IsOwner)
                return;

            if (__instance.isFreeCamera)
                ChangeCameraState(State.Off);
        }

        private static IEnumerator SetupFreecam()
        {
            Logger.LogInfo("Setting up free camera!");
            yield return new WaitUntil(() => StartOfRound.Instance.localPlayerController != null);
            
            localPlayer = StartOfRound.Instance.localPlayerController;

            helmetObject = GameObject.Find("Systems").transform.Find("Rendering/PlayerHUDHelmetModel").gameObject;
            playerRenderer = localPlayer.transform.Find("ScavengerModel/LOD1").GetComponent<SkinnedMeshRenderer>();

            playerAudioListener = localPlayer.transform.Find("ScavengerModel/metarig/CameraContainer/MainCamera/PlayerAudioListener");
            playerCamera = localPlayer.transform.Find("ScavengerModel/metarig/CameraContainer/MainCamera");

            extCamera = Object.Instantiate(StartOfRound.Instance.freeCinematicCamera.gameObject, StartOfRound.Instance.freeCinematicCamera.transform.parent).GetComponent<Camera>();
            extCameraTurnCompass = Object.Instantiate(StartOfRound.Instance.freeCinematicCameraTurnCompass, StartOfRound.Instance.freeCinematicCameraTurnCompass.parent).transform;

            AddInputs();
        }

        private static void AddInputs()
        {
            ToggleCamera.performed += SetExtCamera_performed;
            ToggleControl.performed += SetPlayerControl_performed;
            ToggleHud.performed += SetHud_performed;
            FollowPlayer.performed += SetFollowPlayer_performed;
            BringToPlayer.performed += BringToPlayer_performed;
            SlowMovement.Enable();
            ToggleCamera.Enable();
            ToggleControl.Enable();
            ToggleHud.Enable();
            FollowPlayer.Enable();
            BringToPlayer.Enable();
        }

        private static void RemoveInputs()
        {
            ToggleCamera.performed -= SetExtCamera_performed;
            ToggleControl.performed -= SetPlayerControl_performed;
            ToggleHud.performed -= SetHud_performed;
            BringToPlayer.performed -= BringToPlayer_performed;
            FollowPlayer.performed -= SetFollowPlayer_performed;
            SlowMovement.Disable();
            ToggleCamera.Disable();
            ToggleControl.Disable();
            ToggleHud.Enable();
            FollowPlayer.Disable();
            BringToPlayer.Disable();
        }

        private static void SetExtCamera_performed(InputAction.CallbackContext obj)
        {
            if (localPlayer.isTypingChat || localPlayer.quickMenuManager.isMenuOpen)
            {
                return;
            }
            Logger.Log("Keybind pressed: Toggle camera!");
            if (currentState != State.Off)
            {
                ChangeCameraState(State.Off);
            }
            else
            {
                ChangeCameraState(prevState);
            }
        }

        private static void SetPlayerControl_performed(InputAction.CallbackContext obj)
        {
            if (localPlayer.isTypingChat || localPlayer.quickMenuManager.isMenuOpen || currentState == State.Off)
            {
                return;
            }
            Logger.Log("Keybind pressed: Toggle control!");
            if (currentState == State.ControlCamera)
            {
                ChangeCameraState(State.ControlPlayer);
            }
            else
            {
                ChangeCameraState(State.ControlCamera);
            }
        }

        private static void SetHud_performed(InputAction.CallbackContext obj)
        {
            if (localPlayer.isTypingChat || localPlayer.quickMenuManager.isMenuOpen || currentState == State.Off)
            {
                return;
            }
            Logger.Log("Keybind pressed: Toggle hud!");
            hudToggled = !hudToggled;
            HUDManager.Instance.HideHUD(hudToggled);
            localPlayer.cursorIcon.gameObject.SetActive(!hudToggled);
            localPlayer.cursorTip.gameObject.SetActive(!hudToggled);
            HUDManager.Instance.holdInteractionCanvasGroup.gameObject.SetActive(!hudToggled);
            if (hudToggled)
            {
                if (hideScanNodesCoroutine != null)
                {
                    startOfRound.StopCoroutine(hideScanNodesCoroutine);
                }
                hideScanNodesCoroutine = startOfRound.StartCoroutine(HideScanNodes());
            }
            else
            {
                if (hideScanNodesCoroutine != null)
                {
                    startOfRound.StopCoroutine(hideScanNodesCoroutine);
                }

            }
        }

        private static void SetFollowPlayer_performed(InputAction.CallbackContext obj)
        {
            if (localPlayer.isTypingChat || localPlayer.quickMenuManager.isMenuOpen || currentState == State.Off)
            {
                return;
            }
            Logger.Log("Keybind pressed: Follow player!");
            if (currentState == State.FollowPlayer)
            {
                ChangeCameraState(State.ControlCamera);
            }
            else
            {
                ChangeCameraState(State.FollowPlayer);
            }
        }

        private static void BringToPlayer_performed(InputAction.CallbackContext obj)
        {
            if (localPlayer.isTypingChat || localPlayer.quickMenuManager.isMenuOpen || currentState == State.Off)
            {
                return;
            }
            Logger.Log("Keybind pressed: Bring camera to player!");
            extCameraTurnCompass.transform.position = playerCamera.transform.position;
            extCameraTurnCompass.transform.rotation = playerCamera.transform.rotation;
        }

        private static void ChangeCameraState(State stateToChangeTo)
        {
            zoomInFlag = null;
            switch ((int)currentState)
            {
                case 0:
                    EnableFreecam();
                    stateToChangeTo = (prevState == State.Off) ? State.ControlCamera : stateToChangeTo;
                    break;

                case 3:
                    positionLerpMult = 3f;
                    rotationLerpMult = 3f;
                    extCameraTurnCompass.parent = StartOfRound.Instance.freeCinematicCameraTurnCompass.parent;
                    prevState = currentState;
                    break;

                default:
                    prevState = currentState;
                    break;
            }
            switch ((int)stateToChangeTo)
            {
                case 0:
                    currentState = State.Off;
                    DisableFreecam();
                    break;

                case 1:
                    currentState = State.ControlCamera;
                    localPlayer.isFreeCamera = true;
                    break;

                case 2:
                    currentState = State.ControlPlayer;
                    localPlayer.isFreeCamera = false;
                    break;

                case 3:
                    currentState = State.FollowPlayer;
                    localPlayer.isFreeCamera = false;
                    positionLerpMult = 5f;
                    rotationLerpMult = 6f;
                    extCameraTurnCompass.parent = playerCamera;
                    zoomInFlag = false;
                    break;
            }
        }

        private static void EnableFreecam()
        {
            Logger.Log("Enabling freecam!");
            localPlayer.isFreeCamera = true;
            StartOfRound.Instance.SwitchCamera(extCamera);
            extCamera.cullingMask = 557520895;
            helmetObject.SetActive(false);
            localPlayer.thisPlayerModelArms.enabled = false;
            if (localPlayer.currentlyHeldObjectServer != null)
            {
                localPlayer.currentlyHeldObjectServer.parentObject = localPlayer.serverItemHolder;
            }
            playerRenderer.shadowCastingMode = ShadowCastingMode.On;
            playerAudioListener.SetParent(extCamera.transform, false);
            HUDManager.Instance.HideHUD(true);
            hudToggled = true;
            localPlayer.cursorIcon.gameObject.SetActive(false);
            localPlayer.cursorTip.gameObject.SetActive(false);
            HUDManager.Instance.holdInteractionCanvasGroup.gameObject.SetActive(false);
            lerpTargetZoom = extCamera.fieldOfView;
            if (lookCoroutine != null)
            {
                startOfRound.StopCoroutine(lookCoroutine);
            }
            if (moveCoroutine != null)
            {
                startOfRound.StopCoroutine(moveCoroutine);
            }
            if (hideScanNodesCoroutine != null)
            {
                startOfRound.StopCoroutine(hideScanNodesCoroutine);
            }
            if (parentHeldItemCoroutine != null)
            {
                startOfRound.StopCoroutine(parentHeldItemCoroutine);
            }
            lookCoroutine = startOfRound.StartCoroutine(LookInput());
            moveCoroutine = startOfRound.StartCoroutine(MoveInput());
            hideScanNodesCoroutine = startOfRound.StartCoroutine(HideScanNodes());
            parentHeldItemCoroutine = startOfRound.StartCoroutine(ParentHeldItem());
        }

        private static void DisableFreecam()
        {
            Logger.Log($"Disabling freecam, (Dead: {localPlayer.isPlayerDead})!");
            localPlayer.isFreeCamera = false;
            extCamera.enabled = false;
            StartOfRound.Instance.SwitchCamera(localPlayer.isPlayerDead ? StartOfRound.Instance.spectateCamera : localPlayer.gameplayCamera);
            helmetObject.SetActive(true);
            localPlayer.thisPlayerModelArms.enabled = true;
            if (localPlayer.currentlyHeldObjectServer != null)
            {
                localPlayer.currentlyHeldObjectServer.parentObject = localPlayer.localItemHolder;
            }
            playerRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            playerAudioListener.SetParent(playerCamera, false);
            HUDManager.Instance.HideHUD(localPlayer.isPlayerDead);
            hudToggled = false;
            localPlayer.cursorIcon.gameObject.SetActive(true);
            localPlayer.cursorTip.gameObject.SetActive(true);
            HUDManager.Instance.holdInteractionCanvasGroup.gameObject.SetActive(true);
            if (lookCoroutine != null)
            {
                startOfRound.StopCoroutine(lookCoroutine);
            }
            if (moveCoroutine != null)
            {
                startOfRound.StopCoroutine(moveCoroutine);
            }
            if (hideScanNodesCoroutine != null)
            {
                startOfRound.StopCoroutine(hideScanNodesCoroutine);
            }
            if (parentHeldItemCoroutine != null)
            {
                startOfRound.StopCoroutine(parentHeldItemCoroutine);
            }
        }

        private static IEnumerator LookInput()
        {
            while (true)
            {
                yield return null;
                Vector2 vector = localPlayer.playerActions.Movement.Look.ReadValue<Vector2>() * 0.008f * IngamePlayerSettings.Instance.settings.lookSensitivity;
                if (currentState != State.ControlCamera || localPlayer.quickMenuManager.isMenuOpen || localPlayer.inSpecialMenu || StartOfRound.Instance.newGameIsLoading || localPlayer.disableLookInput)
                {
                    vector = Vector2.zero;
                }
                if (IngamePlayerSettings.Instance.settings.invertYAxis)
                {
                    vector.y *= -1f;
                }
				extCameraTurnCompass.Rotate(new Vector3(0f, vector.x, 0f));
				cameraUp -= vector.y;
				cameraUp = Mathf.Clamp(cameraUp, -80f, 80f);
				extCameraTurnCompass.transform.localEulerAngles = new Vector3(cameraUp, extCameraTurnCompass.transform.localEulerAngles.y, 0f);
                float QEinput = IngamePlayerSettings.Instance.playerInput.actions.FindAction("QEItemInteract").ReadValue<float>();
                bool manuallyZoomingIn = QEinput > 0.01f;
                if (zoomInFlag == null || currentState != State.FollowPlayer)
                {
                    bool manuallyZoomingOut = QEinput < -0.01f;
                    float manualTargetZoom = manuallyZoomingIn ? maxZoomInManual : manuallyZoomingOut ? maxZoomOutManual : lerpTargetZoom;
                    lerpTargetZoom = Mathf.Lerp(lerpTargetZoom, manualTargetZoom, Time.deltaTime * (manuallyZoomingOut ? 0.35f : 0.9f));
                }
                else
                {
                    zoomInFlag = manuallyZoomingIn && localPlayer.hoveringOverTrigger != null;
                    float autoTargetZoom = (bool)zoomInFlag ? maxZoomInAuto : maxZoomOutAuto;
                    lerpTargetZoom = Mathf.Lerp(lerpTargetZoom, autoTargetZoom, Time.deltaTime * ((bool)zoomInFlag ? 1.1f : 0.75f));
                }
                extCamera.fieldOfView = Mathf.Lerp(extCamera.fieldOfView, lerpTargetZoom, 3.5f * Time.deltaTime);
            }
        }

        private static IEnumerator MoveInput()
        {
            while (true)
            {
                yield return null;
				Vector2 moveInputVector = IngamePlayerSettings.Instance.playerInput.actions.FindAction("Move").ReadValue<Vector2>();
				if (currentState != State.ControlCamera || localPlayer.quickMenuManager.isMenuOpen || localPlayer.isTypingChat)
				{
					moveInputVector = Vector2.zero;
				}
				float sprintValue = IngamePlayerSettings.Instance.playerInput.actions.FindAction("Sprint").ReadValue<float>();
                float currentHorizontalSpeed = (sprintValue > 0.5f) ? 10f : SlowMovement.IsPressed() ? 1.5f : 3f;
                float currentVerticalSpeed = (sprintValue > 0.5f) ? 8f : SlowMovement.IsPressed() ? 0.8f : 3f;
                Vector3 vector = (extCameraTurnCompass.transform.right * moveInputVector.x + extCameraTurnCompass.transform.forward * moveInputVector.y) * currentHorizontalSpeed;
                extCameraTurnCompass.transform.position += vector * Time.deltaTime;
                if (currentState == State.ControlCamera && IngamePlayerSettings.Instance.playerInput.actions.FindAction("Jump").IsPressed())
                {
                    extCameraTurnCompass.position += Vector3.up * (Time.deltaTime * currentVerticalSpeed);
                }
                if (currentState == State.ControlCamera && IngamePlayerSettings.Instance.playerInput.actions.FindAction("Crouch").IsPressed())
                {
                    extCameraTurnCompass.position += Vector3.down * (Time.deltaTime * currentVerticalSpeed);
                }
                extCamera.transform.position = Vector3.Lerp(extCamera.transform.position, extCameraTurnCompass.transform.position, positionLerpMult * Time.deltaTime);
                extCamera.transform.rotation = Quaternion.Slerp(extCamera.transform.rotation, extCameraTurnCompass.rotation, rotationLerpMult * Time.deltaTime);
            }
        }

        private static IEnumerator HideScanNodes()
        {
            while (true)
            {
                yield return null;
                for (int i = 0; i < HUDManager.Instance.scanElements.Length; i++)
                {
                    HUDManager.Instance.scanElements[i].gameObject.SetActive(value: false);
                }
            }
        }

        private static IEnumerator ParentHeldItem()
        {
            while (true)
            {
                yield return null;
                if (localPlayer.currentlyHeldObjectServer != null && localPlayer.currentlyHeldObjectServer.parentObject != localPlayer.serverItemHolder)
                {
                    localPlayer.currentlyHeldObjectServer.parentObject = localPlayer.serverItemHolder;
                }
            }
        }
    }
}
