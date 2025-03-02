using NomaiVR.ModConfig;
using NomaiVR.Player;
using UnityEngine;

namespace NomaiVR.UI
{
    public class HelmetFollowCameraRotation : MonoBehaviour
    {
        private Quaternion lastFrameRotation;
        private const float speed = 0.5f;
        private bool smoothEnabled = true;
        private bool snapEnabled = false;
        private bool snapTurnedLastFrame = false;

        private void Start()
        {
            lastFrameRotation = transform.rotation;

            RefreshEnabledSettings();

            ModSettings.OnConfigChange += RefreshEnabledSettings;
        }

        private void OnDestroy()
        {
            ModSettings.OnConfigChange -= RefreshEnabledSettings;
            PlayerBodyPosition.Behaviour.OnSnapTurn -= OnSnapTurn;
        }

        private void LateUpdate()
        {
            if (!Camera.main)
            {
                return;
            }

            var targetRotation = Camera.main.transform.rotation;

            if (!smoothEnabled)
            {
                transform.rotation = targetRotation;
            }
            else if (snapEnabled && snapTurnedLastFrame)
            {
                snapTurnedLastFrame = false;
                transform.rotation = targetRotation;
            }
            else
            {
                var difference = Mathf.Abs(Quaternion.Angle(lastFrameRotation, targetRotation));
                var step = speed * Time.unscaledDeltaTime * difference * difference;
                transform.rotation = Quaternion.RotateTowards(lastFrameRotation, targetRotation, step);
            }

            lastFrameRotation = transform.rotation;
        }

        private void RefreshEnabledSettings()
        {
            smoothEnabled = ModSettings.HudSmoothFollow;
            snapEnabled = ModSettings.SnapTurning;

            PlayerBodyPosition.Behaviour.OnSnapTurn -= OnSnapTurn;

            if (snapEnabled)
            {
                PlayerBodyPosition.Behaviour.OnSnapTurn += OnSnapTurn;
            }
        }

        private void OnSnapTurn()
        {
            snapTurnedLastFrame = true;
        }
    }
}