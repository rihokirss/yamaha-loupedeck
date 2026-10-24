namespace Loupedeck.YamahaDirectPlugin;

// The SDK requires a ClientApplication companion even for a universal API-only plugin.
public sealed class YamahaDirectApplication : ClientApplication
{
    protected override string GetProcessName() => "";
    protected override string GetBundleName() => "";
}
