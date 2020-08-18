﻿using UnityEngine;

namespace NomaiVR
{
    internal class CameraMaskFix : NomaiVRModule<CameraMaskFix.Behaviour, CameraMaskFix.Behaviour.Patch>
    {
        protected override bool IsPersistent => false;
        protected override OWScene[] Scenes => PlayableScenes;

        public class Behaviour : MonoBehaviour
        {
            private OWCamera _camera;
            private static float _farClipPlane = -1;
            public static int cullingMask = -1;
            private static Behaviour _instance;
            private bool _isPaused;

            internal void Start()
            {
                _instance = this;

                _camera = Locator.GetPlayerCamera();
                _camera.postProcessingSettings.chromaticAberrationEnabled = false;
                _camera.postProcessingSettings.vignetteEnabled = false;
                _camera.postProcessingSettings.bloom.intensity = 0.15f;

                if (LoadManager.GetPreviousScene() == OWScene.TitleScreen && LoadManager.GetCurrentScene() == OWScene.SolarSystem)
                {
                    CloseEyes();
                }
            }

            internal void Update()
            {
                if (InputHelper.IsUIInteractionMode() && !_isPaused)
                {
                    _isPaused = true;
                    cullingMask = Camera.main.cullingMask;
                    Camera.main.cullingMask = LayerMask.GetMask("UI");
                }
                if (!InputHelper.IsUIInteractionMode() && _isPaused)
                {
                    _isPaused = false;
                    Camera.main.cullingMask = cullingMask;
                }
            }

            private void CloseEyesDelayed()
            {
                Invoke(nameof(CloseEyes), 3);
            }

            private void CloseEyes()
            {
                cullingMask = Camera.main.cullingMask;
                _farClipPlane = Camera.main.farClipPlane;
                Camera.main.cullingMask = LayerMask.GetMask("VisibleToPlayer", "UI");
                Camera.main.farClipPlane = 5;
                Locator.GetPlayerCamera().postProcessingSettings.eyeMaskEnabled = false;
            }

            private void OpenEyes()
            {
                Camera.main.cullingMask = cullingMask;
                Camera.main.farClipPlane = _farClipPlane;
            }

            public class Patch : NomaiVRPatch
            {
                public override void ApplyPatches()
                {
                    Postfix<Campfire>("StartFastForwarding", nameof(PostStartFastForwarding));

                    var openEyesMethod =
                        typeof(PlayerCameraEffectController)
                        .GetMethod("OpenEyes", new[] { typeof(float), typeof(AnimationCurve) });
                    Postfix(openEyesMethod, nameof(PostOpenEyes));

                    Postfix<PlayerCameraEffectController>("CloseEyes", nameof(PostCloseEyes));
                }

                private static void PostStartFastForwarding()
                {
                    Locator.GetPlayerCamera().enabled = true;
                }

                private static void PostOpenEyes()
                {
                    _instance.OpenEyes();
                }

                private static void PostCloseEyes()
                {
                    _instance.CloseEyesDelayed();
                }
            }
        }
    }
}
