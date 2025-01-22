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
    internal static class MainPatches
    {
        private static GameObject helmetObject;

        private static SkinnedMeshRenderer playerRenderer;

        private static Transform playerAudioListener;

        private static Transform playerCamera;

        private static Camera extCamera;

        private static Transform extCameraTransform;

        private static Transform extCameraTurnCompass;

        private static StartOfRound startOfRound;

        private static PlayerControllerB localPlayer;

        private static Coroutine lookCoroutine;

        private static Coroutine moveCoroutine;

        private static float maxZoomIn = 5f;

        private static float maxZoomOut = 140f;

        private static float targetZoom;

        private static float cameraUp;

        private static InputAction AltKey = new InputAction("AltKey", binding: "<Keyboard>/leftAlt");

        [HarmonyPatch(typeof(StartOfRound), "Start")]
        [HarmonyPostfix]
        private static void OnGameEntered(StartOfRound __instance)
        {
            startOfRound = __instance;
            __instance.StartCoroutine(SetupFreecam());
        }

        private static IEnumerator SetupFreecam()
        {
            Logger.LogInfo("Setting up free camera");
            yield return new WaitUntil(() => StartOfRound.Instance.localPlayerController != null);
            
            localPlayer = StartOfRound.Instance.localPlayerController;

            helmetObject = GameObject.Find("Systems").transform.Find("Rendering/PlayerHUDHelmetModel").gameObject;
            playerRenderer = localPlayer.transform.Find("ScavengerModel/LOD1").GetComponent<SkinnedMeshRenderer>();

            playerAudioListener = localPlayer.transform.Find("ScavengerModel/metarig/CameraContainer/MainCamera/PlayerAudioListener");
            playerCamera = localPlayer.transform.Find("ScavengerModel/metarig/CameraContainer/MainCamera");

            extCamera = Object.Instantiate(StartOfRound.Instance.freeCinematicCamera.gameObject, StartOfRound.Instance.freeCinematicCamera.transform.parent).GetComponent<Camera>();
            extCameraTransform = extCamera.transform;
            extCameraTurnCompass = Object.Instantiate(StartOfRound.Instance.freeCinematicCameraTurnCompass, StartOfRound.Instance.freeCinematicCameraTurnCompass.parent).transform;

            AddInputs();
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
                DisableFreecam();
        }

        private static void AddInputs()
        {
            IngamePlayerSettings.Instance.playerInput.actions.FindAction("SetFreeCamera").performed += SetFreeCamera_performed;
            AltKey.Enable();
        }

        private static void RemoveInputs()
        {
            IngamePlayerSettings.Instance.playerInput.actions.FindAction("SetFreeCamera").performed -= SetFreeCamera_performed;
            AltKey.Disable();
        }

        private static void SetFreeCamera_performed(InputAction.CallbackContext obj)
        {
            Logger.LogDebug("Freecamera keybind pressed");

            if (localPlayer.isFreeCamera)
            {
                DisableFreecam();
            }
            else if (!localPlayer.isPlayerDead)
            {
                EnableFreecam();
            }
        }

        private static void EnableFreecam()
        {
            Logger.Log("Enabling freecam");

            localPlayer.isFreeCamera = true;
            StartOfRound.Instance.SwitchCamera(extCamera);
            extCamera.cullingMask = 557520895;
            helmetObject.SetActive(false);
            localPlayer.thisPlayerModelArms.enabled = false;
            if (localPlayer.currentlyHeldObject != null)
            {
                localPlayer.currentlyHeldObject.parentObject = localPlayer.serverItemHolder;
            }
            playerRenderer.shadowCastingMode = ShadowCastingMode.On;
            playerAudioListener.SetParent(extCameraTransform, false);
            HUDManager.Instance.HideHUD(true);

            targetZoom = extCamera.fieldOfView;
            if (lookCoroutine != null)
            {
                startOfRound.StopCoroutine(lookCoroutine);
            }
            if (moveCoroutine != null)
            {
                startOfRound.StopCoroutine(moveCoroutine);
            }
            lookCoroutine = startOfRound.StartCoroutine(LookInput());
            moveCoroutine = startOfRound.StartCoroutine(MoveInput());
        }

        private static void DisableFreecam()
        {
            Logger.Log($"Disabling freecam (Dead: {localPlayer.isPlayerDead})");

            localPlayer.isFreeCamera = false;
            extCamera.enabled = false;
            StartOfRound.Instance.SwitchCamera(localPlayer.isPlayerDead ? StartOfRound.Instance.spectateCamera : localPlayer.gameplayCamera);
            helmetObject.SetActive(true);
            localPlayer.thisPlayerModelArms.enabled = true;
            if (localPlayer.currentlyHeldObject != null)
            {
                localPlayer.currentlyHeldObject.parentObject = localPlayer.localItemHolder;
            }
            playerRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            playerAudioListener.SetParent(playerCamera, false);
            HUDManager.Instance.HideHUD(localPlayer.isPlayerDead);

            if (lookCoroutine != null)
            {
                startOfRound.StopCoroutine(lookCoroutine);
            }
            if (moveCoroutine != null)
            {
                startOfRound.StopCoroutine(moveCoroutine);
            }
        }

        private static IEnumerator LookInput()
        {
            while (true)
            {
                TOP:
                yield return null;
                if (localPlayer.quickMenuManager.isMenuOpen || localPlayer.inSpecialMenu || StartOfRound.Instance.newGameIsLoading || localPlayer.disableLookInput)
                {
                    goto TOP;
                }
                Vector2 vector = localPlayer.playerActions.Movement.Look.ReadValue<Vector2>() * 0.008f * IngamePlayerSettings.Instance.settings.lookSensitivity;
                if (IngamePlayerSettings.Instance.settings.invertYAxis)
                {
                    vector.y *= -1f;
                }
				extCameraTurnCompass.Rotate(new Vector3(0f, vector.x, 0f));
				cameraUp -= vector.y;
				cameraUp = Mathf.Clamp(cameraUp, -80f, 80f);
				extCameraTurnCompass.transform.localEulerAngles = new Vector3(cameraUp, extCameraTurnCompass.transform.localEulerAngles.y, 0f);
                if (IngamePlayerSettings.Instance.playerInput.actions.FindAction("QEItemInteract").ReadValue<float>() < -0.01f)
                {
                    targetZoom = Mathf.Lerp(targetZoom, maxZoomOut, Time.deltaTime * 0.4f);
                }
                else if (IngamePlayerSettings.Instance.playerInput.actions.FindAction("QEItemInteract").ReadValue<float>() > 0.01f)
                {
                    targetZoom = Mathf.Lerp(targetZoom, maxZoomIn, Time.deltaTime);
                }
                extCamera.fieldOfView = Mathf.Lerp(extCamera.fieldOfView, targetZoom, 3.5f * Time.deltaTime);
            }
        }

        private static IEnumerator MoveInput()
        {
            while (true)
            {
                yield return null;
				Vector2 moveInputVector = IngamePlayerSettings.Instance.playerInput.actions.FindAction("Move").ReadValue<Vector2>();
				if (localPlayer.quickMenuManager.isMenuOpen || localPlayer.isTypingChat || localPlayer.disableMoveInput || (localPlayer.inSpecialInteractAnimation && !localPlayer.isClimbingLadder && !localPlayer.inShockingMinigame))
				{
					moveInputVector = Vector2.zero;
				}
				float sprintValue = IngamePlayerSettings.Instance.playerInput.actions.FindAction("Sprint").ReadValue<float>();
                float currentHorizontalSpeed = (sprintValue > 0.5f) ? 10f : AltKey.IsPressed() ? 1.5f : 3f;
                float currentVerticalSpeed = (sprintValue > 0.5f) ? 8f : AltKey.IsPressed() ? 0.8f : 3f;
                Vector3 vector = (extCameraTurnCompass.transform.right * moveInputVector.x + extCameraTurnCompass.transform.forward * moveInputVector.y) * currentHorizontalSpeed;
                extCameraTurnCompass.transform.position += vector * Time.deltaTime;
                if (IngamePlayerSettings.Instance.playerInput.actions.FindAction("Jump").IsPressed())
                {
                    extCameraTurnCompass.position += Vector3.up * (Time.deltaTime * currentVerticalSpeed);
                }
                if (IngamePlayerSettings.Instance.playerInput.actions.FindAction("Crouch").IsPressed())
                {
                    extCameraTurnCompass.position += Vector3.down * (Time.deltaTime * currentVerticalSpeed);
                }
                extCamera.transform.position = Vector3.Lerp(extCamera.transform.position, extCameraTurnCompass.transform.position, 3f * Time.deltaTime);
                extCamera.transform.rotation = Quaternion.Slerp(extCamera.transform.rotation, extCameraTurnCompass.rotation, 3f * Time.deltaTime);
            }
        }
    }
}
