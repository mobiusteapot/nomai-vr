using System;
using NomaiVR.Hands;
using NomaiVR.Helpers;
using NomaiVR.ModConfig;
using UnityEngine;
using Valve.VR;

namespace NomaiVR.Player
{
    internal class PlayerBodyPosition : NomaiVRModule<PlayerBodyPosition.Behaviour, PlayerBodyPosition.Behaviour.Patch>
    {
        protected override bool IsPersistent => false;
        protected override OWScene[] Scenes => PlayableScenes;

        public class Behaviour : MonoBehaviour
        {
            private Transform cameraParent;
            private static Transform playArea;
            private static OWCamera playerCamera;
            private static Animator playerAnimator;
            private static OWRigidbody playerBody;
            private static PlayerCharacterController playerController;
            private static Autopilot autopilot;
            private readonly SteamVR_Action_Boolean recenterAction = SteamVR_Actions._default.Recenter;

            private static readonly SteamVR_Action_Vector2 turnAction = SteamVR_Actions._default.Look;
            private float turnCooldown = 0.05f;
            private float lastTurnTime;

            private static float snapTurnInputThreshold = 0.15f;
            // For testing. Need to move this to the settings menu.

            private static bool snapTurnEnabled = false;
            private static bool isTurnInCooldown = false;
            private static float snapTurnIncrement = 90f;

            internal void Start()
            {
                // This component is messing with our ability to read the VR camera's rotation.
                // Seems to be responsible for controlling the camera rotation with the mouse / joystick.
                var playerCameraController = Camera.main.GetComponent<PlayerCameraController>();
                if (playerCameraController)
                {
                    playerCameraController.enabled = false;
                }

                AdjustPlayerHeadPosition();
                SetupCamera();
                playerBody = Locator.GetPlayerBody();
                playerAnimator = playerBody.GetComponentInChildren<PlayerAnimController>()._animator;
                playerController = playerBody.GetComponent<PlayerCharacterController>();
                autopilot = playerBody.GetComponent<Autopilot>();

                CreateRecenterMenuEntry();
            }

            private static void AdjustPlayerHeadPosition()
            {
                var playerhead = PlayerHelper.PlayerHead;
                playerhead.localPosition = new Vector3(playerhead.localPosition.x, playerhead.localPosition.y, 0);
            }

            private void SetupCamera()
            {
                // Make an empty parent object for moving the camera around.
                playerCamera = Locator.GetPlayerCamera();
                cameraParent = new GameObject("VrCameraParent").transform;
                playArea = new GameObject("VrPlayArea").transform;
                playArea.parent = Locator.GetPlayerTransform();
                playArea.position = PlayerHelper.PlayerHead.position;
                playArea.rotation = PlayerHelper.PlayerHead.rotation;
                cameraParent.parent = playArea;
                cameraParent.localRotation = Quaternion.identity;
                playerCamera.transform.parent = cameraParent;
                playerCamera.gameObject.AddComponent<VRCameraManipulator>();

                MoveCameraToPlayerHead();
            }

            private void MoveCameraToPlayerHead()
            {
                var movement = PlayerHelper.PlayerHead.position - playerCamera.transform.position;
                cameraParent.position += movement;
            }

            private void CreateRecenterMenuEntry()
            {
                FindObjectOfType<PauseMenuManager>().AddPauseMenuAction("RECENTER VR", 2, MoveCameraToPlayerHead);
            }

            private void UpdateRecenter()
            {
                if (recenterAction.stateDown)
                {
                    MoveCameraToPlayerHead();
                }
            }

            internal void Update()
            {
                var cameraToHead = Vector3.ProjectOnPlane(PlayerHelper.PlayerHead.position - playerCamera.transform.position, PlayerHelper.PlayerHead.up);

                if ((cameraToHead.sqrMagnitude > 0.5f && ModSettings.PreventClipping) || cameraToHead.sqrMagnitude > 10f)
                {
                    MoveCameraToPlayerHead();
                }
                
                UpdateRecenter();

                if(!snapTurnEnabled)
                {
                    return;
                }
                if(Mathf.Abs(turnAction.axis.x) < snapTurnInputThreshold)
                {
                    Logs.Write("Should be able to snap turn again!", debugOnly: false);
                }
                // Check if time has passed and input stick was returned to neutral
                if(isTurnInCooldown && Time.time - lastTurnTime > turnCooldown && Mathf.Abs(turnAction.axis.x) < snapTurnInputThreshold)
                {
                    Logs.Write("Snap turn cooldown over!", debugOnly: false);
                    isTurnInCooldown = false;
                    lastTurnTime = Time.time;
                }
            }

            public class Patch : NomaiVRPatch
            {
                public override void ApplyPatches()
                {
                    Postfix<OWInput>(nameof(OWInput.GetAxisValue), nameof(PostGetAxisValue));
                    Postfix<PlayerCharacterController>(nameof(PlayerCharacterController.UpdateTurning), nameof(PostCharacterTurning));
                    Postfix<JetpackThrusterController>(nameof(JetpackThrusterController.FixedUpdate), nameof(PostThrusterUpdate));
                    Prefix<OWCamera>("set_" + nameof(OWCamera.fieldOfView), nameof(PatchOwCameraFOV));
                    Prefix<OWCamera>("get_" + nameof(OWCamera.fieldOfView), nameof(GetOwCameraFOVScaled));
                }

                private static void PostThrusterUpdate(Vector3 ____rotationalInput)
                {
                    if (!PlayerState.InZeroG() || ____rotationalInput.sqrMagnitude != 0 || autopilot.IsMatchingVelocity())
                    {
                        return;
                    }

                    playerBody.SetAngularVelocity(Vector3.zero);

                    PatchTurning(rotation => playerBody.MoveToRotation(rotation));
                }

                private static void PostCharacterTurning()
                {
                    PatchTurning(rotation => playerBody.transform.rotation = rotation);
                }

                private static void PatchTurning(Action<Quaternion> rotationSetter)
                {
                    var runSpeedX = playerAnimator.GetFloat("RunSpeedX");
                    var runSpeedY = playerAnimator.GetFloat("RunSpeedY");
                    var isStoppedOnGround = playerController.IsGrounded() && (runSpeedX + runSpeedY == 0);
                    var isControllerOriented = !(isStoppedOnGround) && ModSettings.ControllerOrientedMovement;

                    if((OWInput.GetInputMode() != InputMode.Character))
                    {
                        return;
                    }


                    if(!snapTurnEnabled)
                    {

                        var rotationSource = isControllerOriented ? LaserPointer.Behaviour.MovementLaser : playerCamera.transform;
                        var magnitude = 0f;
                        if(!isControllerOriented)
                        {
                            var magnitudeUp = 1 - Vector3.ProjectOnPlane(rotationSource.transform.up, playerBody.transform.up).magnitude;
                            var magnitudeForward = 1 - Vector3.ProjectOnPlane(rotationSource.transform.up, playerBody.transform.right).magnitude;
                            magnitude = magnitudeUp + magnitudeForward;

                            if(magnitude < 0.3f)
                            {
                                return;
                            }
                        }

                        var fromTo = Quaternion.FromToRotation(playerBody.transform.forward, Vector3.ProjectOnPlane(rotationSource.transform.forward, playerBody.transform.up));
                        var targetRotation = fromTo * playerBody.transform.rotation;
                        var inverseRotation = Quaternion.Inverse(fromTo) * playArea.rotation;

                        if(isControllerOriented)
                        {
                            playArea.rotation = inverseRotation;
                            rotationSetter(targetRotation);
                        }
                        else
                        {
                            var maxDegreesDelta = magnitude * 3f;
                            playArea.rotation = Quaternion.RotateTowards(playArea.rotation, inverseRotation, maxDegreesDelta);
                            rotationSetter(Quaternion.RotateTowards(playerBody.transform.rotation, targetRotation, maxDegreesDelta));
                        }
                    }
                    else
                    {

                        float turnInput = turnAction.axis.x;
                        if(!isTurnInCooldown && Mathf.Abs(turnInput) > snapTurnInputThreshold)
                        {
                            isTurnInCooldown = true;
                            float sign = Mathf.Sign(turnInput);
                            Quaternion snapRotation = Quaternion.AngleAxis(snapTurnIncrement * sign, playerBody.transform.up);
                            var fromTo = Quaternion.FromToRotation(playerBody.transform.forward, snapRotation * playerBody.transform.forward);
                            var targetRotation = fromTo * playerBody.transform.rotation;
                            var inverseRotation = Quaternion.Inverse(fromTo) * playArea.rotation;

                            //playArea.rotation = inverseRotation;
                            rotationSetter(targetRotation);
                        }

                    }
                }

                // Override vanilla input handling for turning (if snap turn is enabled)
                private static void PostGetAxisValue(ref Vector2 __result, IInputCommands command, InputMode mask)
                {
                    if(!snapTurnEnabled || (OWInput.GetInputMode() != InputMode.Character))
                    {
                        return;
                    }
                    // If the command is for turning, set the axis value to zero
                    if(command.CommandType == InputConsts.InputCommandType.LOOK)
                    {
                        __result = Vector2.zero;
                    }
                }

                private static bool PatchOwCameraFOV(OWCamera __instance)
                {
                    //Prevents changing the fov of VR cameras
                    //This prevents log spams in projection pools
                    return !__instance.mainCamera.stereoEnabled;
                }

                private static bool GetOwCameraFOVScaled(OWCamera __instance, ref float __result)
                {
                    //Returns FOV scaled by scale factor
                    if (__instance.mainCamera.stereoEnabled) __result = CameraHelper.GetScaledFieldOfView(__instance.mainCamera);
                    return !__instance.mainCamera.stereoEnabled;
                }
            }

        }
    }
}
