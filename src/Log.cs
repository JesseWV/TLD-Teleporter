namespace Teleporter;

internal static class Log
{
    private static MelonLogger.Instance? _instance;

    private static MelonLogger.Instance Instance => _instance ??= new MelonLogger.Instance(BuildInfo.Name, MelonLogger.DefaultMelonColor);

    public static void Bind(MelonLogger.Instance instance) => _instance = instance;

    public static void Msg(string text) => Instance.Msg(text);
    public static void Warning(string text) => Instance.Warning(text);
    public static void Error(string text) => Instance.Error(text);
}
