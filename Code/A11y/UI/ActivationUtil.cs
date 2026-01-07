using TLDAccessibility.A11y.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TLDAccessibility.A11y.UI
{
    internal static class ActivationUtil
    {
        public static bool Activate(GameObject target)
        {
            if (target == null)
            {
                return false;
            }

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                A11yLogger.Warning("Activation failed: EventSystem not found.");
                return false;
            }

            BaseEventData baseEventData = new BaseEventData(eventSystem);
            bool handled = ExecuteEvents.Execute(target, baseEventData, ExecuteEvents.submitHandler);
            if (handled)
            {
                return true;
            }

            PointerEventData pointerEvent = new PointerEventData(eventSystem);
            handled = ExecuteEvents.Execute(target, pointerEvent, ExecuteEvents.pointerClickHandler);
            if (handled)
            {
                return true;
            }

            Button button = target.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.Invoke();
                return true;
            }

            Toggle toggle = target.GetComponent<Toggle>();
            if (toggle != null)
            {
                toggle.isOn = !toggle.isOn;
                return true;
            }

            A11yLogger.Warning($"Activation failed: no handler for {target.name}.");
            return false;
        }
    }
}
