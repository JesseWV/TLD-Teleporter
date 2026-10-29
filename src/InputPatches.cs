namespace Teleporter;

[HarmonyPatch(typeof(InterfaceManager), "ShouldEnableMousePointer")]
internal static class ShowPointerWhilePickerOpen
{
    private static void Postfix(ref bool __result)
    {
        if (Picker.IsOpen) __result = true;
    }
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.GetPauseMenuTogglePressed))]
internal static class NoPauseMenuWhilePickerOpen
{
    private static void Postfix(ref bool __result)
    {
        if (!Picker.SwallowEscape) return;
        if (__result) Picker.BackSeenFrame = Time.frameCount;
        __result = false;
    }
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.GetEscapePressed))]
internal static class NoEscapeWhilePickerOpen
{
    private static void Postfix(ref bool __result)
    {
        if (!Picker.SwallowEscape) return;
        if (__result) Picker.BackSeenFrame = Time.frameCount;
        __result = false;
    }
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.GetCrouchPressed))]
internal static class NoCrouchPressWhilePickerOpen
{
    private static void Postfix(ref bool __result)
    {
        if (Picker.IsOpen) __result = false;
    }
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.CanExecuteCrouchAction))]
internal static class NoCrouchActionWhilePickerOpen
{
    private static void Postfix(ref bool __result)
    {
        if (Picker.IsOpen) __result = false;
    }
}
